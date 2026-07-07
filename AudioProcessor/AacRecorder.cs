using System;
using System.IO;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    public sealed class AacRecorder : IStreamingRecorder
    {
        private WaveFileWriter? _writer;
        private string? _filePath;
        private string? _tempWavPath;
        private WaveFormat? _format;
        private LoopbackFormatConverter? _converter;
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

            // Create temporary WAV path next to the target file
            _tempWavPath = filePath + ".tmp.wav";

            WaveFormat writerFormat = format;
            if (format.Channels > 2)
            {
                _converter = new LoopbackFormatConverter(format, format.SampleRate);
                writerFormat = _converter.OutputFormat;
            }
            else
            {
                _converter = null;
            }

            _writer = new WaveFileWriter(_tempWavPath, writerFormat);
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            if (_writer == null || count <= 0) return;

            if (_converter != null)
            {
                byte[] input;
                if (offset == 0)
                {
                    input = buffer;
                }
                else
                {
                    input = new byte[count];
                    Buffer.BlockCopy(buffer, offset, input, 0, count);
                }

                var (outBuf, outCount) = _converter.Process(input, count);
                if (outCount > 0)
                {
                    _writer.Write(outBuf, 0, outCount);
                }
            }
            else
            {
                _writer.Write(buffer, offset, count);
            }
        }

        public TimeSpan Finish()
        {
            if (_writer == null || _filePath == null || _tempWavPath == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_writer.Length / (double)_writer.WaveFormat.AverageBytesPerSecond);
            _writer.Flush();
            _writer.Dispose();
            _writer = null;

            try
            {
                if (File.Exists(_tempWavPath))
                {
                    using (var reader = new WaveFileReader(_tempWavPath))
                    {
                        MediaFoundationEncoder.EncodeToAac(reader, _filePath, 192000);
                    }
                }
            }
            finally
            {
                if (File.Exists(_tempWavPath))
                {
                    try
                    {
                        File.Delete(_tempWavPath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                _tempWavPath = null;
                _converter = null;
            }

            return duration;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_writer != null)
            {
                try
                {
                    _writer.Dispose();
                }
                catch { }
                _writer = null;
            }

            if (_tempWavPath != null && File.Exists(_tempWavPath))
            {
                try
                {
                    File.Delete(_tempWavPath);
                }
                catch { }
                _tempWavPath = null;
            }

            _converter = null;
        }
    }
}
