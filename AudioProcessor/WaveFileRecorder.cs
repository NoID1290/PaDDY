using System;
using System.IO;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Thin wrapper around NAudio WaveFileWriter.
    /// Call BeginRecording → AppendSamples (many times) → Finish.
    /// </summary>
    public sealed class WaveFileRecorder : IStreamingRecorder
    {
        private WaveFileWriter? _writer;
        private WaveFormat? _format;
        private string? _filePath;
        private bool _disposed;

        public bool IsRecording => _writer != null;
        public string? CurrentFilePath => _filePath;

        public void BeginRecording(string filePath, WaveFormat format)
        {
            if (_writer != null)
                throw new InvalidOperationException("Already recording. Call Finish() first.");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _filePath = filePath;
            _format = format;
            _writer = new WaveFileWriter(filePath, format);
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            _writer?.Write(buffer, offset, count);
        }

        /// <summary>Finalises and closes the file. Returns the recorded duration.</summary>
        public TimeSpan Finish()
        {
            if (_writer == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_writer.Length / (double)_writer.WaveFormat.AverageBytesPerSecond);
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
            return duration;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }
}
