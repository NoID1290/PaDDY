using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Models;

namespace PaDDY.Services
{
    public enum CaptureSourceMode
    {
        Microphone = 0,
        OutputLoopback = 1,
        AppLoopback = 2
    }

    public enum AudioRecordingMode
    {
        AutoVAD = 0,
        KeyBuffer = 1
    }

    /// <summary>
    /// Core voice-activity detection and recording engine.
    /// Uses a ring pre-buffer so the first syllables are never clipped.
    /// Supports AutoVAD (silence-based auto-clip) and KeyBuffer (save on demand) modes.
    /// </summary>
    public sealed class AudioCaptureService : IDisposable
    {
        // â”€â”€ Events â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public event Action<RecordingEntry>? RecordingCompleted;
        /// <summary>Fired with (left, right) normalised 0-100 values. Mono sources fire identical L/R.</summary>
        public event Action<double, double>? RmsLevelChanged;
        public event Action<bool>? RecordingStateChanged; // true = started, false = stopped
        public event Action<string>? CodecCompatibilityWarning;

        // â”€â”€ Configuration properties â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>RMS threshold 0-100. Voice above this is recorded (AutoVAD mode).</summary>
        public double Sensitivity { get; set; } = 30.0;

        /// <summary>How long (ms) silence must persist before closing a clip (AutoVAD mode).</summary>
        public double SilenceTimeoutMs { get; set; } = 700.0;

        public string SaveFolder { get; set; } = "recordings";

        /// <summary>Mic recording: sample rate in Hz (e.g., 16000, 44100, 48000).</summary>
        public int RecordSampleRate { get; set; } = 16000;

        /// <summary>Mic recording: bits per sample (16 or 32). 32 = IEEE float.</summary>
        public int RecordBitDepth { get; set; } = 16;

        /// <summary>Mic recording: number of channels (1=mono, 2=stereo).</summary>
        public int RecordChannels { get; set; } = 1;

        /// <summary>Output codec: "wav", "mp3", "opus", or "ogg".</summary>
        public string RecordCodec { get; set; } = "wav";

        /// <summary>Duration of the past-audio ring buffer in milliseconds.</summary>
        public int PastBufferDurationMs { get; set; } = 10000;

        /// <summary>AutoVAD or KeyBuffer.</summary>
        public AudioRecordingMode RecordingMode { get; set; } = AudioRecordingMode.AutoVAD;

        /// <summary>Input gain multiplier 0.0–1.0. Applied to captured samples before metering and recording.</summary>
        public float InputGain { get; set; } = 0.8f;

        /// <summary>Current active capture format, if monitoring is running.</summary>
        public WaveFormat? CurrentCaptureFormat => _captureFormat;

        // â”€â”€ Internal state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private IWaveIn? _captureIn;
        private WaveFormat? _captureFormat;
        private IStreamingRecorder _recorder = new WaveFileRecorder();
        private bool _isRecording;
        private DateTime _lastVoiceTime;

        // Ring pre-buffer
        private readonly Queue<byte[]> _preBuffer = new();
        private int _preBufferBytes;
        private int _preBufferCapacity;

        // KeyBuffer trigger flag (set from any thread, consumed on audio thread)
        private volatile bool _captureBufferNow;

        private bool _disposed;

        // â”€â”€ Input device enumeration â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public static List<(int Index, string Name)> GetInputDevices()
        {
            var devices = new List<(int, string)>();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                devices.Add((i, caps.ProductName));
            }
            return devices;
        }

