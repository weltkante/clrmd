﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Microsoft.Diagnostics.Runtime.DacInterface
{
    /// <summary>
    /// This is an undocumented, untested, and unsupported interface.  Do not use.
    /// </summary>
    public unsafe class ClrDataProcess : CallableCOMWrapper
    {
        private static Guid IID_IXCLRDataProcess = new Guid("5c552ab6-fc09-4cb3-8e36-22fa03c798b7");

        private IXCLRDataProcessVtable* VTable => (IXCLRDataProcessVtable*)_vtable;

        private FlushDelegate _flush;
        private GetTaskByOSThreadIDDelegate _getTask;
        private RequestDelegate _request;
        private StartEnumMethodInstancesByAddressDelegate _startEnum;
        private EnumMethodInstanceByAddressDelegate _enum;
        private EndEnumMethodInstancesByAddressDelegate _endEnum;

        private readonly DacLibrary _library;

        public ClrDataProcess(DacLibrary library, IntPtr pUnknown)
            : base(library?.OwningLibrary, ref IID_IXCLRDataProcess, pUnknown)
        {
            _library = library;
        }

        public ClrDataProcess(CallableCOMWrapper toClone) : base(toClone)
        {
        }

        public SOSDac GetSOSDacInterface()
        {
            IntPtr result = QueryInterface(ref SOSDac.IID_ISOSDac);
            if (result == IntPtr.Zero)
                return null;

            try
            {
                return new SOSDac(_library, result);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public SOSDac6 GetSOSDacInterface6()
        {
            IntPtr result = QueryInterface(ref SOSDac6.IID_ISOSDac6);
            if (result == IntPtr.Zero)
                return null;

            try
            {
                return new SOSDac6(_library, result);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public void Flush()
        {
            InitDelegate(ref _flush, VTable->Flush);
            _flush(Self);
        }

        public int Request(uint reqCode, ReadOnlySpan<byte> input, Span<byte> output)
        {
            InitDelegate(ref _request, VTable->Request);

            fixed (byte* pInput = input)
            fixed (byte* pOutput = output)
                return _request(Self, reqCode, input.Length, pInput, output.Length, pOutput);
        }

        public ClrStackWalk CreateStackWalk(uint id, uint flags)
        {
            InitDelegate(ref _getTask, VTable->GetTaskByOSThreadID);

            int hr = _getTask(Self, id, out IntPtr pUnkTask);
            if (hr != S_OK)
                return null;

            using ClrDataTask dataTask = new ClrDataTask(_library, pUnkTask);
            // There's a bug in certain runtimes where we will fail to release data deep in the runtime
            // when a C++ exception occurs while constructing a ClrDataStackWalk.  This is a workaround
            // for the largest of the leaks caused by this issue.
            //     https://github.com/Microsoft/clrmd/issues/47
            int count = AddRef();
            ClrStackWalk res = dataTask.CreateStackWalk(_library, flags);
            int released = Release();
            if (released == count && res == null)
                Release();
            return res;
        }

        public IEnumerable<ClrDataMethod> EnumerateMethodInstancesByAddress(ulong addr)
        {
            InitDelegate(ref _startEnum, VTable->StartEnumMethodInstancesByAddress);
            InitDelegate(ref _enum, VTable->EnumMethodInstanceByAddress);
            InitDelegate(ref _endEnum, VTable->EndEnumMethodInstancesByAddress);

            List<ClrDataMethod> result = new List<ClrDataMethod>(1);

            int hr = _startEnum(Self, addr, IntPtr.Zero, out ulong handle);
            if (hr != S_OK)
                return result;

            try
            {
                while ((hr = _enum(Self, ref handle, out IntPtr method)) == S_OK)
                    result.Add(new ClrDataMethod(_library, method));
            }
            finally
            {
                _endEnum(Self, handle);
            }

            return result;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int StartEnumMethodInstancesByAddressDelegate(IntPtr self, ulong address, IntPtr appDomain, out ulong handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EnumMethodInstanceByAddressDelegate(IntPtr self, ref ulong handle, out IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int EndEnumMethodInstancesByAddressDelegate(IntPtr self, ulong handle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RequestDelegate(
            IntPtr self,
            uint reqCode,
            int inBufferSize,
            byte* inBuffer,
            int outBufferSize,
            byte* outBuffer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int FlushDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetTaskByOSThreadIDDelegate(IntPtr self, uint id, out IntPtr pUnknownTask);
    }

#pragma warning disable CS0169
#pragma warning disable CS0649
#pragma warning disable IDE0051 // Remove unused private members
#pragma warning disable CA1823

    internal struct IXCLRDataProcessVtable
    {
        public readonly IntPtr Flush;
        private readonly IntPtr Unused_StartEnumTasks;
        private readonly IntPtr EnumTask;
        private readonly IntPtr EndEnumTasks;

        //(uint id, [Out, MarshalAs(UnmanagedType.IUnknown)] out object task);
        public readonly IntPtr GetTaskByOSThreadID;

        private readonly IntPtr GetTaskByUniqueID;
        private readonly IntPtr GetFlags;
        private readonly IntPtr IsSameObject;
        private readonly IntPtr GetManagedObject;
        private readonly IntPtr GetDesiredExecutionState;
        private readonly IntPtr SetDesiredExecutionState;
        private readonly IntPtr GetAddressType;
        private readonly IntPtr GetRuntimeNameByAddress;
        private readonly IntPtr StartEnumAppDomains;
        private readonly IntPtr EnumAppDomain;
        private readonly IntPtr EndEnumAppDomains;
        private readonly IntPtr GetAppDomainByUniqueID;
        private readonly IntPtr StartEnumAssemblie;
        private readonly IntPtr EnumAssembly;
        private readonly IntPtr EndEnumAssemblies;
        private readonly IntPtr StartEnumModules;
        private readonly IntPtr EnumModule;
        private readonly IntPtr EndEnumModules;
        private readonly IntPtr GetModuleByAddress;

        //(ulong address, [In, MarshalAs(UnmanagedType.Interface)] object appDomain, out ulong handle);
        public readonly IntPtr StartEnumMethodInstancesByAddress;

        //(ref ulong handle, [Out, MarshalAs(UnmanagedType.Interface)] out object method);
        public readonly IntPtr EnumMethodInstanceByAddress;

        //(ulong handle);
        public readonly IntPtr EndEnumMethodInstancesByAddress;
        private readonly IntPtr GetDataByAddress;
        private readonly IntPtr GetExceptionStateByExceptionRecord;
        private readonly IntPtr TranslateExceptionRecordToNotification;

        // (uint reqCode, uint inBufferSize, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] byte[] inBuffer, uint outBufferSize, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] byte[] outBuffer);
        public readonly IntPtr Request;
    }

    internal interface IXCLRDataProcess_
    {
        void Flush();

        void StartEnumTasks_do_not_use();
        void EnumTask_do_not_use();
        void EndEnumTasks_do_not_use();

        [PreserveSig]
        int GetTaskByOSThreadID(
            uint id,
            [Out][MarshalAs(UnmanagedType.IUnknown)]
            out object task);

        void GetTaskByUniqueID_do_not_use( /*[in] ULONG64 taskID, [out] IXCLRDataTask** task*/);
        void GetFlags_do_not_use( /*[out] ULONG32* flags*/);
        void IsSameObject_do_not_use( /*[in] IXCLRDataProcess* process*/);
        void GetManagedObject_do_not_use( /*[out] IXCLRDataValue** value*/);
        void GetDesiredExecutionState_do_not_use( /*[out] ULONG32* state*/);
        void SetDesiredExecutionState_do_not_use( /*[in] ULONG32 state*/);
        void GetAddressType_do_not_use( /*[in] CLRDATA_ADDRESS address, [out] CLRDataAddressType* type*/);

        void GetRuntimeNameByAddress_do_not_use(

            /*[in] CLRDATA_ADDRESS address, [in] ULONG32 flags, [in] ULONG32 bufLen, [out] ULONG32 *nameLen, [out, size_is(bufLen)] WCHAR nameBuf[], [out] CLRDATA_ADDRESS* displacement*/);

        void StartEnumAppDomains_do_not_use( /*[out] CLRDATA_ENUM* handle*/);
        void EnumAppDomain_do_not_use( /*[in, out] CLRDATA_ENUM* handle, [out] IXCLRDataAppDomain** appDomain*/);
        void EndEnumAppDomains_do_not_use( /*[in] CLRDATA_ENUM handle*/);
        void GetAppDomainByUniqueID_do_not_use( /*[in] ULONG64 id, [out] IXCLRDataAppDomain** appDomain*/);
        void StartEnumAssemblie_do_not_uses( /*[out] CLRDATA_ENUM* handle*/);
        void EnumAssembly_do_not_use( /*[in, out] CLRDATA_ENUM* handle, [out] IXCLRDataAssembly **assembly*/);
        void EndEnumAssemblies_do_not_use( /*[in] CLRDATA_ENUM handle*/);
        void StartEnumModules_do_not_use( /*[out] CLRDATA_ENUM* handle*/);
        void EnumModule_do_not_use( /*[in, out] CLRDATA_ENUM* handle, [out] IXCLRDataModule **mod*/);
        void EndEnumModules_do_not_use( /*[in] CLRDATA_ENUM handle*/);
        void GetModuleByAddress_do_not_use( /*[in] CLRDATA_ADDRESS address, [out] IXCLRDataModule** mod*/);

        [PreserveSig]
        int StartEnumMethodInstancesByAddress(
            ulong address,
            [In][MarshalAs(UnmanagedType.Interface)]
            object appDomain,
            out ulong handle);

        [PreserveSig]
        int EnumMethodInstanceByAddress(
            ref ulong handle,
            [Out][MarshalAs(UnmanagedType.Interface)]
            out object method);

        [PreserveSig]
        int EndEnumMethodInstancesByAddress(ulong handle);

        void GetDataByAddress_do_not_use(

            /*[in] CLRDATA_ADDRESS address, [in] ULONG32 flags, [in] IXCLRDataAppDomain* appDomain, [in] IXCLRDataTask* tlsTask, [in] ULONG32 bufLen, [out] ULONG32 *nameLen, [out, size_is(bufLen)] WCHAR nameBuf[], [out] IXCLRDataValue** value, [out] CLRDATA_ADDRESS* displacement*/);

        void GetExceptionStateByExceptionRecord_do_not_use( /*[in] EXCEPTION_RECORD64* record, [out] IXCLRDataExceptionState **exState*/);
        void TranslateExceptionRecordToNotification_do_not_use();

        [PreserveSig]
        int Request(
            uint reqCode,
            uint inBufferSize,
            [In][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
            byte[] inBuffer,
            uint outBufferSize,
            [Out][MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)]
            byte[] outBuffer);
    }
}