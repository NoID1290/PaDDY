using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using Paddy.Models;

namespace Paddy.Services
{
    /// <summary>
    /// Core voice-activity detection and recording engine.
    /// Uses a ring pre-buffer so the first syllables are never clipped.
    /// </summary>
    public sealed class AudioCaptureService : IDisposable
    {
        // ── Events ────────────────────────────────────────────────────────────
        public event Action<RecordingEntry>? RecordingCompleted;
        public event Action<double>? RmsLevelChanged;   // 0-100 normalised value
        public event Action<bool>? RecordingStateChanged; // true = started, false = stopped

        // ── Configuration properties (set before or during capture) ───────────
        /// <summary>RMS threshold 0-100. Voice above this is recorded.</summary>
        public double Sensitivity { get; set; } = 30.0;

        /// <summary>How long (ms) silence must persist before closing a clip.</summary>
        public double SilenceTimeoutMs { get; set; } = 700.0;

        public string SaveFolder { get; set; } = "recordings";

        // ── Internal state ────────────────────────────────────────────────────
        private WaveInEvent? _waveIn;
        private readonly WaveFileRecorder _recorder = new();
        private bool _isRecording;
        private DateTime _lastVoiceTime;

        // Ring pre-buffer: keeps the last ~500ms so voice onset isn't clipped
        private const int PreBufferMs = 500;
        private readonly Queue<byte[]> _preBuffer = new();
        private int _preBufferBytes;
        private int _preBufferCapacity;   // computed on start

        private bool _disposed;

        // ── Input device enumeration ──────────────────────────────────────────
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

        // ── Start / Stop ──────────────────────────────────────────────────────
        public void Start(int deviceIndex)
        {
            if (_waveIn != null) Stop();

            var format = new WaveFormat(16000, 16, 1); // 16kHz, 16-bit, mono
            _preBufferCapacity = (format.AverageBytesPerSecond * PreBufferMs) / 1000;
            _preBuffer.Clear();
            _preBufferBytes = 0;
            _isRecording = false;

            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceIndex,
                WaveFormat = format,
                BufferMilliseconds = 50
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
        }

        public void Stop()
        {
            _waveIn?.StopRecording();
            // Cleanup is handled in OnRecordingStopped
        }

        // ── Audio processing ──────────────────────────────────────────────────
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;

            double rms = ComputeRms(e.Buffer, e.BytesRecorded);
            double normalised = Math.Min(100.0, rms / 32768.0 * 100.0 * 5.0); // scale to 0-100
            RmsLevelChanged?.Invoke(normalised);

            bool hasVoice = normalised > Sensitivity;

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
                    // Still write during silence timeout to include natural pauses
                    _recorder.AppendSamples(e.Buffer, 0, e.BytesRecorded);

                    double silentMs = (DateTime.UtcNow - _lastVoiceTime).TotalMilliseconds;
                    if (silentMs >= SilenceTimeoutMs)
                        FinaliseClip();
                }
                else
                {
                    // Not recording — maintain the pre-buffer ring
                    var chunk = new byte[e.BytesRecorded];
                    Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
                    _preBuffer.Enqueue(chunk);
                    _preBufferBytes += chunk.Length;

                    // Trim pre-buffer to capacity
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
            string filePath = Path.Combine(folder, $"Recording_{timestamp}.wav");

            _recorder.BeginRecording(filePath, new WaveFormat(16000, 16, 1));

            // Flush pre-buffer into the recording so onset is captured
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
                // Discard very short (noise) clips
                try { File.Delete(path); } catch { }
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (_isRecording)
                FinaliseClip();

            _waveIn?.Dispose();
            _waveIn = null;
        }

        // ── RMS computation ───────────────────────────────────────────────────
        private static double ComputeRms(byte[] buffer, int count)
        {
            if (count < 2) return 0;
            long sumSq = 0;
            int samples = count / 2;

            for (int i = 0; i < count - 1; i += 2)
            {
                short sample = (short)(buffer[i] | (buffer[i + 1] << 8));
                sumSq += (long)sample * sample;
            }

            return Math.Sqrt((double)sumSq / samples);
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