        public static List<(string Id, string Name)> GetLoopbackDevices()
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(d => (d.ID, d.FriendlyName))
                .ToList();
        }

        // â”€â”€ Start / Stop â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public void Start(int deviceIndex)
        {
            Start(deviceIndex, CaptureSourceMode.Microphone, null);
        }

        /// <summary>Process ID to capture when using AppLoopback mode.</summary>
        public uint AppLoopbackProcessId { get; set; }

        public void Start(int microphoneDeviceIndex, CaptureSourceMode sourceMode, string? loopbackDeviceId)
        {
            if (_captureIn != null) Stop();

            WaveFormat micFormat = RecordBitDepth == 32
                ? WaveFormat.CreateIeeeFloatWaveFormat(RecordSampleRate, RecordChannels)
                : new WaveFormat(RecordSampleRate, RecordBitDepth, RecordChannels);

            IWaveIn capture = sourceMode switch
            {
                CaptureSourceMode.OutputLoopback => CreateLoopbackCapture(loopbackDeviceId),
                CaptureSourceMode.AppLoopback => new ProcessLoopbackCapture(AppLoopbackProcessId),
                _ => new WaveInEvent
                {
                    DeviceNumber = microphoneDeviceIndex,
                    WaveFormat = micFormat,
                    BufferMilliseconds = 50
                }
            };

            _captureIn = capture;
            _captureFormat = capture.WaveFormat;
            _preBufferCapacity = (_captureFormat.AverageBytesPerSecond * PastBufferDurationMs) / 1000;
            _preBuffer.Clear();
            _preBufferBytes = 0;
            _isRecording = false;
            _captureBufferNow = false;

            _captureIn.DataAvailable += OnDataAvailable;
            _captureIn.RecordingStopped += OnRecordingStopped;
            _captureIn.StartRecording();
        }

        public void Stop()
        {
            _captureIn?.StopRecording();
        }

        /// <summary>
        /// In KeyBuffer mode: saves the current ring buffer contents as a new clip immediately.
        /// Safe to call from any thread.
        /// </summary>
        public void TriggerBufferCapture()
        {
            _captureBufferNow = true;
        }

        // â”€â”€ Audio processing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            // Apply input gain to the buffer before any processing
            ApplyGain(e.Buffer, e.BytesRecorded, _captureFormat, InputGain);

            (double L, double R) = ComputeNormalisedLevels(e.Buffer, e.BytesRecorded, _captureFormat);
            RmsLevelChanged?.Invoke(L, R);

            if (RecordingMode == AudioRecordingMode.KeyBuffer)
            {
                // Only maintain ring buffer and fire when triggered
                var chunk = new byte[e.BytesRecorded];
                Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
                _preBuffer.Enqueue(chunk);
                _preBufferBytes += chunk.Length;

                while (_preBufferBytes > _preBufferCapacity && _preBuffer.Count > 0)
                {
                    var removed = _preBuffer.Dequeue();
                    _preBufferBytes -= removed.Length;
                }

                if (_captureBufferNow)
                {
                    _captureBufferNow = false;
                    StartClip();
                    FinaliseClip();
                }
                return;
            }

            // ── AutoVAD mode ──
            // Convert the linear sensitivity (0-100) to a dB threshold that
            // matches the dB-scaled meter.  Slider 0 → -60 dB, 100 → 0 dB.
            double dbThreshold = (Sensitivity / 100.0) * 60.0 - 60.0;
            double dbL = (L <= 0) ? -100.0 : 20.0 * Math.Log10(L / 100.0);
            bool hasVoice = dbL > dbThreshold;

            if (hasVoice)
            {
                if (!_isRecording)
                    StartClip();

                _lastVoiceTime = DateTime.UtcNow;
                _recorder.AppendSamples(e.Buffer, 0, e.BytesRecorded);
            }
            else
            {
                if (_isRecording)
                {
                    _recorder.AppendSamples(e.Buffer, 0, e.BytesRecorded);

                    double silentMs = (DateTime.UtcNow - _lastVoiceTime).TotalMilliseconds;
                    if (silentMs >= SilenceTimeoutMs)
                        FinaliseClip();
                }
                else
                {
                    var chunk = new byte[e.BytesRecorded];
                    Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
                    _preBuffer.Enqueue(chunk);
                    _preBufferBytes += chunk.Length;

                    while (_preBufferBytes > _preBufferCapacity && _preBuffer.Count > 0)
                    {
                        var removed = _preBuffer.Dequeue();
                        _preBufferBytes -= removed.Length;
                    }
                }
            }
        }

        private void StartClip()
        {
            _isRecording = true;
            _lastVoiceTime = DateTime.UtcNow;

            var format = _captureFormat ?? new WaveFormat(16000, 16, 1);
            string requestedCodec = (RecordCodec ?? "wav").ToLowerInvariant();
            string codecToUse = requestedCodec;

            if (!IsCodecCompatibleWithFormat(requestedCodec, format, out string incompatibilityReason))
            {
                codecToUse = "wav";
                RecordCodec = "wav";
                CodecCompatibilityWarning?.Invoke(
                    $"{requestedCodec.ToUpperInvariant()} is not compatible with current input format ({format.SampleRate} Hz, {format.Channels} channel(s)). {incompatibilityReason} Falling back to WAV.");
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = Path.IsPathRooted(SaveFolder)
                ? SaveFolder
                : Path.Combine(AppContext.BaseDirectory, SaveFolder);
            string ext = StreamingRecorderFactory.ExtensionFor(codecToUse);
            string filePath = Path.Combine(folder, $"Recording_{timestamp}.{ext}");

            _recorder = StreamingRecorderFactory.Create(codecToUse);
            _recorder.BeginRecording(filePath, format);

            foreach (var chunk in _preBuffer)
                _recorder.AppendSamples(chunk, 0, chunk.Length);
            _preBuffer.Clear();
            _preBufferBytes = 0;

            RecordingStateChanged?.Invoke(true);
        }

        private static bool IsCodecCompatibleWithFormat(string codec, WaveFormat format, out string reason)
        {
            reason = string.Empty;

            if (codec == "mp3")
            {
                int[] validRates = { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 };

                if (format.Channels > 2)
                {
                    reason = "MP3 supports only mono or stereo input.";
                    return false;
                }

                if (!validRates.Contains(format.SampleRate))
                {
                    reason = "MP3 requires one of these sample rates: 8k, 11.025k, 12k, 16k, 22.05k, 24k, 32k, 44.1k, 48k.";
                    return false;
                }

                return true;
            }

            if (codec == "opus")
            {
                int[] validRates = { 8000, 12000, 16000, 24000, 48000 };

                if (format.Channels > 2)
                {
                    reason = "Opus supports only mono or stereo input.";
                    return false;
                }

                if (!validRates.Contains(format.SampleRate))
                {
                    reason = "Opus requires one of these sample rates: 8k, 12k, 16k, 24k, 48k.";
                    return false;
                }

                return true;
            }

            return true;
        }

        private void FinaliseClip()
        {
            _isRecording = false;
            string? path = _recorder.CurrentFilePath;
            TimeSpan duration = _recorder.Finish();

            RecordingStateChanged?.Invoke(false);

            if (path != null && File.Exists(path) && duration.TotalMilliseconds > 200)
            {
                RecordingCompleted?.Invoke(new RecordingEntry
                {
                    FilePath = path,
                    Duration = duration
                });
            }
            else if (path != null && File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (_isRecording)
                FinaliseClip();

            _captureIn?.Dispose();
            _captureIn = null;
            _captureFormat = null;
        }

        // â”€â”€ RMS computation â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        /// <summary>
        /// Applies a gain multiplier to all samples in the buffer in-place.
        /// </summary>
        private static void ApplyGain(byte[] buffer, int count, WaveFormat? format, float gain)
        {
            if (format == null || gain == 1.0f) return;

            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
            int bytesPerSample = format.BitsPerSample / 8;

            if (isFloat && bytesPerSample == 4)
            {
                for (int i = 0; i <= count - 4; i += 4)
                {
                    float s = BitConverter.ToSingle(buffer, i);
                    s *= gain;
                    byte[] bytes = BitConverter.GetBytes(s);
                    buffer[i] = bytes[0];
                    buffer[i + 1] = bytes[1];
                    buffer[i + 2] = bytes[2];
                    buffer[i + 3] = bytes[3];
                }
            }
            else
            {
                if (bytesPerSample == 2)
                {
                    // PCM 16-bit
                    for (int i = 0; i <= count - 2; i += 2)
                    {
                        short s = (short)(buffer[i] | (buffer[i + 1] << 8));
                        int scaled = (int)(s * gain);
                        scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
                        short result = (short)scaled;
                        buffer[i] = (byte)(result & 0xFF);
                        buffer[i + 1] = (byte)((result >> 8) & 0xFF);
                    }
                }
                else if (bytesPerSample == 3)
                {
                    // PCM 24-bit
                    for (int i = 0; i <= count - 3; i += 3)
                    {
                        int sample = ReadPcm24(buffer, i);
                        int scaled = (int)(sample * gain);
                        scaled = Math.Clamp(scaled, -8388608, 8388607);
                        WritePcm24(buffer, i, scaled);
                    }
                }
                else if (bytesPerSample == 4)
                {
                    // PCM 32-bit integer
                    for (int i = 0; i <= count - 4; i += 4)
                    {
                        int sample = BitConverter.ToInt32(buffer, i);
                        long scaled = (long)(sample * gain);
                        scaled = Math.Clamp(scaled, int.MinValue, int.MaxValue);
                        byte[] bytes = BitConverter.GetBytes((int)scaled);
                        buffer[i] = bytes[0];
                        buffer[i + 1] = bytes[1];
                        buffer[i + 2] = bytes[2];
                        buffer[i + 3] = bytes[3];
                    }
                }
            }
        }

        /// <summary>
        /// Returns normalised (L, R) levels 0-100.
        /// Mono: L == R. Stereo/multi-channel: uses channels 0/1 as front-left/front-right.
        /// </summary>
        private static (double L, double R) ComputeNormalisedLevels(byte[] buffer, int count, WaveFormat? format)
        {
            if (count <= 0 || format == null) return (0, 0);

            int channels = format.Channels;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;
            int bytesPerSample = format.BitsPerSample / 8;
            if (bytesPerSample <= 0) return (0, 0);

            double rmsL, rmsR;

            if (isFloat && bytesPerSample == 4)
            {
                int sampleCount = count / 4;
                if (sampleCount == 0) return (0, 0);

                if (channels >= 2)
                {
                    // Stereo or multi-channel: track front-left/front-right channels.
                    double sumL = 0, sumR = 0;
                    int samplesL = 0, samplesR = 0;
                    for (int i = 0; i <= count - 4; i += 4)
                    {
                        float s = BitConverter.ToSingle(buffer, i);
                        int sampleIndex = i / 4;
                        int channelIndex = sampleIndex % channels;
                        if (channelIndex == 0) { sumL += s * s; samplesL++; }
                        else if (channelIndex == 1) { sumR += s * s; samplesR++; }
                    }
                    rmsL = samplesL > 0 ? Math.Sqrt(sumL / samplesL) : 0;
                    rmsR = samplesR > 0 ? Math.Sqrt(sumR / samplesR) : 0;
                }
                else
                {
                    // Mono source
                    double sum = 0;
                    int samples = 0;
                    for (int i = 0; i <= count - 4; i += 4)
                    {
                        float s = BitConverter.ToSingle(buffer, i);
                        sum += s * s;
                        samples++;
                    }
                    rmsL = samples > 0 ? Math.Sqrt(sum / samples) : 0;
                    rmsR = rmsL;
                }
            }
            else
            {
                // PCM 16/24/32-bit integer
                if (channels >= 2)
                {
                    double sumL = 0, sumR = 0;
                    int samplesL = 0, samplesR = 0;
                    int sampleIndex = 0;
                    for (int i = 0; i <= count - bytesPerSample; i += bytesPerSample)
                    {
                        double normalized = ReadIntPcmSampleAsDouble(buffer, i, bytesPerSample);
                        double sq = normalized * normalized;
                        int channelIndex = sampleIndex % channels;
                        if (channelIndex == 0) { sumL += sq; samplesL++; }
                        else if (channelIndex == 1) { sumR += sq; samplesR++; }
                        sampleIndex++;
                    }
                    double rawL = samplesL > 0 ? Math.Sqrt(sumL / samplesL) : 0;
                    double rawR = samplesR > 0 ? Math.Sqrt(sumR / samplesR) : 0;
                    rmsL = rawL;
                    rmsR = rawR;
                }
                else
                {
                    // Mono source
                    double sum = 0;
                    int samples = 0;
                    for (int i = 0; i <= count - bytesPerSample; i += bytesPerSample)
                    {
                        double normalized = ReadIntPcmSampleAsDouble(buffer, i, bytesPerSample);
                        sum += normalized * normalized;
                        samples++;
                    }
                    double raw = samples > 0 ? Math.Sqrt(sum / samples) : 0;
                    rmsL = raw;
                    rmsR = raw;
                }
            }

            double normL = Math.Min(100.0, rmsL * 500.0);
            double normR = Math.Min(100.0, rmsR * 500.0);
            return (normL, normR);
        }

        private static int ReadPcm24(byte[] buffer, int byteOffset)
        {
            int sample = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) | (buffer[byteOffset + 2] << 16);
            if ((sample & 0x800000) != 0)
                sample |= unchecked((int)0xFF000000);
            return sample;
        }

        private static void WritePcm24(byte[] buffer, int byteOffset, int sample)
        {
            buffer[byteOffset] = (byte)(sample & 0xFF);
            buffer[byteOffset + 1] = (byte)((sample >> 8) & 0xFF);
            buffer[byteOffset + 2] = (byte)((sample >> 16) & 0xFF);
        }

        private static double ReadIntPcmSampleAsDouble(byte[] buffer, int byteOffset, int bytesPerSample)
        {
            return bytesPerSample switch
            {
                2 => BitConverter.ToInt16(buffer, byteOffset) / 32768.0,
                3 => ReadPcm24(buffer, byteOffset) / 8388608.0,
                4 => BitConverter.ToInt32(buffer, byteOffset) / 2147483648.0,
                _ => 0.0
            };
        }

        private static WasapiLoopbackCapture CreateLoopbackCapture(string? loopbackDeviceId)
        {
            using var enumerator = new MMDeviceEnumerator();

            MMDevice? device = null;
            if (!string.IsNullOrWhiteSpace(loopbackDeviceId))
            {
                try { device = enumerator.GetDevice(loopbackDeviceId); }
                catch { device = null; }
            }

            device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return new WasapiLoopbackCapture(device);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _recorder.Dispose();
        }
    }
}
