using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    /// <summary>
    /// VST3 plugin host effect using direct COM interop with the Steinberg VST3 API.
    /// Loads a .vst3 bundle or DLL, instantiates the audio processor component,
    /// and processes interleaved float PCM through the plugin.
    /// </summary>
    public class Vst3Effect : IVstEffect, IDisposable
    {
        public string Name { get; }
        public string Description { get; }
        public bool IsEnabled { get; set; } = false;

        private IntPtr _moduleHandle;
        private IPluginFactory? _factory;
        private IntPtr _processorPtr;
        private IAudioProcessor? _processor;
        private IComponent? _component;
        private IEditController? _editController;
        private bool _isProcessing;
        private bool _disposed;
        private int _lastBlockSize;
        private double _lastSampleRate;

        // Pre-allocated processing buffers (de-interleaved)
        private float[][]? _inputChannelBuffers;
        private float[][]? _outputChannelBuffers;
        private GCHandle[]? _inputPinHandles;
        private GCHandle[]? _outputPinHandles;

        public Vst3Effect(string pluginPath)
        {
            if (!File.Exists(pluginPath) && !Directory.Exists(pluginPath))
                throw new FileNotFoundException("VST3 Plugin not found.", pluginPath);

            Name = Path.GetFileNameWithoutExtension(pluginPath);

            // Resolve the actual DLL from a .vst3 bundle directory
            string dllPath = ResolveVst3DllPath(pluginPath);
            Description = $"VST3 Plugin ({Name})";

            _moduleHandle = NativeMethods.LoadLibrary(dllPath);
            if (_moduleHandle == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to load VST3 module: {dllPath} (Error: {Marshal.GetLastWin32Error()})");

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

        /// <summary>
        /// Resolves the actual .vst3 DLL path from a bundle directory or direct file path.
        /// VST3 bundles follow: MyPlugin.vst3/Contents/x86_64-win/MyPlugin.vst3
        /// </summary>
        private static string ResolveVst3DllPath(string path)
        {
            if (File.Exists(path))
                return path;

            if (Directory.Exists(path))
            {
                // Standard VST3 bundle structure
                string bundleName = Path.GetFileNameWithoutExtension(path);
                string winDll = Path.Combine(path, "Contents", "x86_64-win", bundleName + ".vst3");
                if (File.Exists(winDll))
                    return winDll;

                // Try without extension in directory name
                winDll = Path.Combine(path, "Contents", "x86_64-win", Path.GetFileName(path));
                if (File.Exists(winDll))
                    return winDll;

                throw new FileNotFoundException($"Could not find VST3 DLL in bundle: {path}");
            }

            throw new FileNotFoundException("VST3 path not found.", path);
        }

        private void InitializePlugin()
        {
            // GetPluginFactory is the entry point for VST3 modules
            IntPtr getFactoryProc = NativeMethods.GetProcAddress(_moduleHandle, "GetPluginFactory");
            if (getFactoryProc == IntPtr.Zero)
            {
                // Try InitDll first (some plugins require it)
                IntPtr initProc = NativeMethods.GetProcAddress(_moduleHandle, "InitDll");
                if (initProc != IntPtr.Zero)
                {
                    var initDll = Marshal.GetDelegateForFunctionPointer<InitDllDelegate>(initProc);
                    initDll();
                }

                getFactoryProc = NativeMethods.GetProcAddress(_moduleHandle, "GetPluginFactory");
                if (getFactoryProc == IntPtr.Zero)
                    throw new InvalidOperationException("VST3 module does not export GetPluginFactory");
            }
            else
            {
                // Call InitDll if available
                IntPtr initProc = NativeMethods.GetProcAddress(_moduleHandle, "InitDll");
                if (initProc != IntPtr.Zero)
                {
                    var initDll = Marshal.GetDelegateForFunctionPointer<InitDllDelegate>(initProc);
                    initDll();
                }
            }

            var getFactory = Marshal.GetDelegateForFunctionPointer<GetPluginFactoryDelegate>(getFactoryProc);
            IntPtr factoryPtr = getFactory();
            if (factoryPtr == IntPtr.Zero)
                throw new InvalidOperationException("GetPluginFactory returned null");

            // Direct C++ vtable dispatch for IPluginFactory (bypasses COM QueryInterface variations across VST3 SDKs)
            IntPtr factoryVtable = Marshal.ReadIntPtr(factoryPtr);
            IntPtr countClassesPtr = Marshal.ReadIntPtr(factoryVtable, 4 * IntPtr.Size);
            IntPtr getClassInfoPtr = Marshal.ReadIntPtr(factoryVtable, 5 * IntPtr.Size);
            IntPtr createInstancePtr = Marshal.ReadIntPtr(factoryVtable, 6 * IntPtr.Size);

            var countClasses = Marshal.GetDelegateForFunctionPointer<CountClassesDelegate>(countClassesPtr);
            var getClassInfo = Marshal.GetDelegateForFunctionPointer<GetClassInfoDelegate>(getClassInfoPtr);
            var createInstance = Marshal.GetDelegateForFunctionPointer<CreateInstanceDelegate>(createInstancePtr);

            int classCount = countClasses(factoryPtr);
            Guid processorCid = Guid.Empty;
            Guid controllerCid = Guid.Empty;

            for (int i = 0; i < classCount; i++)
            {
                var classInfo = new PClassInfo();
                if (getClassInfo(factoryPtr, i, ref classInfo) == 0)
                {
                    string category = classInfo.GetCategory();
                    if (category == "Audio Module Class" && processorCid == Guid.Empty)
                    {
                        processorCid = classInfo.cid;
                    }
                    else if (category == "Component Controller Class" && controllerCid == Guid.Empty)
                    {
                        controllerCid = classInfo.cid;
                    }
                }
            }

            if (processorCid == Guid.Empty)
                throw new InvalidOperationException("No audio processor class found in VST3 module");

            // Create the processor component trying VST3 raw byte layout and fallbacks
            Guid iComponentGuid = CreateVst3Guid(0xE831FF31, 0x4301F2D5, 0xEEBB8E92, 0x02786925);
            IntPtr componentPtr = IntPtr.Zero;
            int hr = createInstance(factoryPtr, ref processorCid, ref iComponentGuid, out componentPtr);

            if (hr != 0 || componentPtr == IntPtr.Zero)
            {
                // Fallback 1: Try FUnknown::iid (0x00000000, 0x00000000, 0xC0000000, 0x00000046)
                Guid fUnknownGuid = CreateVst3Guid(0x00000000, 0x00000000, 0xC0000000, 0x00000046);
                hr = createInstance(factoryPtr, ref processorCid, ref fUnknownGuid, out componentPtr);
            }

            if (hr != 0 || componentPtr == IntPtr.Zero)
            {
                // Fallback 2: Try IPluginBase::iid (0x22888ADB, 0x156E4D93, 0x86E56C35, 0x8D9E3466)
                Guid pluginBaseGuid = CreateVst3Guid(0x22888ADB, 0x156E4D93, 0x86E56C35, 0x8D9E3466);
                hr = createInstance(factoryPtr, ref processorCid, ref pluginBaseGuid, out componentPtr);
            }

            if (hr != 0 || componentPtr == IntPtr.Zero)
            {
                // Fallback 3: Try standard C# / Windows COM GUID layout
                Guid comGuid = new Guid("E831FF31-4301-F2D5-EEBB-8E9202786925");
                hr = createInstance(factoryPtr, ref processorCid, ref comGuid, out componentPtr);
            }

            if (hr != 0 || componentPtr == IntPtr.Zero)
                throw new InvalidOperationException($"Failed to create VST3 component instance (HRESULT: 0x{hr:X8})");

            // Direct vtable dispatch to slot 3 for Initialize(IntPtr.Zero)
            IntPtr compVtable = Marshal.ReadIntPtr(componentPtr);
            IntPtr initCompPtr = Marshal.ReadIntPtr(compVtable, 3 * IntPtr.Size);
            var compInit = Marshal.GetDelegateForFunctionPointer<ComponentInitializeDelegate>(initCompPtr);
            hr = compInit(componentPtr, IntPtr.Zero);
            if (hr != 0)
                throw new InvalidOperationException($"Failed to initialize VST3 component (HRESULT: 0x{hr:X8})");

            try
            {
                _component = (IComponent)Marshal.GetObjectForIUnknown(componentPtr);
            }
            catch
            {
                // Native vtable initialization already completed successfully
            }

            // Query IAudioProcessor using both VST3 byte layout and COM GUID layout
            Guid iAudioProcessorVst3 = CreateVst3Guid(0x42043F99, 0x453CB7DA, 0x9DE769A5, 0x3DC3AE9A);
            int qiRes = Marshal.QueryInterface(componentPtr, ref iAudioProcessorVst3, out _processorPtr);
            if (qiRes != 0 || _processorPtr == IntPtr.Zero)
            {
                Guid iAudioProcessorCom = new Guid("42043F99-453C-B7DA-9DE7-69A53DC3AE9A");
                qiRes = Marshal.QueryInterface(componentPtr, ref iAudioProcessorCom, out _processorPtr);
            }
            if (qiRes != 0 || _processorPtr == IntPtr.Zero)
            {
                _processorPtr = componentPtr;
            }

            try
            {
                _processor = (IAudioProcessor)Marshal.GetObjectForIUnknown(_processorPtr);
            }
            catch
            {
                // Fallback handled via direct vtable if COM RCW binding fails
            }

            // Try to get the edit controller
            try
            {
                if (controllerCid != Guid.Empty)
                {
                    Guid iEditControllerGuid = CreateVst3Guid(0xDCD7BBE3, 0x448D7742, 0xCCAA74A8, 0x9E759C97);
                    hr = createInstance(factoryPtr, ref controllerCid, ref iEditControllerGuid, out IntPtr controllerPtr);
                    if (hr != 0 || controllerPtr == IntPtr.Zero)
                    {
                        Guid iEditControllerCom = new Guid("DCD7BBE3-448D-7742-CCAA-74A89E759C97");
                        hr = createInstance(factoryPtr, ref controllerCid, ref iEditControllerCom, out controllerPtr);
                    }
                    if (hr == 0 && controllerPtr != IntPtr.Zero)
                    {
                        IntPtr ctrlVtable = Marshal.ReadIntPtr(controllerPtr);
                        IntPtr ctrlInitPtr = Marshal.ReadIntPtr(ctrlVtable, 3 * IntPtr.Size);
                        var ctrlInit = Marshal.GetDelegateForFunctionPointer<ComponentInitializeDelegate>(ctrlInitPtr);
                        ctrlInit(controllerPtr, IntPtr.Zero);
                        try { _editController = (IEditController)Marshal.GetObjectForIUnknown(controllerPtr); } catch { }
                    }
                }
            }
            catch
            {
                // Edit controller is optional
            }
        }

        /// <summary>
        /// Converts a VST3 16-byte raw TUID / UID specification into a C# Guid with exact byte ordering.
        /// Steinberg VST3 UIDs are specified as 4 32-bit integers in raw big-endian byte sequence.
        /// </summary>
        private static Guid CreateVst3Guid(uint l1, uint l2, uint l3, uint l4)
        {
            byte[] bytes = new byte[16]
            {
                (byte)((l1 >> 24) & 0xFF), (byte)((l1 >> 16) & 0xFF), (byte)((l1 >> 8) & 0xFF), (byte)(l1 & 0xFF),
                (byte)((l2 >> 24) & 0xFF), (byte)((l2 >> 16) & 0xFF), (byte)((l2 >> 8) & 0xFF), (byte)(l2 & 0xFF),
                (byte)((l3 >> 24) & 0xFF), (byte)((l3 >> 16) & 0xFF), (byte)((l3 >> 8) & 0xFF), (byte)(l3 & 0xFF),
                (byte)((l4 >> 24) & 0xFF), (byte)((l4 >> 16) & 0xFF), (byte)((l4 >> 8) & 0xFF), (byte)(l4 & 0xFF)
            };
            return new Guid(bytes);
        }

        // ── VST3 COM Interface Definitions ──────────────────────────────────

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CountClassesDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetClassInfoDelegate(IntPtr self, int index, ref PClassInfo info);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateInstanceDelegate(IntPtr self, ref Guid cid, ref Guid iid, out IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int ComponentInitializeDelegate(IntPtr self, IntPtr context);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InitDllDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ExitDllDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr GetPluginFactoryDelegate();

        public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
        {
            if (_processor == null || !IsEnabled || _disposed) return;

            int sampleCount = count / channels;
            if (sampleCount <= 0) return;

            // Setup processing if parameters changed
            if (sampleRate != _lastSampleRate || sampleCount != _lastBlockSize)
            {
                SetupProcessing(sampleRate, sampleCount);
            }

            // Ensure buffers are allocated
            EnsureBuffers(channels, sampleCount);

            int bufferChannels = Math.Max(2, channels);

            // De-interleave input samples
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    _inputChannelBuffers![c][i] = buffer[offset + i * channels + c];
                }
            }

            // If incoming is mono (channels == 1) but we allocated stereo, copy channel 0 to channel 1
            if (channels == 1 && bufferChannels >= 2)
            {
                Array.Copy(_inputChannelBuffers![0], _inputChannelBuffers![1], sampleCount);
            }

            // Copy input to output as starting point
            for (int c = 0; c < bufferChannels; c++)
            {
                Array.Copy(_inputChannelBuffers![c], _outputChannelBuffers![c], sampleCount);
            }

            // Process through VST3
            try
            {
                ProcessVst3Audio(bufferChannels, sampleCount);
            }
            catch
            {
                // On processing failure, pass through unprocessed audio
                return;
            }

            // Interleave output samples back
            for (int c = 0; c < channels; c++)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    buffer[offset + i * channels + c] = _outputChannelBuffers![c][i];
                }
            }
        }

        private void SetupProcessing(double sampleRate, int blockSize)
        {
            if (_processor == null) return;

            if (_isProcessing)
            {
                _processor.SetProcessing(0); // stop
                _isProcessing = false;
            }

            var setup = new ProcessSetup
            {
                processMode = 0, // kRealtime
                symbolicSampleSize = 0, // kSample32
                maxSamplesPerBlock = blockSize,
                sampleRate = sampleRate
            };

            int hr = _processor.SetupProcessing(ref setup);
            if (hr == 0)
            {
                _processor.SetProcessing(1); // start
                _isProcessing = true;
            }

            _lastSampleRate = sampleRate;
            _lastBlockSize = blockSize;
        }

        private void EnsureBuffers(int channels, int sampleCount)
        {
            int bufferChannels = Math.Max(2, channels); // Always allocate at least stereo (2 channels) to prevent crashes in stereo VST3 plugins

            if (_inputChannelBuffers != null && _inputChannelBuffers.Length == bufferChannels &&
                _inputChannelBuffers[0].Length >= sampleCount)
                return;

            FreeBufferPins();

            _inputChannelBuffers = new float[bufferChannels][];
            _outputChannelBuffers = new float[bufferChannels][];
            _inputPinHandles = new GCHandle[bufferChannels];
            _outputPinHandles = new GCHandle[bufferChannels];

            for (int c = 0; c < bufferChannels; c++)
            {
                _inputChannelBuffers[c] = new float[sampleCount];
                _outputChannelBuffers[c] = new float[sampleCount];
                _inputPinHandles[c] = GCHandle.Alloc(_inputChannelBuffers[c], GCHandleType.Pinned);
                _outputPinHandles[c] = GCHandle.Alloc(_outputChannelBuffers[c], GCHandleType.Pinned);
            }
        }

        private void ProcessVst3Audio(int channels, int sampleCount)
        {
            if (_processor == null || _inputPinHandles == null || _outputPinHandles == null) return;

            // Build channel pointer arrays
            IntPtr[] inputPtrs = new IntPtr[channels];
            IntPtr[] outputPtrs = new IntPtr[channels];
            for (int c = 0; c < channels; c++)
            {
                inputPtrs[c] = _inputPinHandles[c].AddrOfPinnedObject();
                outputPtrs[c] = _outputPinHandles[c].AddrOfPinnedObject();
            }

            // Pin the pointer arrays
            var inputPtrsHandle = GCHandle.Alloc(inputPtrs, GCHandleType.Pinned);
            var outputPtrsHandle = GCHandle.Alloc(outputPtrs, GCHandleType.Pinned);

            try
            {
                // Create AudioBusBuffers for input
                var inputBus = new AudioBusBuffers
                {
                    numChannels = channels,
                    silenceFlags = 0,
                    channelBuffers32 = inputPtrsHandle.AddrOfPinnedObject()
                };

                // Create AudioBusBuffers for output
                var outputBus = new AudioBusBuffers
                {
                    numChannels = channels,
                    silenceFlags = 0,
                    channelBuffers32 = outputPtrsHandle.AddrOfPinnedObject()
                };

                // Pin the bus structs so we can get their addresses
                var inputBusHandle = GCHandle.Alloc(inputBus, GCHandleType.Pinned);
                var outputBusHandle = GCHandle.Alloc(outputBus, GCHandleType.Pinned);

                try
                {
                    // Create ProcessData
                    var processData = new ProcessData
                    {
                        processMode = 0, // kRealtime
                        symbolicSampleSize = 0, // kSample32
                        numSamples = sampleCount,
                        numInputs = 1,
                        numOutputs = 1,
                        inputs = inputBusHandle.AddrOfPinnedObject(),
                        outputs = outputBusHandle.AddrOfPinnedObject(),
                        inputParameterChanges = IntPtr.Zero,
                        outputParameterChanges = IntPtr.Zero,
                        inputEvents = IntPtr.Zero,
                        outputEvents = IntPtr.Zero,
                        processContext = IntPtr.Zero
                    };

                    _processor.Process(ref processData);
                }
                finally
                {
                    inputBusHandle.Free();
                    outputBusHandle.Free();
                }
            }
            finally
            {
                inputPtrsHandle.Free();
                outputPtrsHandle.Free();
            }
        }

        public void Reset()
        {
            if (_processor == null || _disposed) return;

            if (_isProcessing)
            {
                _processor.SetProcessing(0);
                _isProcessing = false;
            }
        }

        public bool HasEditor => false;

        public void OpenEditor(IntPtr hWnd)
        {
        }

        public void CloseEditor()
        {
        }

        public bool GetEditorSize(out int width, out int height)
        {
            width = 0;
            height = 0;
            return false;
        }

        public int GetParameterCount()
        {
            return 0;
        }

        public VstParameterInfo GetParameterInfo(int index)
        {
            return new VstParameterInfo { Index = index, Name = $"Param {index}", Display = "", Label = "", Value = 0f };
        }

        public void SetParameterValue(int index, float value)
        {
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

            if (_isProcessing && _processor != null)
            {
                try { _processor.SetProcessing(0); } catch { }
                _isProcessing = false;
            }

            FreeBufferPins();

            if (_component != null)
            {
                try { _component.Terminate(); } catch { }
            }

            if (_editController != null)
            {
                try { _editController.Terminate(); } catch { }
            }

            // Release COM references
            if (_processor != null)
            {
                Marshal.ReleaseComObject(_processor);
                _processor = null;
            }
            if (_component != null)
            {
                Marshal.ReleaseComObject(_component);
                _component = null;
            }
            if (_editController != null)
            {
                Marshal.ReleaseComObject(_editController);
                _editController = null;
            }
            if (_factory != null)
            {
                Marshal.ReleaseComObject(_factory);
                _factory = null;
            }

            if (_processorPtr != IntPtr.Zero)
            {
                Marshal.Release(_processorPtr);
                _processorPtr = IntPtr.Zero;
            }

            if (_moduleHandle != IntPtr.Zero)
            {
                // Call ExitDll if available
                IntPtr exitProc = NativeMethods.GetProcAddress(_moduleHandle, "ExitDll");
                if (exitProc != IntPtr.Zero)
                {
                    var exitDll = Marshal.GetDelegateForFunctionPointer<ExitDllDelegate>(exitProc);
                    try { exitDll(); } catch { }
                }

                NativeMethods.FreeLibrary(_moduleHandle);
                _moduleHandle = IntPtr.Zero;
            }
        }

        // ── VST3 COM Interface Definitions ──────────────────────────────────

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        internal struct PClassInfo
        {
            public Guid cid;
            public int cardinality;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] categoryBytes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] nameBytes;

            public string GetCategory()
            {
                if (categoryBytes == null) return "";
                int len = Array.IndexOf(categoryBytes, (byte)0);
                if (len < 0) len = categoryBytes.Length;
                return System.Text.Encoding.ASCII.GetString(categoryBytes, 0, len);
            }

            public string GetName()
            {
                if (nameBytes == null) return "";
                int len = Array.IndexOf(nameBytes, (byte)0);
                if (len < 0) len = nameBytes.Length;
                return System.Text.Encoding.ASCII.GetString(nameBytes, 0, len);
            }
        }

        [ComImport, Guid("7A4D811C-4A1F-5211-EED2-D9AE9FBF430B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IPluginFactory
        {
            [PreserveSig]
            int GetFactoryInfo(IntPtr info);
            [PreserveSig]
            int CountClasses();
            [PreserveSig]
            int GetClassInfo(int index, ref PClassInfo info);
            [PreserveSig]
            int CreateInstance(ref Guid cid, ref Guid iid, out IntPtr obj);
        }

        [ComImport, Guid("E831FF31-4301-F2D5-EEBB-8E9202786925"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IComponent
        {
            // IPluginBase
            [PreserveSig]
            int Initialize(IntPtr context);
            [PreserveSig]
            int Terminate();

            // IComponent
            [PreserveSig]
            int GetControllerClassId(out Guid classId);
            [PreserveSig]
            int SetIoMode(int mode);
            [PreserveSig]
            int GetBusCount(int type, int dir);
            [PreserveSig]
            int GetBusInfo(int type, int dir, int index, IntPtr bus);
            [PreserveSig]
            int GetRoutingInfo(IntPtr inInfo, IntPtr outInfo);
            [PreserveSig]
            int ActivateBus(int type, int dir, int index, [MarshalAs(UnmanagedType.U1)] bool state);
            [PreserveSig]
            int SetActive([MarshalAs(UnmanagedType.U1)] bool state);
            [PreserveSig]
            int SetState(IntPtr state);
            [PreserveSig]
            int GetState(IntPtr state);
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessSetup
        {
            public int processMode;       // 0=Realtime, 1=Prefetch, 2=Offline
            public int symbolicSampleSize; // 0=float32, 1=float64
            public int maxSamplesPerBlock;
            public double sampleRate;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct AudioBusBuffers
        {
            public int numChannels;
            public ulong silenceFlags;
            public IntPtr channelBuffers32; // float**
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessData
        {
            public int processMode;
            public int symbolicSampleSize;
            public int numSamples;
            public int numInputs;
            public int numOutputs;
            public IntPtr inputs;  // AudioBusBuffers*
            public IntPtr outputs; // AudioBusBuffers*
            public IntPtr inputParameterChanges;
            public IntPtr outputParameterChanges;
            public IntPtr inputEvents;
            public IntPtr outputEvents;
            public IntPtr processContext;
        }

        [ComImport, Guid("42043F99-453C-B7DA-9DE7-69A53DC3AE9A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioProcessor
        {
            [PreserveSig]
            int SetBusArrangements(IntPtr inputs, int numIns, IntPtr outputs, int numOuts);
            [PreserveSig]
            int GetBusArrangement(int dir, int index, out long arr);
            [PreserveSig]
            int CanProcessSampleSize(int symbolicSampleSize);
            [PreserveSig]
            int GetLatencySamples();
            [PreserveSig]
            int SetupProcessing(ref ProcessSetup setup);
            [PreserveSig]
            int SetProcessing(int state);
            [PreserveSig]
            int Process(ref ProcessData data);
            [PreserveSig]
            int GetTailSamples();
        }

        [ComImport, Guid("DCD7BBE3-448D-7742-CCAA-74A89E759C97"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IEditController
        {
            // IPluginBase
            [PreserveSig]
            int Initialize(IntPtr context);
            [PreserveSig]
            int Terminate();

            // IEditController
            [PreserveSig]
            int SetComponentState(IntPtr state);
            [PreserveSig]
            int SetState(IntPtr state);
            [PreserveSig]
            int GetState(IntPtr state);
            [PreserveSig]
            int GetParameterCount();
            // ... additional methods omitted for brevity
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
