using System;
using System.IO;
using NAudio.Wave;
using OggVorbisEncoder;

namespace NoIDSoftwork.AudioProcessor
{
    public sealed class VorbisRecorder : IStreamingRecorder
    {
        private FileStream? _fileStream;
        private VorbisInfo? _vorbisInfo;
        private ProcessingState? _processingState;
        private OggStream? _oggStream;
        private string? _filePath;
        private WaveFormat? _format;
        private long _samplesWritten;
        private bool _disposed;
        private bool _headersWritten;
        private int _encodeChannels;

        // Vorbis path downmix for surround loopback capture
        private LoopbackFormatConverter? _converter;

        public bool IsRecording => _fileStream != null;
        public string? CurrentFilePath => _filePath;

        public void BeginRecording(string filePath, WaveFormat format)
        {
            if (_fileStream != null)
                throw new InvalidOperationException("Already recording. Call Finish() first.");

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _filePath = filePath;
            _format = format;
            _samplesWritten = 0;
            _headersWritten = false;

            // Keep Vorbis encoder on a stable mono/stereo layout for loopback sources.
            _encodeChannels = Math.Clamp(format.Channels, 1, 2);
            _converter = format.Channels > 2
                ? new LoopbackFormatConverter(format, format.SampleRate)
                : null;

            _vorbisInfo = VorbisInfo.InitVariableBitRate(_encodeChannels, format.SampleRate, 0.5f);
            _processingState = ProcessingState.Create(_vorbisInfo);
            _oggStream = new OggStream(new Random().Next());
            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);

            // Write the three required Ogg/Vorbis header packets
            var comments = new Comments();
            _oggStream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(_vorbisInfo));
            _oggStream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(comments));
            _oggStream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(_vorbisInfo));
            FlushPages(force: true);
            _headersWritten = true;
        }

        public void AppendSamples(byte[] buffer, int offset, int count)
        {
            if (_processingState == null || _oggStream == null || _format == null || !_headersWritten) return;

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
                int convBytesPerSample = _converter.OutputFormat.BitsPerSample / 8;
                int convChannels = _converter.OutputFormat.Channels;
                int convInterleaved = outCount / convBytesPerSample;
                int convFrames = convInterleaved / convChannels;
                bool convIsFloat = _converter.OutputFormat.Encoding == WaveFormatEncoding.IeeeFloat && _converter.OutputFormat.BitsPerSample == 32;

                var convPcm = new float[convChannels][];
                for (int ch = 0; ch < convChannels; ch++)
                    convPcm[ch] = new float[convFrames];

                for (int i = 0; i < convFrames; i++)
                {
                    for (int ch = 0; ch < convChannels; ch++)
                    {
                        int idx = (i * convChannels + ch) * convBytesPerSample;
                        convPcm[ch][i] = ReadSampleAsFloat(outBuf, idx, convBytesPerSample, convIsFloat);
                    }
                }

                _processingState.WriteData(convPcm, convFrames);
                DrainPackets();
                _samplesWritten += convFrames;
                return;
            }

            int bytesPerSample = _format.BitsPerSample / 8;
            int interleaved = count / bytesPerSample;
            int channels = _format.Channels;
            int frames = interleaved / channels;
            bool isFloat = _format.Encoding == WaveFormatEncoding.IeeeFloat && _format.BitsPerSample == 32;

            // Convert to float[][] planar
            var pcm = new float[channels][];
            for (int ch = 0; ch < channels; ch++)
                pcm[ch] = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + (i * channels + ch) * bytesPerSample;
                    float sample = ReadSampleAsFloat(buffer, idx, bytesPerSample, isFloat);
                    pcm[ch][i] = sample;
                }
            }

            _processingState.WriteData(pcm, frames);
            DrainPackets();
            _samplesWritten += frames;
        }

        public TimeSpan Finish()
        {
            if (_processingState == null || _format == null) return TimeSpan.Zero;

            var duration = TimeSpan.FromSeconds(_samplesWritten / (double)_format.SampleRate);
            _processingState.WriteEndOfStream();
            DrainPackets();
            FlushPages(force: true);
            CloseStreams();
            return duration;
        }

        private void DrainPackets()
        {
            if (_processingState == null || _oggStream == null) return;
            while (_processingState.PacketOut(out OggPacket packet))
            {
                _oggStream.PacketIn(packet);
                FlushPages(force: false);
            }
        }

        private void FlushPages(bool force)
        {
            if (_oggStream == null || _fileStream == null) return;
            while (_oggStream.PageOut(out OggPage page, force))
            {
                _fileStream.Write(page.Header, 0, page.Header.Length);
                _fileStream.Write(page.Body, 0, page.Body.Length);
            }
        }

        private void CloseStreams()
        {
            _fileStream?.Flush();
            _fileStream?.Dispose();
            _fileStream = null;
            _processingState = null;
            _oggStream = null;
            _vorbisInfo = null;
            _converter = null;
        }

        private static float ReadSampleAsFloat(byte[] buffer, int byteOffset, int bytesPerSample, bool isFloat)
        {
            if (isFloat)
                return Math.Clamp(BitConverter.ToSingle(buffer, byteOffset), -1f, 1f);

            return bytesPerSample switch
            {
                2 => BitConverter.ToInt16(buffer, byteOffset) / 32768f,
                3 => ReadPcm24(buffer, byteOffset) / 8388608f,
                4 => BitConverter.ToInt32(buffer, byteOffset) / 2147483648f,
                _ => 0f
            };
        }

        private static int ReadPcm24(byte[] buffer, int byteOffset)
        {
            int sample = buffer[byteOffset] | (buffer[byteOffset + 1] << 8) | (buffer[byteOffset + 2] << 16);
            if ((sample & 0x800000) != 0)
                sample |= unchecked((int)0xFF000000);
            return sample;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_processingState != null && _headersWritten)
                {
                    _processingState.WriteEndOfStream();
                    DrainPackets();
                    FlushPages(force: true);
                }
            }
            catch { }
            CloseStreams();
        }
    }
}
