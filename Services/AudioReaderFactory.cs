using System;
using System.IO;
using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using NAudio.Vorbis;
using NAudio.Wave;

namespace PaDDY.Services
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
    /// Opus reader adapter. OpusOggReadStream is forward-only; seeking re-opens the
    /// file and skips to the target position by decoding and discarding samples.
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
