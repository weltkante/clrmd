﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace Microsoft.Diagnostics.Runtime.Desktop
{
    internal class PrimitiveType : BaseDesktopHeapType
    {
        public PrimitiveType(DesktopGCHeap heap, ClrElementType type)
            : base(0, heap, heap.DesktopRuntime.ErrorModule, 0)
        {
            ElementType = type;
        }

        public override int BaseSize => DesktopInstanceField.GetSize(this, ElementType);
        public override ClrType BaseType => DesktopHeap.ValueType;
        public override int ElementSize => 0;
        public override ClrHeap Heap => DesktopHeap;
        public override IList<ClrInterface> Interfaces => Array.Empty<ClrInterface>();
        public override bool IsAbstract => false;
        public override bool IsFinalizable => false;
        public override bool IsInterface => false;
        public override bool IsInternal => false;
        public override bool IsPrivate => false;
        public override bool IsProtected => false;
        public override bool IsPublic => false;
        public override bool IsSealed => false;
        public override uint MetadataToken => 0;
        public override ulong MethodTable => 0;
        public override string Name => GetElementTypeName();

        public override IEnumerable<ulong> EnumerateMethodTables()
        {
            return Array.Empty<ulong>();
        }

        public override void EnumerateRefsOfObject(ulong objRef, Action<ulong, int> action)
        {
        }

        public override void EnumerateRefsOfObjectCarefully(ulong objRef, Action<ulong, int> action)
        {
        }

        public override ulong GetArrayElementAddress(ulong objRef, int index)
        {
            throw new InvalidOperationException();
        }

        public override object GetArrayElementValue(ulong objRef, int index)
        {
            throw new InvalidOperationException();
        }

        public override int GetArrayLength(ulong objRef)
        {
            throw new InvalidOperationException();
        }

        public override ClrInstanceField GetFieldByName(string name)
        {
            return null;
        }

        public override bool GetFieldForOffset(int fieldOffset, bool inner, out ClrInstanceField childField, out int childFieldOffset)
        {
            childField = null;
            childFieldOffset = 0;
            return false;
        }

        public override ulong GetSize(ulong objRef)
        {
            return 0;
        }

        public override ClrStaticField GetStaticFieldByName(string name)
        {
            return null;
        }

        internal override ulong GetModuleAddress(ClrAppDomain domain)
        {
            return 0;
        }

        public override IList<ClrInstanceField> Fields => Array.Empty<ClrInstanceField>();

        private string GetElementTypeName()
        {
            return ElementType switch
            {
                ClrElementType.Boolean => "System.Boolean",
                ClrElementType.Char => "System.Char",
                ClrElementType.Int8 => "System.SByte",
                ClrElementType.UInt8 => "System.Byte",
                ClrElementType.Int16 => "System.Int16",
                ClrElementType.UInt16 => "System.UInt16",
                ClrElementType.Int32 => "System.Int32",
                ClrElementType.UInt32 => "System.UInt32",
                ClrElementType.Int64 => "System.Int64",
                ClrElementType.UInt64 => "System.UInt64",
                ClrElementType.Float => "System.Single",
                ClrElementType.Double => "System.Double",
                ClrElementType.NativeInt => "System.IntPtr",
                ClrElementType.NativeUInt => "System.UIntPtr",
                ClrElementType.Struct => "Sytem.ValueType",
                _ => ElementType.ToString(),
            };
        }
    }
}