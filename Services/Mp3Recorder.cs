using System;
using System.IO;
using NAudio.Lame;
using NAudio.Wave;

namespace PaDDY.Services
{
    public sealed class Mp3Recorder : IStreamingRecorder
    {
        private LameMP3FileWriter? _writer;
        private string? _filePath;
        private WaveFormat? _format;
        private long _bytesWritten;
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
            _bytesWritten = 0;
            _writer = new LameMP3FileWriter(filePath, format, LAMEPreset.STANDARD);
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            _writer?.Write(buffer, offset, count);
            _bytesWritten += count;
        }

        public TimeSpan Finish()
        {
            if (_writer == null || _format == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_bytesWritten / (double)_format.AverageBytesPerSecond);
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
