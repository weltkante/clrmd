﻿using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime
{
    static class ExtensionMethods
    {
        public static int Read(this Stream stream, Span<byte> span)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(span.Length);
            try
            {
                int numRead = stream.Read(buffer, 0, span.Length);
                new ReadOnlySpan<byte>(buffer, 0, numRead).CopyTo(span);
                return numRead;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static ulong AsPointer(this Span<byte> span, int index = 0)
        {
            if (index != 0)
                span = span.Slice(index * IntPtr.Size, IntPtr.Size);

            Debug.Assert(span.Length >= IntPtr.Size);
            return IntPtr.Size == 4
                ? MemoryMarshal.Read<uint>(span)
                : MemoryMarshal.Read<ulong>(span);
        }

        public static int AsInt32(this Span<byte> span)
        {
            Debug.Assert(span.Length >= sizeof(int));
            return MemoryMarshal.Read<int>(span);
        }

        public static uint AsUInt32(this Span<byte> span)
        {
            Debug.Assert(span.Length >= sizeof(uint));
            return MemoryMarshal.Read<uint>(span);
        }

        public static ulong AsUInt64(this Span<byte> span)
        {
            Debug.Assert(span.Length >= sizeof(ulong));
            return MemoryMarshal.Read<ulong>(span);
        }
    }
}
