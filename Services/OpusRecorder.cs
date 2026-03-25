using System;
using System.IO;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Wave;

namespace Paddy.Services
{
    public sealed class OpusRecorder : IStreamingRecorder
    {
        private FileStream? _fileStream;
        private OpusOggWriteStream? _oggWriter;
        private string? _filePath;
        private WaveFormat? _format;
        private long _samplesWritten;
        private bool _disposed;

        public bool IsRecording => _oggWriter != null;
        public string? CurrentFilePath => _filePath;

        public void BeginRecording(string filePath, WaveFormat format)
        {
            if (_oggWriter != null)
                throw new InvalidOperationException("Already recording. Call Finish() first.");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _filePath = filePath;
            _format = format;
            _samplesWritten = 0;

            var encoder = OpusCodecFactory.CreateEncoder(format.SampleRate, format.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = 64000;

            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            _oggWriter = new OpusOggWriteStream(encoder, _fileStream, null, format.SampleRate);
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            if (_oggWriter == null || _format == null) return;

            // Convert PCM bytes to short[] samples
            int bytesPerSample = _format.BitsPerSample / 8;
            int sampleCount = count / bytesPerSample;
            var samples = new short[sampleCount];

            if (_format.BitsPerSample == 16)
            {
                for (int i = 0; i < sampleCount; i++)
                    samples[i] = BitConverter.ToInt16(buffer, offset + i * 2);
            }
            else if (_format.BitsPerSample == 32)
            {
                // 32-bit IEEE float → 16-bit PCM
                for (int i = 0; i < sampleCount; i++)
                {
                    float f = BitConverter.ToSingle(buffer, offset + i * 4);
                    samples[i] = (short)Math.Clamp((int)(f * 32767f), short.MinValue, short.MaxValue);
                }
            }

            _oggWriter.WriteSamples(samples, 0, sampleCount);
            _samplesWritten += sampleCount / _format.Channels;
        }

        public TimeSpan Finish()
        {
            if (_oggWriter == null || _format == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_samplesWritten / (double)_format.SampleRate);
            _oggWriter.Finish();
            _oggWriter = null;
            _fileStream?.Dispose();
            _fileStream = null;
            return duration;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _oggWriter?.Finish(); } catch { }
            _oggWriter = null;
            _fileStream?.Dispose();
            _fileStream = null;
        }
    }
}
