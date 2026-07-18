using System;
using System.IO;
using System.Linq;
using Jacobi.Vst.Core;
using Jacobi.Vst.Core.Host;
using Jacobi.Vst.Host.Interop;

namespace NoIDSoftwork.EffectProcessor.Effects
{
    public class Vst2HostCommandStub : IVstHostCommandStub
    {
        public IVstPluginContext PluginContext { get; set; } = null!;
        public IVstHostCommands20 Commands { get; }

        public Vst2HostCommandStub()
        {
            Commands = new Vst2HostCommands();
        }
    }

    public class Vst2HostCommands : IVstHostCommands20
    {
        public void SetParameterAutomated(int index, float value) {}
        public int GetVersion() => 2400;
        public int GetCurrentPluginID() => 0;
        public void ProcessIdle() {}

        public VstTimeInfo GetTimeInfo(VstTimeInfoFlags filter) => new VstTimeInfo();
        public bool ProcessEvents(VstEvent[] events) => false;
        public bool IoChanged() => false;
        public bool SizeWindow(int width, int height) => false;
        public float GetSampleRate() => 44100.0f;
        public int GetBlockSize() => 1024;
        public int GetInputLatency() => 0;
        public int GetOutputLatency() => 0;
        public VstProcessLevels GetProcessLevel() => VstProcessLevels.Unknown;
        public VstAutomationStates GetAutomationState() => VstAutomationStates.Unsupported;
        public string GetVendorString() => "NoID Softwork";
        public string GetProductString() => "PaDDY";
        public int GetVendorVersion() => 1;
        public VstCanDoResult CanDo(string cando) => VstCanDoResult.No;
        public VstHostLanguage GetLanguage() => VstHostLanguage.English;
        public string GetDirectory() => "";
        public bool UpdateDisplay() => false;
        public bool BeginEdit(int index) => false;
        public bool EndEdit(int index) => false;
        public bool OpenFileSelector(VstFileSelect fileSelect) => false;
        public bool CloseFileSelector(VstFileSelect fileSelect) => false;
    }

    public class Vst2Effect : IVstEffect, IDisposable
    {
        public string Name { get; }
        public string Description { get; }
        public bool IsEnabled { get; set; } = true;

        private VstPluginContext? _pluginContext;
        private Vst2HostCommandStub? _hostCmdStub;
        private VstAudioBufferManager? _inputBufferManager;
        private VstAudioBufferManager? _outputBufferManager;
        private VstAudioBuffer[]? _inputBuffers;
        private VstAudioBuffer[]? _outputBuffers;

        public Vst2Effect(string pluginPath)
        {
            if (!File.Exists(pluginPath))
                throw new FileNotFoundException("VST Plugin not found.", pluginPath);
                
            Name = Path.GetFileNameWithoutExtension(pluginPath);
            Description = "VST2 Plugin (mda Dynamics)";

            _hostCmdStub = new Vst2HostCommandStub();
            _pluginContext = VstPluginContext.Create(pluginPath, _hostCmdStub);
            _hostCmdStub.PluginContext = _pluginContext;

            _pluginContext.PluginCommandStub.Commands.Open();
            _pluginContext.PluginCommandStub.Commands.MainsChanged(true);
        }

        public void ProcessBuffer(float[] buffer, int offset, int count, int channels, int sampleRate)
        {
            if (_pluginContext == null || !IsEnabled) return;

            int sampleCount = count / channels;
            
            if (_inputBufferManager == null || _inputBufferManager.BufferSize != sampleCount || _inputBufferManager.BufferCount != channels)
            {
                _inputBufferManager?.Dispose();
                _outputBufferManager?.Dispose();

                _inputBufferManager = new VstAudioBufferManager(channels, sampleCount);
                _outputBufferManager = new VstAudioBufferManager(channels, sampleCount);

                _inputBuffers = _inputBufferManager.Buffers.ToArray();
                _outputBuffers = _outputBufferManager.Buffers.ToArray();

                _pluginContext.PluginCommandStub.Commands.SetBlockSize(sampleCount);
            }

            _pluginContext.PluginCommandStub.Commands.SetSampleRate(sampleRate);

            // De-interleave input samples
            for (int c = 0; c < channels; c++)
            {
                var span = _inputBuffers![c].AsSpan();
                for (int i = 0; i < sampleCount; i++)
                {
                    span[i] = buffer[offset + i * channels + c];
                }
            }

            // Call VST plugin process
            _pluginContext.PluginCommandStub.Commands.ProcessReplacing(_inputBuffers!, _outputBuffers!);

            // Interleave output samples
            for (int c = 0; c < channels; c++)
            {
                var span = _outputBuffers![c].AsSpan();
                for (int i = 0; i < sampleCount; i++)
                {
                    buffer[offset + i * channels + c] = span[i];
                }
            }
        }

        public void Reset()
        {
            if (_pluginContext != null)
            {
                _pluginContext.PluginCommandStub.Commands.MainsChanged(false);
                _pluginContext.PluginCommandStub.Commands.MainsChanged(true);
            }
        }

        public void OpenEditor(IntPtr hWnd)
        {
            _pluginContext?.PluginCommandStub.Commands.EditorOpen(hWnd);
        }

        public void CloseEditor()
        {
            _pluginContext?.PluginCommandStub.Commands.EditorClose();
        }

        public void Dispose()
        {
            if (_pluginContext != null)
            {
                _pluginContext.PluginCommandStub.Commands.MainsChanged(false);
                _pluginContext.PluginCommandStub.Commands.Close();
                _pluginContext.Dispose();
                _pluginContext = null;
            }
            _inputBufferManager?.Dispose();
            _outputBufferManager?.Dispose();
        }
    }
}
