using System;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NoIDSoftwork.EffectProcessor;

namespace PaDDY.Services
{
    /// <summary>
    /// Real-time live microphone capture, real-time FX modulation (pitch shift, reverb, distortion, etc.),
    /// and dual-bus WASAPI output routing (Primary Headset + Secondary/Virtual Audio Cable for Discord).
    /// </summary>
    public sealed class LiveMicModulatorService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private BufferedWaveProvider? _inputBuffer;
        private IWavePlayer? _primaryPlayer;
        private IWavePlayer? _secondaryPlayer;
        private BufferedWaveProvider? _primaryBuffer;
        private BufferedWaveProvider? _secondaryBuffer;
        private bool _disposed;
        private bool _isRunning;
        private bool _isMuted;
        private bool _isFxEnabled = true;
        private float _gain = 1.0f;

        public bool IsRunning => _isRunning;
        public bool IsMuted { get => _isMuted; set => _isMuted = value; }
        public bool IsFxEnabled { get => _isFxEnabled; set => _isFxEnabled = value; }
        public float Gain { get => _gain; set => _gain = Math.Clamp(value, 0f, 4f); }

        public float PeakLevel { get; private set; }

        public IEffectChain EffectChain { get; } = EffectChainFactory.CreateGlobal();

        public event EventHandler<float>? PeakLevelUpdated;

        public void Start(int inputDeviceIndex, int primaryOutputDeviceIndex, int secondaryOutputDeviceIndex = 0, bool dualOutputEnabled = false)
        {
            Stop();

            try
            {
                int sampleRate = 48000;
                int channels = 1;
                WaveFormat waveFormat = new WaveFormat(sampleRate, 16, channels);

                _waveIn = new WaveInEvent
                {
                    DeviceNumber = Math.Max(0, inputDeviceIndex),
                    WaveFormat = waveFormat,
                    BufferMilliseconds = 20
                };

                _inputBuffer = new BufferedWaveProvider(waveFormat)
                {
                    DiscardOnBufferOverflow = true
                };

                _primaryBuffer = new BufferedWaveProvider(waveFormat)
                {
                    DiscardOnBufferOverflow = true
                };

                _primaryPlayer = AudioOutputDeviceResolver.CreateWasapiPlayer(primaryOutputDeviceIndex, 40);
                _primaryPlayer.Init(_primaryBuffer);
                _primaryPlayer.Play();

                if (dualOutputEnabled && secondaryOutputDeviceIndex > 0)
                {
                    _secondaryBuffer = new BufferedWaveProvider(waveFormat)
                    {
                        DiscardOnBufferOverflow = true
                    };
                    _secondaryPlayer = AudioOutputDeviceResolver.CreateWasapiPlayer(secondaryOutputDeviceIndex - 1, 40);
                    _secondaryPlayer.Init(_secondaryBuffer);
                    _secondaryPlayer.Play();
                }

                _waveIn.DataAvailable += OnMicDataAvailable;
                _waveIn.StartRecording();
                _isRunning = true;
            }
            catch
            {
                Stop();
            }
        }

        private void OnMicDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || e.BytesRecorded <= 0)
                return;

            int sampleCount = e.BytesRecorded / 2;
            float[] sampleBuffer = new float[sampleCount];

            // Convert 16-bit PCM byte array to float sample array
            float maxAbs = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                short sample16 = (short)(e.Buffer[i * 2] | (e.Buffer[i * 2 + 1] << 8));
                float f = sample16 / 32768.0f;
                maxAbs = Math.Max(maxAbs, Math.Abs(f));
                sampleBuffer[i] = f;
            }

            float effectivePeak = _isMuted ? 0f : Math.Min(1.0f, maxAbs * _gain);
            PeakLevel = effectivePeak;
            PeakLevelUpdated?.Invoke(this, effectivePeak);

            if (_isMuted)
            {
                Array.Clear(sampleBuffer, 0, sampleBuffer.Length);
            }
            else
            {
                // Apply volume gain staging
                if (Math.Abs(_gain - 1.0f) > 0.001f)
                {
                    for (int i = 0; i < sampleBuffer.Length; i++)
                    {
                        sampleBuffer[i] = Math.Clamp(sampleBuffer[i] * _gain, -1.0f, 1.0f);
                    }
                }

                // Apply real-time live FX (pitch shift, reverb, distortion, noise gate, EQ)
                if (_isFxEnabled && EffectChain != null)
                {
                    EffectChain.ProcessBuffer(sampleBuffer, 0, sampleBuffer.Length, 1, 48000);
                }
            }

            // Convert float back to 16-bit PCM bytes
            byte[] outputBytes = new byte[sampleCount * 2];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample16 = (short)(Math.Clamp(sampleBuffer[i], -1.0f, 1.0f) * 32767.0f);
                outputBytes[i * 2] = (byte)(sample16 & 0xFF);
                outputBytes[i * 2 + 1] = (byte)((sample16 >> 8) & 0xFF);
            }

            _primaryBuffer?.AddSamples(outputBytes, 0, outputBytes.Length);
            _secondaryBuffer?.AddSamples(outputBytes, 0, outputBytes.Length);
        }

        public void Stop()
        {
            _isRunning = false;

            if (_waveIn != null)
            {
                _waveIn.DataAvailable -= OnMicDataAvailable;
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }

            if (_primaryPlayer != null)
            {
                try { _primaryPlayer.Stop(); } catch { }
                _primaryPlayer.Dispose();
                _primaryPlayer = null;
            }

            if (_secondaryPlayer != null)
            {
                try { _secondaryPlayer.Stop(); } catch { }
                _secondaryPlayer.Dispose();
                _secondaryPlayer = null;
            }

            _primaryBuffer = null;
            _secondaryBuffer = null;
            _inputBuffer = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
