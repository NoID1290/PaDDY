using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public class Vst2Effect : IVstEffect, IDisposable
    {
        public string Name { get; }
        public string Description { get; }
        public bool IsEnabled { get; set; } = false;

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

            int inputChannels = _inputChannelBuffers!.Length;
            int outputChannels = _outputChannelBuffers!.Length;

            // De-interleave
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    _inputChannelBuffers![c][i] = buffer[offset + i * channels + c];
                }
            }

            // If mono source and stereo plugin, copy channel 0 to channel 1
            if (channels == 1 && inputChannels >= 2)
            {
                Array.Copy(_inputChannelBuffers![0], _inputChannelBuffers![1], sampleCount);
            }

            // Clear any remaining input channels
            for (int c = Math.Max(channels, 2); c < inputChannels; c++)
            {
                Array.Clear(_inputChannelBuffers![c], 0, sampleCount);
            }

            ProcessVst2Audio(inputChannels, outputChannels, sampleCount);

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
            int inputChannels = Math.Max(Math.Max(2, channels), _effect.numInputs);
            int outputChannels = Math.Max(Math.Max(2, channels), _effect.numOutputs);

            if (_inputChannelBuffers != null && _inputChannelBuffers.Length == inputChannels &&
                _inputChannelBuffers[0].Length >= sampleCount &&
                _outputChannelBuffers != null && _outputChannelBuffers.Length == outputChannels &&
                _outputChannelBuffers[0].Length >= sampleCount)
                return;

            FreeBufferPins();

            _inputChannelBuffers = new float[inputChannels][];
            _outputChannelBuffers = new float[outputChannels][];
            _inputPinHandles = new GCHandle[inputChannels];
            _outputPinHandles = new GCHandle[outputChannels];

            for (int c = 0; c < inputChannels; c++)
            {
                _inputChannelBuffers[c] = new float[sampleCount];
                _inputPinHandles[c] = GCHandle.Alloc(_inputChannelBuffers[c], GCHandleType.Pinned);
            }
            for (int c = 0; c < outputChannels; c++)
            {
                _outputChannelBuffers[c] = new float[sampleCount];
                _outputPinHandles[c] = GCHandle.Alloc(_outputChannelBuffers[c], GCHandleType.Pinned);
            }
        }

        private void ProcessVst2Audio(int inputChannels, int outputChannels, int sampleCount)
        {
            if (_effectPtr == IntPtr.Zero || _inputPinHandles == null || _outputPinHandles == null) return;

            IntPtr[] inputPtrs = new IntPtr[inputChannels];
            IntPtr[] outputPtrs = new IntPtr[outputChannels];
            for (int c = 0; c < inputChannels; c++)
            {
                inputPtrs[c] = _inputPinHandles[c].AddrOfPinnedObject();
            }
            for (int c = 0; c < outputChannels; c++)
            {
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

        public bool HasEditor => (_effect.flags & 1) == 1;

        public void OpenEditor(IntPtr hWnd)
        {
            if (HasEditor && _effectPtr != IntPtr.Zero)
            {
                Dispatch(Vst2Opcodes.effEditOpen, 0, 0, hWnd, 0.0f);
            }
        }

        public void CloseEditor()
        {
            if (HasEditor && _effectPtr != IntPtr.Zero)
            {
                Dispatch(Vst2Opcodes.effEditClose, 0, 0, IntPtr.Zero, 0.0f);
            }
        }

        public bool GetEditorSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            if (!HasEditor || _effectPtr == IntPtr.Zero) return false;

            IntPtr rectPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(rectPtr, IntPtr.Zero);
            try
            {
                Dispatch(Vst2Opcodes.effEditGetRect, 0, 0, rectPtr, 0.0f);
                IntPtr eRectAddr = Marshal.ReadIntPtr(rectPtr);
                if (eRectAddr != IntPtr.Zero)
                {
                    var rect = Marshal.PtrToStructure<ERect>(eRectAddr);
                    width = rect.right - rect.left;
                    height = rect.bottom - rect.top;
                    return width > 0 && height > 0;
                }
            }
            catch
            {
            }
            finally
            {
                Marshal.FreeHGlobal(rectPtr);
            }
            return false;
        }

        public int GetParameterCount()
        {
            if (_effectPtr == IntPtr.Zero) return 0;
            return _effect.numParams;
        }

        public VstParameterInfo GetParameterInfo(int index)
        {
            var info = new VstParameterInfo { Index = index, Name = $"Param {index}", Display = "", Label = "", Value = 0f };
            if (_effectPtr == IntPtr.Zero || index < 0 || index >= _effect.numParams) return info;

            try
            {
                if (_effect.getParameter != IntPtr.Zero)
                {
                    var getParam = Marshal.GetDelegateForFunctionPointer<AEffectGetParameterProc>(_effect.getParameter);
                    info.Value = getParam(_effectPtr, index);
                }

                byte[] nameBuf = new byte[32];
                GCHandle hName = GCHandle.Alloc(nameBuf, GCHandleType.Pinned);
                try
                {
                    Dispatch(Vst2Opcodes.effGetParamName, index, 0, hName.AddrOfPinnedObject(), 0.0f);
                    info.Name = System.Text.Encoding.ASCII.GetString(nameBuf).TrimEnd('\0', ' ').Trim();
                    if (string.IsNullOrEmpty(info.Name)) info.Name = $"Param {index}";
                }
                finally { hName.Free(); }

                byte[] dispBuf = new byte[32];
                GCHandle hDisp = GCHandle.Alloc(dispBuf, GCHandleType.Pinned);
                try
                {
                    Dispatch(Vst2Opcodes.effGetParamDisplay, index, 0, hDisp.AddrOfPinnedObject(), 0.0f);
                    info.Display = System.Text.Encoding.ASCII.GetString(dispBuf).TrimEnd('\0', ' ').Trim();
                }
                finally { hDisp.Free(); }

                byte[] lblBuf = new byte[32];
                GCHandle hLbl = GCHandle.Alloc(lblBuf, GCHandleType.Pinned);
                try
                {
                    Dispatch(Vst2Opcodes.effGetParamLabel, index, 0, hLbl.AddrOfPinnedObject(), 0.0f);
                    info.Label = System.Text.Encoding.ASCII.GetString(lblBuf).TrimEnd('\0', ' ').Trim();
                }
                finally { hLbl.Free(); }
            }
            catch { }

            return info;
        }

        public void SetParameterValue(int index, float value)
        {
            if (_effectPtr == IntPtr.Zero || _effect.setParameter == IntPtr.Zero || index < 0 || index >= _effect.numParams) return;
            try
            {
                var setParam = Marshal.GetDelegateForFunctionPointer<AEffectSetParameterProc>(_effect.setParameter);
                setParam(_effectPtr, index, value);
            }
            catch { }
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AEffectSetParameterProc(IntPtr effect, int index, float parameterValue);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate float AEffectGetParameterProc(IntPtr effect, int index);

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct ERect
        {
            public short top;
            public short left;
            public short bottom;
            public short right;
        }

        private static class Vst2Opcodes
        {
            public const int effOpen = 0;
            public const int effClose = 1;
            public const int effSetProgram = 2;
            public const int effGetProgram = 3;
            public const int effGetParamDisplay = 7;
            public const int effGetParamName = 8;
            public const int effGetParamLabel = 9;
            public const int effSetSampleRate = 10;
            public const int effSetBlockSize = 11;
            public const int effMainsChanged = 12;
            public const int effEditGetRect = 13;
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
