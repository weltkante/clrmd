﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#pragma warning disable 649

namespace Microsoft.Diagnostics.Runtime.Utilities
{
    /// <summary>
    /// SymPathElement represents the text between the semicolons in a symbol path.  It can be a symbol server specification or a simple directory path.
    /// SymPathElement follows functional conventions.  After construction everything is read-only.
    /// </summary>
    public class SymPathElement
    {
        /// <summary>
        /// returns a list of SymPathElements from a semicolon delimited string representing a symbol path
        /// </summary>
        public static List<SymPathElement> GetElements(string symbolPath)
        {
            string[] entries = (symbolPath ?? "").Split(';');
            List<SymPathElement>  result = new List<SymPathElement>(entries.Length);

            foreach (string element in entries)
                if (!string.IsNullOrEmpty(element))
                    result.Add(new SymPathElement(element));

            return result;
        }

        /// <summary>
        /// returns true if this element of the symbol server path a symbol server specification
        /// </summary>
        public bool IsSymServer { get; }
        /// <summary>
        /// returns true if this element of the symbol server path is a local cache specification
        /// </summary>
        public bool IsCache { get; }
        /// <summary>
        /// returns the local cache for a symbol server specifcation.  returns null if not specified
        /// </summary>
        public string Cache { get; set; }
        /// <summary>
        /// returns location to look for symbols.  This is either a directory specification or an URL (for symbol servers)
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// IsRemote returns true if it looks like the target is not on the local machine.
        /// </summary>
        public bool IsRemote
        {
            get
            {
                if (Target != null)
                {
                    if (Target.StartsWith(@"\\"))
                        return true;

                    // We assume drive letters from the back of the alphabet are remote.  
                    if (2 <= Target.Length && Target[1] == ':')
                    {
                        char driveLetter = char.ToUpperInvariant(Target[0]);
                        if ('T' <= driveLetter && driveLetter <= 'Z')
                            return true;
                    }
                }

                if (!IsSymServer)
                    return false;
                if (Cache != null)
                    return true;
                if (Target == null)
                    return false;

                if (Target.StartsWith("http:/", StringComparison.OrdinalIgnoreCase))
                    return true;

                return false;
            }
        }

        /// <summary>
        /// returns the string repsentation for the symbol server path element (e.g. SRV*c:\temp*\\symbols\symbols)
        /// </summary>
        public override string ToString()
        {
            if (IsSymServer)
            {
                string ret = "SRV";
                if (Cache != null)
                    ret += "*" + Cache;
                if (Target != null)
                    ret += "*" + Target;
                return ret;
            }

            return Target;
        }

        /// <summary>
        /// Implements object interface
        /// </summary>
        public override bool Equals(object obj)
        {
            if (!(obj is SymPathElement asSymPathElem))
                return false;

            return
                Target == asSymPathElem.Target &&
                Cache == asSymPathElem.Cache &&
                IsSymServer == asSymPathElem.IsSymServer;
        }

        /// <summary>
        /// Implements object interface
        /// </summary>
        public override int GetHashCode()
        {
            return Target.GetHashCode();
        }

        internal SymPathElement InsureHasCache(string defaultCachePath)
        {
            if (!IsSymServer)
                return this;
            if (Cache != null)
                return this;
            if (Target == defaultCachePath)
                return this;

            return new SymPathElement(true, defaultCachePath, Target);
        }

        internal SymPathElement LocalOnly()
        {
            if (!IsRemote)
                return this;
            if (Cache != null)
                return new SymPathElement(true, null, Cache);

            return null;
        }

        /// <summary>
        /// returns a new SymPathElement with the corresponding properties initialized
        /// </summary>
        public SymPathElement(bool isSymServer, string cache, string target)
        {
            IsSymServer = isSymServer;
            Cache = cache;
            Target = target;
        }

        internal SymPathElement(string strElem)
        {
            Match m = Regex.Match(strElem, @"^\s*(SRV\*|http:)((\s*.*?\s*)\*)?\s*(.*?)\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                IsSymServer = true;
                Cache = m.Groups[3].Value;
                if (m.Groups[1].Value.Equals("http:", StringComparison.CurrentCultureIgnoreCase))
                    Target = "http:" + m.Groups[4].Value;
                else
                    Target = m.Groups[4].Value;
                if (Cache.Length == 0)
                    Cache = null;
                if (Target.Length == 0)
                    Target = null;
                return;
            }

            m = Regex.Match(strElem, @"^\s*CACHE\*(.*?)\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                IsCache = true;
                Cache = m.Groups[1].Value;
            }
            else
                Target = strElem.Trim();
        }
    }
}