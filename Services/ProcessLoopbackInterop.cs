using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace PaDDY.Services
{
    // ── COM interop for Windows 10 2004+ process-specific loopback capture ──

    [StructLayout(LayoutKind.Sequential)]
    internal struct AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
    {
        public uint TargetProcessId;
        public PROCESS_LOOPBACK_MODE ProcessLoopbackMode;
    }

    internal enum PROCESS_LOOPBACK_MODE : uint
    {
        PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE = 0,
        PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1
    }

    internal enum AUDIOCLIENT_ACTIVATION_TYPE : uint
    {
        DEFAULT = 0,
        PROCESS_LOOPBACK = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        public AUDIOCLIENT_ACTIVATION_TYPE ActivationType;
        public AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)]  public ushort vt;
        [FieldOffset(2)]  public ushort wReserved1;
        [FieldOffset(4)]  public ushort wReserved2;
        [FieldOffset(6)]  public ushort wReserved3;
        [FieldOffset(8)]  public uint cbSize;
        [FieldOffset(16)] public IntPtr pBlobData;

        public static PROPVARIANT CreateBlob(byte[] data)
        {
            var pv = new PROPVARIANT();
            pv.vt = 65; // VT_BLOB
            pv.cbSize = (uint)data.Length;
            pv.pBlobData = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, pv.pBlobData, data.Length);
            return pv;
        }

        public void Free()
        {
            if (pBlobData != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pBlobData);
                pBlobData = IntPtr.Zero;
            }
        }
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceAsyncOperation
    {
        // Returns HRESULT via PreserveSig; activatedInterface is a raw IntPtr
        // so the CLR does NOT wrap it in an RCW (which would do QueryInterface).
        [PreserveSig]
        int GetActivateResult(
            out int activateResult,
            out IntPtr activatedInterface);
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    internal sealed class ActivateAudioInterfaceCompletionHandler
        : IActivateAudioInterfaceCompletionHandler
    {
        private readonly System.Threading.Tasks.TaskCompletionSource<IntPtr> _tcs = new();

        public System.Threading.Tasks.Task<IntPtr> Task => _tcs.Task;

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            int outerHr = activateOperation.GetActivateResult(out int hr, out IntPtr pInterface);
            if (outerHr < 0)
                _tcs.SetException(Marshal.GetExceptionForHR(outerHr)
                    ?? new COMException("GetActivateResult failed", outerHr));
            else if (hr < 0)
                _tcs.SetException(Marshal.GetExceptionForHR(hr)
                    ?? new COMException("ActivateAudioInterfaceAsync failed", hr));
            else
                _tcs.SetResult(pInterface);
        }
    }

    // ── Raw vtable wrappers — bypasses QueryInterface entirely ──────────────

    /// <summary>
    /// Wraps a raw IAudioClient COM pointer, calling methods via the vtable directly.
    /// IUnknown occupies slots 0-2; IAudioClient methods start at slot 3.
    /// </summary>
    internal sealed class AudioClientHandle : IDisposable
    {
        private IntPtr _ptr;

        // IAudioClient vtable slot indices (after IUnknown 0-2)
        private const int Slot_Initialize        = 3;
        private const int Slot_GetBufferSize     = 4;
        private const int Slot_GetStreamLatency  = 5;
        private const int Slot_GetCurrentPadding = 6;
        private const int Slot_IsFormatSupported = 7;
        private const int Slot_GetMixFormat      = 8;
        private const int Slot_GetDevicePeriod   = 9;
        private const int Slot_Start             = 10;
        private const int Slot_Stop              = 11;
        private const int Slot_Reset             = 12;
        private const int Slot_SetEventHandle    = 13;
        private const int Slot_GetService        = 14;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_Initialize(IntPtr self, int shareMode, int flags,
            long bufferDuration, long periodicity, IntPtr pFormat, ref Guid sessionGuid);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_GetMixFormat(IntPtr self, out IntPtr ppFormat);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_NoArgs(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_GetService(IntPtr self, ref Guid riid, out IntPtr ppv);

        /// <summary>
        /// Wraps a raw IAudioClient COM pointer. The pointer must already be
        /// the IAudioClient interface (not IUnknown). Caller transfers ownership.
        /// </summary>
        public AudioClientHandle(IntPtr audioClientPtr)
        {
            _ptr = audioClientPtr;
        }

        private IntPtr VtblSlot(int index)
        {
            IntPtr vtbl = Marshal.ReadIntPtr(_ptr);
            return Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
        }

        public void Initialize(int shareMode, int flags, long bufferDuration, IntPtr pFormat)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_Initialize>(VtblSlot(Slot_Initialize));
            Guid empty = Guid.Empty;
            Marshal.ThrowExceptionForHR(fn(_ptr, shareMode, flags, bufferDuration, 0, pFormat, ref empty));
        }

        public IntPtr GetMixFormat()
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_GetMixFormat>(VtblSlot(Slot_GetMixFormat));
            Marshal.ThrowExceptionForHR(fn(_ptr, out IntPtr p));
            return p;
        }

        public void Start()
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_NoArgs>(VtblSlot(Slot_Start));
            Marshal.ThrowExceptionForHR(fn(_ptr));
        }

        public void Stop()
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_NoArgs>(VtblSlot(Slot_Stop));
            fn(_ptr); // ignore HRESULT on stop
        }

        public void Reset()
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_NoArgs>(VtblSlot(Slot_Reset));
            fn(_ptr);
        }

        public IntPtr GetService(Guid riid)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_GetService>(VtblSlot(Slot_GetService));
            Marshal.ThrowExceptionForHR(fn(_ptr, ref riid, out IntPtr ppv));
            return ppv;
        }

        public void Dispose()
        {
            if (_ptr != IntPtr.Zero)
            {
                Marshal.Release(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    /// <summary>
    /// Wraps a raw IAudioCaptureClient COM pointer via vtable calls.
    /// IUnknown occupies slots 0-2; IAudioCaptureClient methods start at slot 3.
    /// </summary>
    internal sealed class CaptureClientHandle : IDisposable
    {
        private IntPtr _ptr;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_GetBuffer(IntPtr self, out IntPtr ppData,
            out int pNumFramesToRead, out int pdwFlags,
            out long pu64DevicePosition, out long pu64QPCPosition);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_ReleaseBuffer(IntPtr self, int numFramesRead);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int Del_GetNextPacketSize(IntPtr self, out int pNumFrames);

        public CaptureClientHandle(IntPtr ptr)
        {
            _ptr = ptr;
        }

        private IntPtr VtblSlot(int index)
        {
            IntPtr vtbl = Marshal.ReadIntPtr(_ptr);
            return Marshal.ReadIntPtr(vtbl, index * IntPtr.Size);
        }

        public int GetBuffer(out IntPtr data, out int frames, out int flags,
            out long devPos, out long qpcPos)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_GetBuffer>(VtblSlot(3));
            return fn(_ptr, out data, out frames, out flags, out devPos, out qpcPos);
        }

        public int ReleaseBuffer(int frames)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_ReleaseBuffer>(VtblSlot(4));
            return fn(_ptr, frames);
        }

        public int GetNextPacketSize(out int frames)
        {
            var fn = Marshal.GetDelegateForFunctionPointer<Del_GetNextPacketSize>(VtblSlot(5));
            return fn(_ptr, out frames);
        }

        public void Dispose()
        {
            if (_ptr != IntPtr.Zero)
            {
                Marshal.Release(_ptr);
                _ptr = IntPtr.Zero;
            }
        }
    }

    // ── Activation helper ───────────────────────────────────────────────────

    internal static class ProcessLoopbackInterop
    {
        private static readonly Guid IID_IAudioClient =
            new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

        private const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK =
            "VAD\\Process_Loopback";

        [DllImport("Mmdevapi.dll", PreserveSig = false)]
        private static extern void ActivateAudioInterfaceAsync(
            [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            ref PROPVARIANT activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);

        /// <summary>
        /// Activates an IAudioClient for process-specific loopback capture.
        /// Returns a raw vtable wrapper — no QueryInterface involved.
        /// Requires Windows 10 version 2004 (build 19041) or later.
        /// </summary>
        public static async System.Threading.Tasks.Task<AudioClientHandle>
            ActivateProcessLoopbackAsync(uint processId, bool includeProcessTree = true)
        {
            var loopbackParams = new AUDIOCLIENT_PROCESS_LOOPBACK_PARAMS
            {
                TargetProcessId = processId,
                ProcessLoopbackMode = includeProcessTree
                    ? PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
                    : PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE
            };

            var activationParams = new AUDIOCLIENT_ACTIVATION_PARAMS
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.PROCESS_LOOPBACK,
                ProcessLoopbackParams = loopbackParams
            };

            int structSize = Marshal.SizeOf(activationParams);
            byte[] blob = new byte[structSize];
            IntPtr ptr = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(activationParams, ptr, false);
                Marshal.Copy(ptr, blob, 0, structSize);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }

            var propVariant = PROPVARIANT.CreateBlob(blob);
            try
            {
                var handler = new ActivateAudioInterfaceCompletionHandler();
                ActivateAudioInterfaceAsync(
                    VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                    IID_IAudioClient,
                    ref propVariant,
                    handler,
                    out _);

                // The raw IntPtr IS the IAudioClient pointer — ActivateAudioInterfaceAsync
                // was told to activate IID_IAudioClient, so the returned pointer is that interface.
                // No QueryInterface or RCW involved.
                IntPtr pAudioClient = await handler.Task.ConfigureAwait(false);
                return new AudioClientHandle(pAudioClient);
            }
            finally
            {
                propVariant.Free();
            }
        }
    }
}
