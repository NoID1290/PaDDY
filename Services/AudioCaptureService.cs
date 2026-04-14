using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PaDDY.Models;

namespace PaDDY.Services
{
    public enum CaptureSourceMode
    {
        Microphone = 0,
        OutputLoopback = 1
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

        public void Start(int microphoneDeviceIndex, CaptureSourceMode sourceMode, string? loopbackDeviceId)
        {
            if (_captureIn != null) Stop();

            WaveFormat micFormat = RecordBitDepth == 32
                ? WaveFormat.CreateIeeeFloatWaveFormat(RecordSampleRate, RecordChannels)
                : new WaveFormat(RecordSampleRate, RecordBitDepth, RecordChannels);

            IWaveIn capture = sourceMode switch
            {
                CaptureSourceMode.OutputLoopback => CreateLoopbackCapture(loopbackDeviceId),
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

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string folder = Path.IsPathRooted(SaveFolder)
                ? SaveFolder
                : Path.Combine(AppContext.BaseDirectory, SaveFolder);
            string ext = StreamingRecorderFactory.ExtensionFor(RecordCodec);
            string filePath = Path.Combine(folder, $"Recording_{timestamp}.{ext}");

            _recorder = StreamingRecorderFactory.Create(RecordCodec);
            var format = _captureFormat ?? new WaveFormat(16000, 16, 1);
            _recorder.BeginRecording(filePath, format);

            foreach (var chunk in _preBuffer)
                _recorder.AppendSamples(chunk, 0, chunk.Length);
            _preBuffer.Clear();
            _preBufferBytes = 0;

            RecordingStateChanged?.Invoke(true);
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
            if (isFloat)
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
        }

        /// <summary>
        /// Returns normalised (L, R) levels 0-100.
        /// For mono or unknown formats, L == R.
        /// </summary>
        private static (double L, double R) ComputeNormalisedLevels(byte[] buffer, int count, WaveFormat? format)
        {
            if (count <= 0 || format == null) return (0, 0);

            bool isStereo = format.Channels == 2;
            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32;

            double rmsL, rmsR;

            if (isFloat)
            {
                int sampleCount = count / 4;
                if (sampleCount == 0) return (0, 0);

                double sumL = 0, sumR = 0;
                int samplesL = 0, samplesR = 0;

                for (int i = 0; i <= count - 4; i += 4)
                {
                    float s = BitConverter.ToSingle(buffer, i);
                    int sampleIndex = i / 4;
                    if (isStereo)
                    {
                        if (sampleIndex % 2 == 0) { sumL += s * s; samplesL++; }
                        else { sumR += s * s; samplesR++; }
                    }
                    else { sumL += s * s; samplesL++; }
                }

                rmsL = samplesL > 0 ? Math.Sqrt(sumL / samplesL) : 0;
                rmsR = isStereo ? (samplesR > 0 ? Math.Sqrt(sumR / samplesR) : 0) : rmsL;
            }
            else
            {
                // PCM 16-bit
                if (count < 2) return (0, 0);

                long sumL = 0, sumR = 0;
                int samplesL = 0, samplesR = 0;
                int sampleIndex = 0;

                for (int i = 0; i <= count - 2; i += 2)
                {
                    short s = (short)(buffer[i] | (buffer[i + 1] << 8));
                    long sq = (long)s * s;
                    if (isStereo)
                    {
                        if (sampleIndex % 2 == 0) { sumL += sq; samplesL++; }
                        else { sumR += sq; samplesR++; }
                    }
                    else { sumL += sq; samplesL++; }
                    sampleIndex++;
                }

                double rawL = samplesL > 0 ? Math.Sqrt((double)sumL / samplesL) / short.MaxValue : 0;
                double rawR = isStereo ? (samplesR > 0 ? Math.Sqrt((double)sumR / samplesR) / short.MaxValue : 0) : rawL;
                rmsL = rawL;
                rmsR = rawR;
            }

            double normL = Math.Min(100.0, rmsL * 500.0);
            double normR = Math.Min(100.0, rmsR * 500.0);
            return (normL, isStereo ? normR : normL);
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
