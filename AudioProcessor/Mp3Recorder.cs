using System;
using System.IO;
using System.Linq;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NoIDSoftwork.AudioProcessor
{
    public sealed class Mp3Recorder : IStreamingRecorder
    {
        private static readonly int[] ValidMp3Rates = { 8000, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000 };

        private LameMP3FileWriter? _writer;
        private string? _filePath;
        private WaveFormat? _format;
        private long _samplesWritten;
        private bool _disposed;

        // Downmix converter for multi-channel sources (MP3 only supports mono/stereo)
        private LoopbackFormatConverter? _converter;
        private WaveFormat? _writerFormat;
        private int _encodeRate;
        private int _encodeChannels;
        private bool _needsResampling;
        private BufferedWaveProvider? _resamplerInput;
        private WdlResamplingSampleProvider? _resampler;

        public bool IsRecording => _writer != null;
        public string? CurrentFilePath => _filePath;

        public void BeginRecording(string filePath, WaveFormat format)
        {
            if (_writer != null)
                throw new InvalidOperationException("Already recording. Call Finish() first.");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _filePath = filePath;
            _format = format;
            _samplesWritten = 0;

            _encodeChannels = Math.Clamp(format.Channels, 1, 2);
            _encodeRate = ValidMp3Rates.OrderBy(r => Math.Abs(r - format.SampleRate)).First();
            _needsResampling = _encodeRate != format.SampleRate;

            if (format.Channels > 2)
            {
                // MP3 only supports mono/stereo — downmix via ITU-R BS.775 coefficients
                _converter = new LoopbackFormatConverter(format, format.SampleRate);
            }
            else
            {
                _converter = null;
            }

            if (_needsResampling)
            {
                var inputFmt = new WaveFormat(format.SampleRate, 16, _encodeChannels);
                _resamplerInput = new BufferedWaveProvider(inputFmt) { DiscardOnBufferOverflow = false };
                _resampler = new WdlResamplingSampleProvider(_resamplerInput.ToSampleProvider(), _encodeRate);
            }

            // Feed LAME a conservative format for maximum driver/loopback compatibility.
            _writerFormat = new WaveFormat(_encodeRate, 16, _encodeChannels);
            _writer = new LameMP3FileWriter(filePath, _writerFormat, LAMEPreset.STANDARD);
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            if (_writer == null || _format == null || count <= 0) return;

            short[] inputSamples;
            int inputSampleCount;

            if (_converter != null)
            {
                // Downmix multi-channel to stereo before encoding
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

                int channels = _converter.OutputFormat.Channels;
                int bytesPerFrame = channels * 4;
                int alignedOutCount = outCount - (outCount % bytesPerFrame);
                if (alignedOutCount <= 0) return;

                int sampleCount = alignedOutCount / 4;
                inputSamples = new short[sampleCount];

                for (int i = 0; i < sampleCount; i++)
                {
                    float f = BitConverter.ToSingle(outBuf, i * 4);
                    inputSamples[i] = (short)Math.Clamp((int)(f * 32767f), short.MinValue, short.MaxValue);
                }

                inputSampleCount = sampleCount;
            }
            else
            {
                int bytesPerSample = _format.BitsPerSample / 8;
                if (bytesPerSample <= 0) return;

                int bytesPerFrame = bytesPerSample * _format.Channels;
                int alignedCount = count - (count % bytesPerFrame);
                if (alignedCount <= 0) return;

                int captureFrames = alignedCount / bytesPerFrame;
                int captureSamples = captureFrames * _format.Channels;
                inputSamples = new short[captureSamples];

                if (_format.BitsPerSample == 16)
                {
                    for (int i = 0; i < captureSamples; i++)
                        inputSamples[i] = BitConverter.ToInt16(buffer, offset + i * 2);
                }
                else if (_format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32)
                {
                    for (int i = 0; i < captureSamples; i++)
                    {
                        float f = BitConverter.ToSingle(buffer, offset + i * 4);
                        inputSamples[i] = (short)Math.Clamp((int)(f * 32767f), short.MinValue, short.MaxValue);
                    }
                }
                else if (_format.BitsPerSample == 24)
                {
                    for (int i = 0; i < captureSamples; i++)
                    {
                        int sourceOffset = offset + i * 3;
                        inputSamples[i] = (short)Math.Clamp(ReadPcm24(buffer, sourceOffset) >> 8, short.MinValue, short.MaxValue);
                    }
                }
                else if (_format.BitsPerSample == 32)
                {
                    for (int i = 0; i < captureSamples; i++)
                    {
                        int sourceOffset = offset + i * 4;
                        int sample = BitConverter.ToInt32(buffer, sourceOffset);
                        inputSamples[i] = (short)Math.Clamp(sample >> 16, short.MinValue, short.MaxValue);
                    }
                }
                else
                {
                    return;
                }

                inputSampleCount = captureSamples;
            }

            if (_needsResampling && _resamplerInput != null && _resampler != null)
            {
                byte[] pcmBytes = new byte[inputSampleCount * 2];
                Buffer.BlockCopy(inputSamples, 0, pcmBytes, 0, pcmBytes.Length);
                _resamplerInput.AddSamples(pcmBytes, 0, pcmBytes.Length);

                var floatBuf = new float[_encodeRate * _encodeChannels];
                int read;
                while ((read = _resampler.Read(floatBuf, 0, floatBuf.Length)) > 0)
                {
                    int alignedRead = read - (read % _encodeChannels);
                    if (alignedRead <= 0)
                        continue;

                    var outBytes = new byte[alignedRead * 2];
                    for (int i = 0; i < alignedRead; i++)
                    {
                        short s = (short)Math.Clamp((int)(floatBuf[i] * 32767f), short.MinValue, short.MaxValue);
                        int o = i * 2;
                        outBytes[o] = (byte)(s & 0xFF);
                        outBytes[o + 1] = (byte)((s >> 8) & 0xFF);
                    }

                    _writer.Write(outBytes, 0, outBytes.Length);
                    _samplesWritten += alignedRead / _encodeChannels;
                }
            }
            else
            {
                int alignedInput = inputSampleCount - (inputSampleCount % _encodeChannels);
                if (alignedInput <= 0)
                    return;

                var outBytes = new byte[alignedInput * 2];
                Buffer.BlockCopy(inputSamples, 0, outBytes, 0, outBytes.Length);
                _writer.Write(outBytes, 0, outBytes.Length);
                _samplesWritten += alignedInput / _encodeChannels;
            }
        }

        private static int ReadPcm24(byte[] buffer, int byteOffset)
        {
            int sample = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) | (buffer[byteOffset + 2] << 16);
            if ((sample & 0x800000) != 0)
                sample |= unchecked((int)0xFF000000);
            return sample;
        }

        public TimeSpan Finish()
        {
            if (_writer == null || _writerFormat == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_samplesWritten / (double)_writerFormat.SampleRate);
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
            _resamplerInput = null;
            _resampler = null;
            _converter = null;
            return duration;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
            _resamplerInput = null;
            _resampler = null;
            _converter = null;
        }
    }
}
