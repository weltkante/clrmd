﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// In v4.5, this class supports multithreading.
    /// </summary>
    public partial class DefaultSymbolLocator : SymbolLocator
    {
        private static readonly Dictionary<FileEntry, Task<string>> s_files = new Dictionary<FileEntry, Task<string>>();
        private static readonly Dictionary<string, Task> s_copy = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Attempts to locate a binary via the symbol server.  This function will then copy the file
        /// locally to the symbol cache and return the location of the local file on disk.
        /// </summary>
        /// <param name="fileName">The filename that the binary is indexed under.</param>
        /// <param name="buildTimeStamp">The build timestamp the binary is indexed under.</param>
        /// <param name="imageSize">The image size the binary is indexed under.</param>
        /// <param name="checkProperties">Whether or not to validate the properties of the binary after download.</param>
        /// <returns>A full path on disk (local) of where the binary was copied to, null if it was not found.</returns>
        public override async Task<string> FindBinaryAsync(string fileName, int buildTimeStamp, int imageSize, bool checkProperties = true)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentNullException(nameof(fileName));

            string simpleFilename = Path.GetFileName(fileName);
            FileEntry fileEntry = new FileEntry(simpleFilename, buildTimeStamp, imageSize);

            HashSet<FileEntry> missingFiles = _missingFiles;

            Task<string> task = null;
            lock (s_files)
            {
                if (IsMissing(missingFiles, fileEntry))
                    return null;

                if (!s_files.TryGetValue(fileEntry, out task))
                    task = s_files[fileEntry] = DownloadFileWorker(fileName, simpleFilename, buildTimeStamp, imageSize, checkProperties);
            }

            // If we failed to find the file, we need to clear out the empty task, since the user could
            // change symbol paths and we need s_files to only contain positive results.
            string result = await task.ConfigureAwait(false);
            if (result == null)
                ClearFailedTask(s_files, task, missingFiles, fileEntry);

            return result;
        }

        private static void ClearFailedTask<T>(Dictionary<T, Task<string>> tasks, Task<string> task, HashSet<T> missingFiles, T fileEntry)
        {
            lock (tasks)
            {
                if (tasks.TryGetValue(fileEntry, out Task<string> tmp) && tmp == task)
                    tasks.Remove(fileEntry);

                lock (missingFiles)
                    missingFiles.Add(fileEntry);
            }
        }

        private async Task<string> DownloadFileWorker(string fileFullPath, string fileSimpleName, int buildTimeStamp, int imageSize, bool checkProperties)
        {
            string fileIndexPath = GetIndexPath(fileSimpleName, buildTimeStamp, imageSize);
            string cachePath = Path.Combine(SymbolCache, fileIndexPath);

            bool match(string file) => ValidateBinary(file, buildTimeStamp, imageSize, checkProperties);
            string result = CheckLocalPaths(fileFullPath, fileSimpleName, cachePath, match);
            if (result != null)
            {
                Trace("Found '{0}' locally on path '{1}'.", fileSimpleName, result);
                return result;
            }

            result = await SearchSymbolServerForFile(fileSimpleName, fileIndexPath, match).ConfigureAwait(false);
            return result;
        }

        private static string CheckLocalPaths(string fullName, string simpleName, string fullDestPath, Func<string, bool> matches)
        {
            // We were given a full path instead of simply "foo.bar".
            if (fullName != simpleName)
            {
                if (matches(fullName))
                    return fullName;
            }

            // Check the target path too.
            if (File.Exists(fullDestPath))
            {
                if (matches(fullDestPath))
                    return fullDestPath;

                // We somehow got a bad file here...this shouldn't have happened.
                File.Delete(fullDestPath);
            }

            return null;
        }

        private async Task<string> SearchSymbolServerForFile(string fileSimpleName, string fileIndexPath, Func<string, bool> match)
        {
            List<Task<string>> tasks = new List<Task<string>>();
            foreach (SymPathElement element in SymPathElement.GetElements(SymbolPath))
            {
                if (element.IsSymServer)
                {
                    tasks.Add(TryGetFileFromServerAsync(element.Target, fileIndexPath, element.Cache ?? SymbolCache));
                }
                else
                {
                    string fullDestPath = Path.Combine(element.Cache ?? SymbolCache, fileIndexPath);
                    string sourcePath = Path.Combine(element.Target, fileSimpleName);
                    tasks.Add(CheckAndCopyRemoteFile(sourcePath, fullDestPath, match));
                }
            }

            string result = await GetFirstNonNullResult(tasks).ConfigureAwait(false);
            return result;
        }

        private static async Task<T> GetFirstNonNullResult<T>(List<Task<T>> tasks)
            where T : class
        {
            while (tasks.Count > 0)
            {
                Task<T> task = await Task.WhenAny(tasks).ConfigureAwait(false);

                T result = task.Result;
                if (result != null)
                    return result;

                if (tasks.Count == 1)
                    break;

                tasks.Remove(task);
            }

            return null;
        }

        private async Task<string> CheckAndCopyRemoteFile(string sourcePath, string fullDestPath, Func<string, bool> matches)
        {
            if (!matches(sourcePath))
                return null;

            try
            {
                using (Stream stream = File.OpenRead(sourcePath))
                    await CopyStreamToFileAsync(stream, sourcePath, fullDestPath, stream.Length).ConfigureAwait(false);

                return fullDestPath;
            }
            catch (Exception e)
            {
                Trace("Error copying file '{0}' to '{1}': {2}", sourcePath, fullDestPath, e);
            }

            return null;
        }

        private async Task<string> TryGetFileFromServerAsync(string urlForServer, string fileIndexPath, string cache)
        {
            string fullDestPath = Path.Combine(cache, fileIndexPath);
            Debug.Assert(!string.IsNullOrWhiteSpace(cache));
            if (string.IsNullOrWhiteSpace(urlForServer))
                return null;

            // There are three ways symbol files can be indexed.  Start looking for each one.

            // First, check for the compressed location.  This is the one we really want to download.
            string compressedFilePath = fileIndexPath.Substring(0, fileIndexPath.Length - 1) + "_";
            string compressedFileTarget = Path.Combine(cache, compressedFilePath);

            TryDeleteFile(compressedFileTarget);
            Task<string> compressedFilePathDownload = GetPhysicalFileFromServerAsync(urlForServer, compressedFilePath, compressedFileTarget);

            // Second, check if the raw file itself is indexed, uncompressed.
            Task<string> rawFileDownload = GetPhysicalFileFromServerAsync(urlForServer, fileIndexPath, fullDestPath);

            // Last, check for a redirection link.
            string filePtrSigPath = Path.Combine(Path.GetDirectoryName(fileIndexPath), "file.ptr");
            Task<string> filePtrDownload = GetPhysicalFileFromServerAsync(urlForServer, filePtrSigPath, fullDestPath, true);

            // Handle compressed download.
            string result = await compressedFilePathDownload.ConfigureAwait(false);
            if (result != null)
            {
                try
                {
                    // Decompress it
                    Command.Run("Expand " + Command.Quote(result) + " " + Command.Quote(fullDestPath));
                    Trace($"Found '{Path.GetFileName(fileIndexPath)}' on server '{urlForServer}'.  Copied to '{fullDestPath}'.");
                    return fullDestPath;
                }
                catch (Exception e)
                {
                    Trace("Exception encountered while expanding file '{0}': {1}", result, e.Message);
                }
                finally
                {
                    if (File.Exists(result))
                        File.Delete(result);
                }
            }

            // Handle uncompressed download.
            result = await rawFileDownload.ConfigureAwait(false);
            if (result != null)
            {
                Trace($"Found '{Path.GetFileName(fileIndexPath)}' on server '{urlForServer}'.  Copied to '{result}'.");
                return result;
            }

            // Handle redirection case.
            string filePtrData = (await filePtrDownload.ConfigureAwait(false) ?? "").Trim();
            if (filePtrData.StartsWith("PATH:"))
                filePtrData = filePtrData.Substring(5);

            if (!filePtrData.StartsWith("MSG:") && File.Exists(filePtrData))
            {
                try
                {
                    using (FileStream input = File.OpenRead(filePtrData))
                        await CopyStreamToFileAsync(input, filePtrSigPath, fullDestPath, input.Length).ConfigureAwait(false);

                    Trace($"Found '{Path.GetFileName(fileIndexPath)}' on server '{urlForServer}'.  Copied to '{fullDestPath}'.");
                    return fullDestPath;
                }
                catch (Exception)
                {
                    Trace("Error copying from file.ptr: content '{0}' from '{1}' to '{2}'.", filePtrData, filePtrSigPath, fullDestPath);
                }
            }
            else if (!string.IsNullOrWhiteSpace(filePtrData))
            {
                Trace("Error resolving file.ptr: content '{0}' from '{1}'.", filePtrData, filePtrSigPath);
            }

            Trace($"No file matching '{Path.GetFileName(fileIndexPath)}' found on server '{urlForServer}'.");
            return null;
        }

        private static void TryDeleteFile(string file)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // Ignore failure here.
                }
            }
        }

        private async Task<string> GetPhysicalFileFromServerAsync(string serverPath, string fileIndexPath, string fullDestPath, bool returnContents = false)
        {
            if (string.IsNullOrEmpty(serverPath))
                return null;

            if (File.Exists(fullDestPath))
            {
                if (returnContents)
                    return File.ReadAllText(fullDestPath);

                return fullDestPath;
            }

            if (IsHttp(serverPath))
            {
                string fullUri = serverPath + "/" + fileIndexPath.Replace('\\', '/');
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(new Uri(fullUri));
                    req.UserAgent = "Microsoft-Symbol-Server/6.13.0009.1140";
                    req.Timeout = Timeout;
                    WebResponse response = await req.GetResponseAsync().ConfigureAwait(false);
                    using Stream fromStream = response.GetResponseStream();
                    if (returnContents)
                    {
                        using StreamReader stream = new StreamReader(fromStream);
                        return await stream.ReadToEndAsync().ConfigureAwait(false);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath));
                    await CopyStreamToFileAsync(fromStream, fullUri, fullDestPath, response.ContentLength).ConfigureAwait(false);
                    Trace("Found '{0}' at '{1}'.  Copied to '{2}'.", Path.GetFileName(fileIndexPath), fullUri, fullDestPath);
                    return fullDestPath;
                }
                catch (WebException)
                {
                    // Is probably just a 404, which happens normally.
                    return null;
                }
                catch (Exception e)
                {
                    Trace("Probe of {0} failed: {1}", fullUri, e.Message);
                    return null;
                }
            }

            string fullSrcPath = Path.Combine(serverPath, fileIndexPath);
            if (!File.Exists(fullSrcPath))
                return null;

            if (returnContents)
            {
                try
                {
                    return File.ReadAllText(fullSrcPath);
                }
                catch
                {
                    return "";
                }
            }

            using (FileStream fs = File.OpenRead(fullSrcPath))
                await CopyStreamToFileAsync(fs, fullSrcPath, fullDestPath, fs.Length).ConfigureAwait(false);

            return fullDestPath;
        }

        private static bool IsHttp(string server)
        {
            return server.StartsWith("http:", StringComparison.CurrentCultureIgnoreCase) || server.StartsWith("https:", StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>
        /// Clear missing file/pdb cache
        /// </summary>
        protected override void SymbolPathOrCacheChanged()
        {
            _missingFiles = new HashSet<FileEntry>();
            _missingPdbs = new HashSet<PdbEntry>();
        }

        private static bool IsMissing<T>(HashSet<T> entries, T entry)
        {
            lock (entries)
                return entries.Contains(entry);
        }

        /// <summary>
        /// Copies the given file from the input stream into fullDestPath.
        /// </summary>
        /// <param name="stream">The input stream to copy the file from.</param>
        /// <param name="fullSrcPath">The source of this file.  This is for informational/logging purposes and shouldn't be opened directly.</param>
        /// <param name="fullDestPath">The destination path of where the file should go on disk.</param>
        /// <param name="size">The length of the given file.  (Also for informational purposes, do not use this as part of a copy loop.</param>
        /// <returns>A task indicating when the copy is completed.</returns>
        protected override void CopyStreamToFile(Stream stream, string fullSrcPath, string fullDestPath, long size)
        {
            Task task = Task.Run(async () => { await CopyStreamToFileAsync(stream, fullSrcPath, fullDestPath, size).ConfigureAwait(false); });
            task.Wait();
        }

        /// <summary>
        /// Copies the given file from the input stream into fullDestPath.
        /// </summary>
        /// <param name="input">The input stream to copy the file from.</param>
        /// <param name="fullSrcPath">The source of this file.  This is for informational/logging purposes and shouldn't be opened directly.</param>
        /// <param name="fullDestPath">The destination path of where the file should go on disk.</param>
        /// <param name="size">The length of the given file.  (Also for informational purposes, do not use this as part of a copy loop.</param>
        /// <returns>A task indicating when the copy is completed.</returns>
        protected override async Task CopyStreamToFileAsync(Stream input, string fullSrcPath, string fullDestPath, long size)
        {
            if (input == null)
                throw new ArgumentNullException(nameof(input));

            Directory.CreateDirectory(Path.GetDirectoryName(fullDestPath));

            Task result;
            FileStream output = null;
            try
            {
                lock (s_copy)
                {
                    if (!s_copy.TryGetValue(fullDestPath, out result))
                    {
                        if (File.Exists(fullDestPath))
                            return;

                        try
                        {
                            Trace("Copying '{0}' from '{1}' to '{2}'.", Path.GetFileName(fullDestPath), fullSrcPath, fullDestPath);

                            output = new FileStream(fullDestPath, FileMode.CreateNew);
                            s_copy[fullDestPath] = result = input.CopyToAsync(output);
                        }
                        catch (Exception e)
                        {
                            Trace("Encountered an error while attempting to copy '{0} to '{1}': {2}", fullSrcPath, fullDestPath, e.Message);
                        }
                    }
                }

                await result.ConfigureAwait(false);
            }
            finally
            {
                if (output != null)
                    output.Dispose();
            }
        }

        private string GetFileEntry(FileEntry entry)
        {
            lock (s_files)
            {
                if (s_files.TryGetValue(entry, out Task<string> task))
                    return task.Result;
            }

            return null;
        }

        private void SetFileEntry(HashSet<FileEntry> missingFiles, FileEntry entry, string value)
        {
            if (value != null)
            {
                lock (s_files)
                {
                    if (!s_files.ContainsKey(entry))
                    {
                        Task<string> task = new Task<string>(() => value);
                        s_files[entry] = task;
                        task.Start();
                    }
                }
            }
            else
            {
                lock (missingFiles)
                    missingFiles.Add(entry);
            }
        }

        internal override void PrefetchBinary(string name, int timestamp, int imagesize)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(name))
                    new Task(async () => await FindBinaryAsync(name, timestamp, imagesize, true).ConfigureAwait(false)).Start();
            }
            catch (Exception e)
            {
                // Background fetching binaries should never cause an exception that will tear down the process
                // (which would be the case since this is done on a background worker thread with no one around
                // to handle it).  We will swallow all exceptions here, but fail in debug builds.
                Debug.Fail(e.ToString());
            }
        }
    }
}