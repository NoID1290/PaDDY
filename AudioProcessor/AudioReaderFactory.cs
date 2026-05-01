using System;
using System.IO;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using CUETools.Codecs;
using CUETools.Codecs.FLAKE;
using NAudio.Flac;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Opens the right <see cref="IUnifiedAudioReader"/> based on file extension.
    /// </summary>
    public static class AudioReaderFactory
    {
        public static IUnifiedAudioReader Open(string filePath)
        {
            string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            return ext switch
            {
                "ogg" => new VorbisReaderAdapter(filePath),
                "opus" => new OpusReaderAdapter(filePath),
                "flac" => new FlacReaderAdapter(filePath),
                _ => new WavMp3ReaderAdapter(filePath)   // wav, mp3
            };
        }
    }

    // ── WAV / MP3 ─────────────────────────────────────────────────────────────

    internal sealed class WavMp3ReaderAdapter : IUnifiedAudioReader
    {
        private readonly AudioFileReader _reader;

        public WavMp3ReaderAdapter(string filePath) => _reader = new AudioFileReader(filePath);

        public WaveFormat WaveFormat => _reader.WaveFormat;
        public TimeSpan TotalTime => _reader.TotalTime;
        public TimeSpan CurrentTime { get => _reader.CurrentTime; set => _reader.CurrentTime = value; }

        public IWaveProvider AsWaveProvider() => _reader;
        public ISampleProvider AsSampleProvider() => _reader;
        public int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

        public void Dispose() => _reader.Dispose();
    }

    // ── FLAC ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// FLAC reader adapter.
    /// Playback uses NAudio.Flac.FlacReader (WaveStream → WasapiOut).
    /// Sample data for waveform rendering uses CUETools FlakeReader, which is the
    /// companion decoder to FlakeWriter and reliably decodes every FLAC file we write.
    /// </summary>
    internal sealed class FlacReaderAdapter : IUnifiedAudioReader
    {
        private readonly FlacReader _reader;
        private readonly string _filePath;

        public FlacReaderAdapter(string filePath)
        {
            _filePath = filePath;
            _reader = new FlacReader(filePath);
        }

        public WaveFormat WaveFormat => _reader.WaveFormat;
        public TimeSpan TotalTime => _reader.TotalTime;

        public TimeSpan CurrentTime
        {
            get => _reader.CurrentTime;
            set => _reader.CurrentTime = value;
        }

        public IWaveProvider AsWaveProvider() => _reader;

        // Use FlakeReader (CUETools) for float sample decoding — guaranteed compatibility
        // with files written by FlakeWriter.  NAudio.Flac.FlacReader.Read(float[]) is a
        // stub that returns -1 and cannot be used as an ISampleProvider.
        public ISampleProvider AsSampleProvider() => new FlakeReaderSampleProvider(_filePath);

        public int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

        public void Dispose() => _reader.Dispose();

        private sealed class FlakeReaderSampleProvider : ISampleProvider
        {
            private readonly FlakeReader _flakeReader;
            private readonly AudioBuffer _buf;
            private readonly int _channels;
            private readonly int _bytesPerSample;
            private readonly float _scale;
            private byte[]? _rawBytes;
            private int _bufByteOffset;
            private int _bufByteEnd;

            public FlakeReaderSampleProvider(string filePath)
            {
                _flakeReader = new FlakeReader(filePath, null);
                var pcm = _flakeReader.PCM;
                _channels = pcm.ChannelCount;
                _bytesPerSample = (pcm.BitsPerSample + 7) / 8;
                _scale = pcm.BitsPerSample <= 16 ? 32768f
                       : pcm.BitsPerSample <= 24 ? 8388608f
                       : 2147483648f;
                _buf = new AudioBuffer(pcm, 4096);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(pcm.SampleRate, pcm.ChannelCount);
            }

            public WaveFormat WaveFormat { get; }

            public int Read(float[] buffer, int offset, int count)
            {
                int written = 0;
                while (written < count)
                {
                    if (_bufByteOffset >= _bufByteEnd)
                    {
                        int framesRead = _flakeReader.Read(_buf, 4096);
                        if (framesRead == 0) break;
                        _rawBytes = _buf.Bytes; // AudioBuffer.Interlace stored PCM here
                        _bufByteOffset = 0;
                        _bufByteEnd = framesRead * _channels * _bytesPerSample;
                    }

                    int samplesAvail = (_bufByteEnd - _bufByteOffset) / _bytesPerSample;
                    int toCopy = Math.Min(samplesAvail, count - written);
                    for (int i = 0; i < toCopy; i++)
                        buffer[offset + written + i] = ToFloat(_rawBytes!, _bufByteOffset + i * _bytesPerSample);
                    _bufByteOffset += toCopy * _bytesPerSample;
                    written += toCopy;
                }
                return written;
            }

            private float ToFloat(byte[] buf, int off) => _bytesPerSample switch
            {
                1 => (buf[off] - 128) / 128f,
                2 => BitConverter.ToInt16(buf, off) / _scale,
                3 => Read24(buf, off) / _scale,
                _ => BitConverter.ToInt32(buf, off) / _scale,
            };

            private static int Read24(byte[] buf, int off)
            {
                int s = buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16);
                return (s & 0x800000) != 0 ? s | unchecked((int)0xFF000000) : s;
            }
        }
    }

    // ── Ogg Vorbis ────────────────────────────────────────────────────────────

    internal sealed class VorbisReaderAdapter : IUnifiedAudioReader
    {
        private readonly VorbisWaveReader _reader;

        public VorbisReaderAdapter(string filePath) => _reader = new VorbisWaveReader(filePath);

        public WaveFormat WaveFormat => _reader.WaveFormat;
        public TimeSpan TotalTime => _reader.TotalTime;
        public TimeSpan CurrentTime { get => _reader.CurrentTime; set => _reader.CurrentTime = value; }

        public IWaveProvider AsWaveProvider() => _reader;

        public ISampleProvider AsSampleProvider()
        {
            // VorbisWaveReader is a 16-bit provider; wrap in a sample provider
            return _reader.ToSampleProvider();
        }

        public int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

        public void Dispose() => _reader.Dispose();
    }

    // ── Opus ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opus reader adapter. OpusOggReadStream.SeekTo() works directly on a seekable FileStream.
    /// </summary>
    internal sealed class OpusReaderAdapter : IUnifiedAudioReader, IWaveProvider, ISampleProvider
    {
        private readonly string _filePath;
        private readonly FileStream _fileStream;
        private readonly OpusOggReadStream _readStream;
        private short[] _decodeBuf = Array.Empty<short>();
        private int _decodeBufOffset;
        private int _decodeBufCount;

        // Standard Opus output: 48 kHz, stereo, 16-bit
        private const int OpusSampleRate = 48000;
        private const int OpusChannels = 2;
        private readonly WaveFormat _waveFormat;
        private readonly TimeSpan _totalTime;
        private TimeSpan _currentTime;

        public OpusReaderAdapter(string filePath)
        {
            _filePath = filePath;
            _waveFormat = new WaveFormat(OpusSampleRate, 16, OpusChannels);

            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = OpusCodecFactory.CreateDecoder(OpusSampleRate, OpusChannels);
            _readStream = new OpusOggReadStream(decoder, _fileStream);
            _totalTime = _readStream.TotalTime;
        }

        public WaveFormat WaveFormat => _waveFormat;
        public TimeSpan TotalTime => _totalTime;

        public TimeSpan CurrentTime
        {
            get => _currentTime;
            set => SeekTo(value);
        }

        private void SeekTo(TimeSpan target)
        {
            // OpusOggReadStream.SeekTo() works directly on a seekable FileStream
            _readStream.SeekTo(target);
            _decodeBuf = Array.Empty<short>();
            _decodeBufOffset = 0;
            _decodeBufCount = 0;
            _currentTime = target;
        }

        public IWaveProvider AsWaveProvider() => this;
        public ISampleProvider AsSampleProvider() => this;

        // IWaveProvider / ISampleProvider share the same underlying decode
        WaveFormat IWaveProvider.WaveFormat => _waveFormat;
        WaveFormat ISampleProvider.WaveFormat => WaveFormat.CreateIeeeFloatWaveFormat(_waveFormat.SampleRate, _waveFormat.Channels);

        public int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;
            while (written < count)
            {
                // Drain existing decode buffer first
                while (_decodeBufCount > 0 && written < count)
                {
                    buffer[offset + written] = (byte)(_decodeBuf[_decodeBufOffset] & 0xFF);
                    buffer[offset + written + 1] = (byte)((_decodeBuf[_decodeBufOffset] >> 8) & 0xFF);
                    written += 2;
                    _decodeBufOffset++;
                    _decodeBufCount--;
                }
                if (written >= count) break;

                // Decode next packet
                if (!_readStream.HasNextPacket) break;
                _decodeBuf = _readStream.DecodeNextPacket() ?? Array.Empty<short>();
                _decodeBufOffset = 0;
                _decodeBufCount = _decodeBuf.Length;
            }

            if (written > 0)
                _currentTime += TimeSpan.FromSeconds(written / 2.0 / (_waveFormat.SampleRate * _waveFormat.Channels));

            return written;
        }

        int ISampleProvider.Read(float[] buffer, int offset, int count)
        {
            var pcm = new byte[count * 2];
            int got = Read(pcm, 0, count * 2);
            int samples = got / 2;
            for (int i = 0; i < samples; i++)
            {
                short s = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                buffer[offset + i] = s / 32768f;
            }
            return samples;
        }

        public void Dispose()
        {
            _fileStream.Dispose();
        }
    }
}
