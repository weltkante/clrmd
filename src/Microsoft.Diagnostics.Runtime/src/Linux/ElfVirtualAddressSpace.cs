﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Diagnostics.Runtime.Linux
{
    internal class ELFVirtualAddressSpace : IAddressSpace
    {
        private readonly ElfProgramHeader[] _segments;
        private readonly IAddressSpace _addressSpace;

        public string Name => _addressSpace.Name;

        public ELFVirtualAddressSpace(IReadOnlyList<ElfProgramHeader> segments, IAddressSpace addressSpace)
        {
            Length = segments.Max(s => s.VirtualAddress + s.VirtualSize);
            // FileSize == 0 means the segment isn't backed by any data
            _segments = segments.Where((programHeader) => programHeader.FileSize > 0).ToArray();
            _addressSpace = addressSpace;
        }

        public long Length { get; }

        public int Read(long position, Span<byte> buffer)
        {
            int bytesRead = 0;
            while (bytesRead != buffer.Length)
            {
                int i = 0;
                for (; i < _segments.Length; i++)
                {
                    long virtualAddress = _segments[i].VirtualAddress;
                    long virtualSize = _segments[i].VirtualSize;

                    long upperAddress = virtualAddress + virtualSize;
                    if (virtualAddress <= position && position < upperAddress)
                    {
                        int bytesToReadRange = (int)Math.Min(buffer.Length - bytesRead, upperAddress - position);
                        long segmentOffset = position - virtualAddress;

                        Span<byte> slice = buffer.Slice(bytesRead, bytesToReadRange);
                        int bytesReadRange = _segments[i].AddressSpace.Read(segmentOffset, slice);
                        if (bytesReadRange == 0)
                            goto done;

                        position += bytesReadRange;
                        bytesRead += bytesReadRange;
                        break;
                    }
                }

                if (i == _segments.Length)
                    break;
            }

        done:
            buffer.Slice(bytesRead, bytesRead - buffer.Length).Clear();
            return bytesRead;
        }
    }
}
