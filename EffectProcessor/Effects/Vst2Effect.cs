using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public class Vst2Effect : IVstEffect, IDisposable
    {
        public string Name { get; }
        public string Description { get; }
        public bool IsEnabled { get; set; } = true;

        private IntPtr _moduleHandle;
        private IntPtr _effectPtr;
        private AEffect _effect;

        // Keep delegates alive
        private AudioMasterCallbackDelegate _audioMasterCallback;

        private bool _isProcessing;
        private bool _disposed;
        private int _lastBlockSize;
        private float _lastSampleRate;

        // Buffers
        private float[][]? _inputChannelBuffers;
        private float[][]? _outputChannelBuffers;
        private GCHandle[]? _inputPinHandles;
        private GCHandle[]? _outputPinHandles;

        public Vst2Effect(string pluginPath)
        {
            if (!File.Exists(pluginPath))
                throw new FileNotFoundException("VST2 Plugin not found.", pluginPath);

            Name = Path.GetFileNameWithoutExtension(pluginPath);
            Description = $"VST2 Plugin ({Name})";

            _moduleHandle = NativeMethods.LoadLibrary(pluginPath);
            if (_moduleHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load VST2 module: {pluginPath} (Error: {Marshal.GetLastWin32Error()})");

            try
            {
                InitializePlugin();
            }
            catch
            {
                NativeMethods.FreeLibrary(_moduleHandle);
                _moduleHandle = IntPtr.Zero;
                throw;
            }
        }

        private void InitializePlugin()
        {
            IntPtr mainProc = NativeMethods.GetProcAddress(_moduleHandle, "VSTPluginMain");
            if (mainProc == IntPtr.Zero)
            {
                mainProc = NativeMethods.GetProcAddress(_moduleHandle, "main");
                if (mainProc == IntPtr.Zero)
                    throw new InvalidOperationException("VST2 module does not export VSTPluginMain or main");
            }

            _audioMasterCallback = new AudioMasterCallbackDelegate(HostCallback);
            var pluginMain = Marshal.GetDelegateForFunctionPointer<VstPluginMainDelegate>(mainProc);

            _effectPtr = pluginMain(_audioMasterCallback);
            if (_effectPtr == IntPtr.Zero)
                throw new InvalidOperationException("VSTPluginMain returned null");

            _effect = Marshal.PtrToStructure<AEffect>(_effectPtr);

            if (_effect.magic != 0x56737450) // 'VstP'
                throw new InvalidOperationException("Invalid VST2 magic number");

            // Open the plugin
            Dispatch(Vst2Opcodes.effOpen, 0, 0, IntPtr.Zero, 0.0f);
        }

        private IntPtr HostCallback(IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt)
        {
            switch (opcode)
            {
                case 1: // audioMasterVersion
                    return (IntPtr)2400; // VST 2.4
                default:
                    return IntPtr.Zero;
            }
        }

        private IntPtr Dispatch(int opcode, int index, int value, IntPtr ptr, float opt)
        {
            if (_effectPtr == IntPtr.Zero || _effect.dispatcher == IntPtr.Zero) return IntPtr.Zero;
            var dispatcher = Marshal.GetDelegateForFunctionPointer<AEffectDispatcherProc>(_effect.dispatcher);
            return dispatcher(_effectPtr, opcode, index, (IntPtr)value, ptr, opt);
        }

        public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
        {
            if (!IsEnabled || _disposed || _effectPtr == IntPtr.Zero || _effect.processReplacing == IntPtr.Zero) return;

            int sampleCount = count / channels;
            if (sampleCount <= 0) return;

            if (sampleRate != _lastSampleRate || sampleCount != _lastBlockSize)
            {
                SetupProcessing(sampleRate, sampleCount);
            }

            EnsureBuffers(channels, sampleCount);

            // De-interleave
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    _inputChannelBuffers![c][i] = buffer[offset + i * channels + c];
                }
            }

            ProcessVst2Audio(channels, sampleCount);

            // Interleave back
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    buffer[offset + i * channels + c] = _outputChannelBuffers![c][i];
                }
            }
        }

        private void SetupProcessing(float sampleRate, int blockSize)
        {
            if (_isProcessing)
            {
                Dispatch(Vst2Opcodes.effMainsChanged, 0, 0, IntPtr.Zero, 0.0f);
                _isProcessing = false;
            }

            Dispatch(Vst2Opcodes.effSetSampleRate, 0, 0, IntPtr.Zero, sampleRate);
            Dispatch(Vst2Opcodes.effSetBlockSize, 0, blockSize, IntPtr.Zero, 0.0f);

            Dispatch(Vst2Opcodes.effMainsChanged, 0, 1, IntPtr.Zero, 0.0f); // Resume
            _isProcessing = true;

            _lastSampleRate = sampleRate;
            _lastBlockSize = blockSize;
        }

        private void EnsureBuffers(int channels, int sampleCount)
        {
            if (_inputChannelBuffers != null && _inputChannelBuffers.Length == channels &&
                _inputChannelBuffers[0].Length >= sampleCount)
                return;

            FreeBufferPins();

            _inputChannelBuffers = new float[channels][];
            _outputChannelBuffers = new float[channels][];
            _inputPinHandles = new GCHandle[channels];
            _outputPinHandles = new GCHandle[channels];

            for (int c = 0; c < channels; c++)
            {
                _inputChannelBuffers[c] = new float[sampleCount];
                _outputChannelBuffers[c] = new float[sampleCount];
                _inputPinHandles[c] = GCHandle.Alloc(_inputChannelBuffers[c], GCHandleType.Pinned);
                _outputPinHandles[c] = GCHandle.Alloc(_outputChannelBuffers[c], GCHandleType.Pinned);
            }
        }

        private void ProcessVst2Audio(int channels, int sampleCount)
        {
            if (_effectPtr == IntPtr.Zero || _inputPinHandles == null || _outputPinHandles == null) return;

            IntPtr[] inputPtrs = new IntPtr[channels];
            IntPtr[] outputPtrs = new IntPtr[channels];
            for (int c = 0; c < channels; c++)
            {
                inputPtrs[c] = _inputPinHandles[c].AddrOfPinnedObject();
                outputPtrs[c] = _outputPinHandles[c].AddrOfPinnedObject();
            }

            var processReplacing = Marshal.GetDelegateForFunctionPointer<AEffectProcessProc>(_effect.processReplacing);

            var inputPtrsHandle = GCHandle.Alloc(inputPtrs, GCHandleType.Pinned);
            var outputPtrsHandle = GCHandle.Alloc(outputPtrs, GCHandleType.Pinned);

            try
            {
                processReplacing(_effectPtr, inputPtrsHandle.AddrOfPinnedObject(), outputPtrsHandle.AddrOfPinnedObject(), sampleCount);
            }
            finally
            {
                inputPtrsHandle.Free();
                outputPtrsHandle.Free();
            }
        }

        public void Reset()
        {
            if (_disposed || _effectPtr == IntPtr.Zero) return;

            if (_isProcessing)
            {
                Dispatch(Vst2Opcodes.effMainsChanged, 0, 0, IntPtr.Zero, 0.0f);
                Dispatch(Vst2Opcodes.effMainsChanged, 0, 1, IntPtr.Zero, 0.0f);
            }
        }

        public void OpenEditor(IntPtr hWnd)
        {
            if ((_effect.flags & 1) == 1) // effFlagsHasEditor
            {
                Dispatch(Vst2Opcodes.effEditOpen, 0, 0, hWnd, 0.0f);
            }
            else
            {
                // Fallback or generic message handled by caller
            }
        }

        public void CloseEditor()
        {
            if ((_effect.flags & 1) == 1)
            {
                Dispatch(Vst2Opcodes.effEditClose, 0, 0, IntPtr.Zero, 0.0f);
            }
        }

        private void FreeBufferPins()
        {
            if (_inputPinHandles != null)
            {
                foreach (var h in _inputPinHandles)
                    if (h.IsAllocated) h.Free();
                _inputPinHandles = null;
            }
            if (_outputPinHandles != null)
            {
                foreach (var h in _outputPinHandles)
                    if (h.IsAllocated) h.Free();
                _outputPinHandles = null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_isProcessing)
            {
                Dispatch(Vst2Opcodes.effMainsChanged, 0, 0, IntPtr.Zero, 0.0f);
                _isProcessing = false;
            }

            FreeBufferPins();

            if (_effectPtr != IntPtr.Zero)
            {
                Dispatch(Vst2Opcodes.effClose, 0, 0, IntPtr.Zero, 0.0f);
                _effectPtr = IntPtr.Zero;
            }

            if (_moduleHandle != IntPtr.Zero)
            {
                NativeMethods.FreeLibrary(_moduleHandle);
                _moduleHandle = IntPtr.Zero;
            }
        }

        // --- Interop Structs & Delegates ---

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct AEffect
        {
            public int magic;
            public IntPtr dispatcher;
            public IntPtr process;
            public IntPtr setParameter;
            public IntPtr getParameter;
            public int numPrograms;
            public int numParams;
            public int numInputs;
            public int numOutputs;
            public int flags;
            public IntPtr resvd1;
            public IntPtr resvd2;
            public int initialDelay;
            public int realQualities;
            public int offQualities;
            public float ioRatio;
            public IntPtr objectPtr;
            public IntPtr user;
            public int uniqueID;
            public int version;
            public IntPtr processReplacing;
            public IntPtr processDoubleReplacing;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 56)]
            public byte[] future;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AudioMasterCallbackDelegate(IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr VstPluginMainDelegate(AudioMasterCallbackDelegate audioMaster);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr AEffectDispatcherProc(IntPtr effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AEffectProcessProc(IntPtr effect, IntPtr inputs, IntPtr outputs, int sampleFrames);

        private static class Vst2Opcodes
        {
            public const int effOpen = 0;
            public const int effClose = 1;
            public const int effSetProgram = 2;
            public const int effGetProgram = 3;
            public const int effSetSampleRate = 10;
            public const int effSetBlockSize = 11;
            public const int effMainsChanged = 12;
            public const int effEditOpen = 14;
            public const int effEditClose = 15;
            public const int effEditIdle = 19;
        }

        private static class NativeMethods
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr LoadLibrary(string lpFileName);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FreeLibrary(IntPtr hModule);

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, ExactSpelling = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
        }
    }
}
