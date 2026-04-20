using System;
using System.IO;
using System.Linq;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NoIDSoftwork.AudioProcessor
{
    public sealed class OpusRecorder : IStreamingRecorder
    {
        // Sample rates accepted by the Opus encoder (per RFC 6716 / Concentus)
        private static readonly int[] ValidOpusRates = { 8000, 12000, 16000, 24000, 48000 };

        private FileStream? _fileStream;
        private OpusOggWriteStream? _oggWriter;
        private string? _filePath;
        private WaveFormat? _format;
        private long _samplesWritten;
        private bool _disposed;

        // Resampling pipeline (used when capture rate is not a valid Opus rate, e.g. 44100 Hz)
        private BufferedWaveProvider? _resamplerInput;
        private WdlResamplingSampleProvider? _resampler;
        private bool _needsResampling;
        private int _encodeRate;
        // Opus only supports 1 or 2 channels; surround devices (5.1, 7.1) are downmixed.
        private int _encodeChannels;

        // Downmix converter for multi-channel sources using ITU-R BS.775 coefficients
        private LoopbackFormatConverter? _converter;

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

            // Snap to the nearest Opus-supported rate if the capture rate is not valid.
            // This is common with WASAPI loopback devices that run at 44100 Hz.
            _encodeRate = ValidOpusRates.Contains(format.SampleRate)
                ? format.SampleRate
                : ValidOpusRates.OrderBy(r => Math.Abs(r - format.SampleRate)).First();

            // Opus only accepts 1 or 2 channels. Downmix surround sources via ITU-R BS.775.
            _encodeChannels = Math.Clamp(format.Channels, 1, 2);

            // Set up multi-channel downmix converter if needed
            if (format.Channels > 2)
            {
                _converter = new LoopbackFormatConverter(format, format.SampleRate);
                // After conversion, data is stereo IEEE float at source sample rate
            }
            else
            {
                _converter = null;
            }

            _needsResampling = _encodeRate != format.SampleRate;

            if (_needsResampling)
            {
                // Feed 16-bit PCM at the capture rate (already downmixed to _encodeChannels)
                var inputFmt = new WaveFormat(format.SampleRate, 16, _encodeChannels);
                _resamplerInput = new BufferedWaveProvider(inputFmt) { DiscardOnBufferOverflow = false };
                _resampler = new WdlResamplingSampleProvider(_resamplerInput.ToSampleProvider(), _encodeRate);
            }

            var encoder = OpusCodecFactory.CreateEncoder(_encodeRate, _encodeChannels, OpusApplication.OPUS_APPLICATION_VOIP);
            encoder.Bitrate = 64000;

            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            _oggWriter = new OpusOggWriteStream(encoder, _fileStream, null, _encodeRate);
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            if (_oggWriter == null || _format == null || count <= 0) return;

            short[] inputSamples;
            int inputSampleCount;

            if (_converter != null)
            {
                // Multi-channel → stereo via ITU-R BS.775 downmix
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

                // outBuf is stereo IEEE float — convert to 16-bit PCM
                int bytesPerFrame = _encodeChannels * 4;
                int alignedOutCount = outCount - (outCount % bytesPerFrame);
                if (alignedOutCount <= 0) return;

                int stereoSamples = alignedOutCount / 4; // 4 bytes per float sample
                inputSamples = new short[stereoSamples];
                for (int i = 0; i < stereoSamples; i++)
                {
                    float f = BitConverter.ToSingle(outBuf, i * 4);
                    inputSamples[i] = (short)Math.Clamp((int)(f * 32767f), short.MinValue, short.MaxValue);
                }
                inputSampleCount = stereoSamples;
            }
            else
            {
                // Mono or stereo source — convert directly to 16-bit PCM
                int bytesPerSample = _format.BitsPerSample / 8;
                if (bytesPerSample <= 0) return;

                int captureFrames = count / (bytesPerSample * _format.Channels);
                if (captureFrames <= 0) return;

                int captureSamples = captureFrames * _format.Channels;

                var rawSamples = new short[captureSamples];
                if (_format.BitsPerSample == 16)
                {
                    for (int i = 0; i < captureSamples; i++)
                        rawSamples[i] = BitConverter.ToInt16(buffer, offset + i * 2);
                }
                else if (_format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32)
                {
                    for (int i = 0; i < captureSamples; i++)
                    {
                        float f = BitConverter.ToSingle(buffer, offset + i * 4);
                        rawSamples[i] = (short)Math.Clamp((int)(f * 32767f), short.MinValue, short.MaxValue);
                    }
                }
                else if (_format.BitsPerSample == 24)
                {
                    for (int i = 0; i < captureSamples; i++)
                    {
                        int idx = offset + i * 3;
                        int sample24 = buffer[idx] | (buffer[idx + 1] << 8) | (buffer[idx + 2] << 16);
                        if ((sample24 & 0x800000) != 0)
                            sample24 |= unchecked((int)0xFF000000);
                        rawSamples[i] = (short)Math.Clamp(sample24 >> 8, short.MinValue, short.MaxValue);
                    }
                }
                else if (_format.BitsPerSample == 32)
                {
                    for (int i = 0; i < captureSamples; i++)
                    {
                        int sample = BitConverter.ToInt32(buffer, offset + i * 4);
                        rawSamples[i] = (short)Math.Clamp(sample >> 16, short.MinValue, short.MaxValue);
                    }
                }
                else
                {
                    return;
                }

                inputSamples = rawSamples;
                inputSampleCount = captureSamples;
            }

            // Resample if needed, then write to Ogg/Opus encoder
            if (_needsResampling && _resamplerInput != null && _resampler != null)
            {
                byte[] pcmBytes = new byte[inputSampleCount * 2];
                Buffer.BlockCopy(inputSamples, 0, pcmBytes, 0, pcmBytes.Length);
                _resamplerInput.AddSamples(pcmBytes, 0, pcmBytes.Length);

                var floatBuf = new float[_encodeRate * _encodeChannels]; // up to 1 sec per drain
                int read;
                while ((read = _resampler.Read(floatBuf, 0, floatBuf.Length)) > 0)
                {
                    int alignedRead = read - (read % _encodeChannels);
                    if (alignedRead <= 0)
                        continue;

                    var outSamples = new short[alignedRead];
                    for (int i = 0; i < alignedRead; i++)
                        outSamples[i] = (short)Math.Clamp((int)(floatBuf[i] * 32767f), short.MinValue, short.MaxValue);

                    _oggWriter.WriteSamples(outSamples, 0, alignedRead);
                    _samplesWritten += alignedRead / _encodeChannels;
                }
            }
            else
            {
                int alignedInput = inputSampleCount - (inputSampleCount % _encodeChannels);
                if (alignedInput <= 0)
                    return;

                _oggWriter.WriteSamples(inputSamples, 0, alignedInput);
                _samplesWritten += alignedInput / _encodeChannels;
            }
        }

        public TimeSpan Finish()
        {
            if (_oggWriter == null || _format == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_samplesWritten / (double)_encodeRate);
            _oggWriter.Finish();
            _oggWriter = null;
            _fileStream?.Dispose();
            _fileStream = null;
            _resamplerInput = null;
            _resampler = null;
            _converter = null;
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
            _resamplerInput = null;
            _resampler = null;
            _converter = null;
        }
    }
}
