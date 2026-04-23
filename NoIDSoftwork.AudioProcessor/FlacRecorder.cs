using System;
using System.IO;
using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Lossless FLAC recorder backed by CUETools.Codecs.FLAKE (pure-C# encoder).
    /// Supports 16-bit and 24-bit integer PCM input directly; 32-bit float and
    /// 32-bit int input are converted to 24-bit integer samples before encoding.
    /// Multi-channel loopback sources are downmixed to stereo before encoding.
    /// </summary>
    public sealed class FlacRecorder : IStreamingRecorder
    {
        private FlakeWriter? _writer;
        private AudioPCMConfig? _pcmConfig;
        private WaveFormat? _format;
        private LoopbackFormatConverter? _converter;
        private long _framesWritten;
        private int _sampleRate;

        public bool IsRecording { get; private set; }
        public string? CurrentFilePath { get; private set; }

        public void BeginRecording(string filePath, WaveFormat waveFormat)
        {
            if (_writer != null)
                throw new InvalidOperationException("Already recording. Call Finish() first.");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            _format = waveFormat;
            _sampleRate = waveFormat.SampleRate;
            _framesWritten = 0;

            if (waveFormat.Channels > 2)
            {
                _converter = new LoopbackFormatConverter(waveFormat, waveFormat.SampleRate);
            }
            else
            {
                _converter = null;
            }

            // FLAC encoder requires integer PCM: 16 or 24 bps.
            // Float input → 24-bit; 32-bit int → 24-bit (top 24 bits); others preserved.
            int flacBps = (_converter != null || waveFormat.Encoding == WaveFormatEncoding.IeeeFloat) ? 24
                        : waveFormat.BitsPerSample is 16 or 24 ? waveFormat.BitsPerSample
                        : 24;

            int flacChannels = _converter?.OutputFormat.Channels ?? waveFormat.Channels;
            _pcmConfig = new AudioPCMConfig(flacBps, flacChannels, waveFormat.SampleRate);

            // Path is passed; stream is opened lazily by FlakeWriter on the first Write() call.
            _writer = new FlakeWriter(filePath, null, _pcmConfig) { CompressionLevel = 5 };
            CurrentFilePath = filePath;
            IsRecording = true;
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            if (_writer == null || _format == null || _pcmConfig == null || count <= 0)
                return;

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

                var (convertedBuffer, convertedCount) = _converter.Process(input, count);
                WriteConvertedSamples(convertedBuffer, convertedCount);
                return;
            }

            // Align to a whole source frame.
            count -= count % _format.BlockAlign;
            if (count <= 0) return;

            int frameCount = count / _format.BlockAlign;
            bool srcIsFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32;
            bool srcIs32Int = _format.Encoding == WaveFormatEncoding.Pcm && _format.BitsPerSample == 32;

            if (!srcIsFloat && !srcIs32Int && _format.BitsPerSample == _pcmConfig.BitsPerSample)
            {
                // 16-bit or 24-bit integer PCM: copy bytes and pass directly.
                // AudioBuffer.BytesToFLACSamples() handles 16-bit and 24-bit natively.
                byte[] data = new byte[count];
                Buffer.BlockCopy(buffer, offset, data, 0, count);
                _writer.Write(new AudioBuffer(_pcmConfig, data, frameCount));
            }
            else
            {
                // Conversion needed: fill an int[frame, channel] array.
                int channels = _pcmConfig.ChannelCount;
                int srcBytesPerSample = _format.BitsPerSample / 8;
                var samples = new int[frameCount, channels];

                for (int f = 0; f < frameCount; f++)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        int idx = offset + (f * channels + ch) * srcBytesPerSample;
                        samples[f, ch] = ReadAsFlacSample(buffer, idx, srcBytesPerSample, srcIsFloat, _pcmConfig.BitsPerSample);
                    }
                }

                _writer.Write(new AudioBuffer(_pcmConfig, samples, frameCount));
            }

            _framesWritten += frameCount;
        }

        private void WriteConvertedSamples(byte[] buffer, int count)
        {
            if (_writer == null || _pcmConfig == null || count <= 0)
                return;

            int bytesPerSample = sizeof(float);
            int bytesPerFrame = _pcmConfig.ChannelCount * bytesPerSample;
            count -= count % bytesPerFrame;
            if (count <= 0)
                return;

            int frameCount = count / bytesPerFrame;
            var samples = new int[frameCount, _pcmConfig.ChannelCount];

            for (int frame = 0; frame < frameCount; frame++)
            {
                for (int channel = 0; channel < _pcmConfig.ChannelCount; channel++)
                {
                    int sampleOffset = (frame * _pcmConfig.ChannelCount + channel) * bytesPerSample;
                    samples[frame, channel] = ReadAsFlacSample(buffer, sampleOffset, bytesPerSample, srcIsFloat: true, _pcmConfig.BitsPerSample);
                }
            }

            _writer.Write(new AudioBuffer(_pcmConfig, samples, frameCount));
            _framesWritten += frameCount;
        }

        /// <summary>
        /// Converts a single raw sample from the source buffer to an integer value
        /// scaled for the target FLAC bit depth.
        /// </summary>
        private static int ReadAsFlacSample(byte[] buf, int byteOffset, int srcBytesPerSample, bool srcIsFloat, int targetBits)
        {
            if (srcIsFloat)
            {
                float f = Math.Clamp(BitConverter.ToSingle(buf, byteOffset), -1f, 1f);
                return targetBits == 24 ? (int)(f * 8388607f) : (int)(f * 32767f);
            }

            // 32-bit integer PCM → keep only the top targetBits bits.
            int raw = BitConverter.ToInt32(buf, byteOffset);
            return raw >> (32 - targetBits);
        }

        public TimeSpan Finish()
        {
            IsRecording = false;
            var duration = TimeSpan.FromSeconds(_framesWritten / (double)_sampleRate);
            _writer?.Close();
            _writer = null;
            _converter = null;
            return duration;
        }

        public void Dispose()
        {
            // Close() finalises stream headers; safe to call even if Finish() was already called.
            _writer?.Dispose();
            _writer = null;
            _converter = null;
        }
    }
}
