using System;
using System.IO;
using System.Linq;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace PaDDY.Services
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

            // Opus only accepts 1 or 2 channels. Downmix surround sources at AppendSamples time.
            _encodeChannels = Math.Clamp(format.Channels, 1, 2);

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
            if (_oggWriter == null || _format == null) return;

            int bytesPerSample = _format.BitsPerSample / 8;
            int captureFrames = count / (bytesPerSample * _format.Channels);
            int captureSamples = captureFrames * _format.Channels;

            // Step 1: Convert capture bytes → 16-bit PCM at the full capture channel count
            var rawSamples = new short[captureSamples];
            if (_format.BitsPerSample == 16)
            {
                for (int i = 0; i < captureSamples; i++)
                    rawSamples[i] = BitConverter.ToInt16(buffer, offset + i * 2);
            }
            else if (_format.BitsPerSample == 32)
            {
                for (int i = 0; i < captureSamples; i++)
                {
                    float f = BitConverter.ToSingle(buffer, offset + i * 4);
                    rawSamples[i] = (short)Math.Clamp((int)(f * 32767f), short.MinValue, short.MaxValue);
                }
            }

            // Step 2: Downmix to _encodeChannels if the capture device has more channels.
            // Each destination channel averages all source channels at stride _encodeChannels.
            // e.g. for 5.1 → stereo: L = avg(FL, C, RL), R = avg(FR, LFE, RR)
            short[] inputSamples;
            int inputSampleCount;
            if (_format.Channels > _encodeChannels)
            {
                inputSampleCount = captureFrames * _encodeChannels;
                inputSamples = new short[inputSampleCount];
                int srcCh = _format.Channels;
                int dstCh = _encodeChannels;
                for (int frame = 0; frame < captureFrames; frame++)
                {
                    for (int ch = 0; ch < dstCh; ch++)
                    {
                        long sum = 0;
                        int cnt = 0;
                        for (int sch = ch; sch < srcCh; sch += dstCh)
                        {
                            sum += rawSamples[frame * srcCh + sch];
                            cnt++;
                        }
                        inputSamples[frame * dstCh + ch] = (short)(sum / cnt);
                    }
                }
            }
            else
            {
                inputSamples = rawSamples;
                inputSampleCount = captureSamples;
            }

            // Step 3: Resample if needed, then write to Ogg/Opus encoder
            if (_needsResampling && _resamplerInput != null && _resampler != null)
            {
                byte[] pcmBytes = new byte[inputSampleCount * 2];
                Buffer.BlockCopy(inputSamples, 0, pcmBytes, 0, pcmBytes.Length);
                _resamplerInput.AddSamples(pcmBytes, 0, pcmBytes.Length);

                var floatBuf = new float[_encodeRate * _encodeChannels]; // up to 1 sec per drain
                int read;
                while ((read = _resampler.Read(floatBuf, 0, floatBuf.Length)) > 0)
                {
                    var outSamples = new short[read];
                    for (int i = 0; i < read; i++)
                        outSamples[i] = (short)Math.Clamp((int)(floatBuf[i] * 32767f), short.MinValue, short.MaxValue);

                    _oggWriter.WriteSamples(outSamples, 0, read);
                    _samplesWritten += read / _encodeChannels;
                }
            }
            else
            {
                _oggWriter.WriteSamples(inputSamples, 0, inputSampleCount);
                _samplesWritten += inputSampleCount / _encodeChannels;
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
        }
    }
}
