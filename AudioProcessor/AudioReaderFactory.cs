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
                "m4a" => new AacReaderAdapter(filePath),
                "aac" => new AacReaderAdapter(filePath),
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
    ///
    /// Ground-truth decoder: CUETools <see cref="FlakeReader"/>.
    ///   • <see cref="TotalTime"/>   – derived from <c>FlakeReader.Length</c> (actual frame count).
    ///   • <see cref="CurrentTime"/> – seeks via <c>FlakeReader.Position</c> (sample-frame accurate).
    ///   • <see cref="Read"/>        – delivers integer PCM from FlakeReader (used for trim export).
    ///   • <see cref="AsSampleProvider"/> – wraps FlakeReader to yield IEEE-float samples (waveform).
    ///
    /// Playback only: <see cref="NAudio.Flac.FlacReader"/> is kept solely for
    /// <see cref="AsWaveProvider()"/> because WASAPI requires a WaveStream.
    /// All duration/seek/sample logic uses FlakeReader to stay consistent.
    /// </summary>
    internal sealed class FlacReaderAdapter : IUnifiedAudioReader
    {
        // Playback only — provides WaveStream for WASAPI
        private readonly FlacReader _playbackReader;

        // Ground-truth decoder — owns duration, seeking, raw byte output, and float samples
        private readonly FlakeReader _flakeReader;
        private readonly AudioPCMConfig _pcm;
        private readonly AudioBuffer _decodeBuffer;
        private readonly WaveFormat _waveFormat;
        private readonly TimeSpan _totalTime;
        private readonly string _filePath;

        // Rolling byte window from the last decoded FlakeReader block
        private byte[]? _pendingBytes;
        private int _pendingOffset;
        private int _pendingEnd;

        public FlacReaderAdapter(string filePath)
        {
            _filePath = filePath;
            _playbackReader = new FlacReader(filePath);

            _flakeReader = new FlakeReader(filePath, null);
            _pcm = _flakeReader.PCM;

            // Build a WaveFormat that exactly matches the integer PCM FlakeReader outputs
            _waveFormat = new WaveFormat(_pcm.SampleRate, _pcm.BitsPerSample, _pcm.ChannelCount);

            // FlakeReader.Length is the total number of PCM frames — the authoritative count
            _totalTime = TimeSpan.FromSeconds((double)_flakeReader.Length / _pcm.SampleRate);

            _decodeBuffer = new AudioBuffer(_pcm, 4096);
        }

        public WaveFormat WaveFormat => _waveFormat;
        public TimeSpan TotalTime => _totalTime;

        public TimeSpan CurrentTime
        {
            get => TimeSpan.FromSeconds((double)_flakeReader.Position / _pcm.SampleRate);
            set
            {
                // Seek via FlakeReader.Position (sample-frame granularity, no silent failures)
                long targetFrame = (long)(value.TotalSeconds * _pcm.SampleRate);
                targetFrame = Math.Clamp(targetFrame, 0L, _flakeReader.Length);
                _flakeReader.Position = targetFrame;

                // Discard any pending decoded bytes from before the seek
                _pendingBytes = null;
                _pendingOffset = 0;
                _pendingEnd = 0;

                // Keep the playback reader loosely in sync (best-effort; only used for WASAPI)
                try { _playbackReader.CurrentTime = value; } catch { }
            }
        }

        /// <summary>WASAPI playback only. Do not use for sample data.</summary>
        public IWaveProvider AsWaveProvider() => _playbackReader;

        /// <summary>
        /// Waveform rendering or playback — IEEE float samples decoded by FlakeReader.
        /// Wraps the adapter's own <see cref="_flakeReader"/>, which has already been seeked
        /// to the desired start position via <see cref="CurrentTime"/>.
        /// </summary>
        public ISampleProvider AsSampleProvider() => new FlakeReaderSampleProvider(_pcm, _flakeReader);

        /// <summary>
        /// Raw integer PCM bytes from FlakeReader — used by trim/export.
        /// Bytes are in the same format as <see cref="WaveFormat"/> (little-endian signed PCM).
        /// </summary>
        public int Read(byte[] buffer, int offset, int count)
        {
            int written = 0;
            int bytesPerSample = (_pcm.BitsPerSample + 7) / 8;
            int bytesPerFrame  = bytesPerSample * _pcm.ChannelCount;

            while (written < count)
            {
                // Drain any already-decoded bytes first
                if (_pendingOffset < _pendingEnd)
                {
                    int toCopy = Math.Min(count - written, _pendingEnd - _pendingOffset);
                    Buffer.BlockCopy(_pendingBytes!, _pendingOffset, buffer, offset + written, toCopy);
                    _pendingOffset += toCopy;
                    written += toCopy;
                    continue;
                }

                // Decode the next block
                int framesDecoded = _flakeReader.Read(_decodeBuffer, 4096);
                if (framesDecoded == 0) break;

                _pendingBytes  = _decodeBuffer.Bytes;
                _pendingOffset = 0;
                _pendingEnd    = framesDecoded * bytesPerFrame;
            }

            // Align to a whole frame boundary before returning
            int aligned = written - (written % bytesPerFrame);
            return aligned;
        }

        public void Dispose()
        {
            _playbackReader.Dispose();
            // FlakeReader does not implement IDisposable; nothing to dispose
        }

        // ── Float sample provider (waveform rendering) ─────────────────────

        private sealed class FlakeReaderSampleProvider : ISampleProvider
        {
            private readonly FlakeReader _flakeReader;
            private AudioBuffer _buf = null!;
            private int _channels;
            private int _bytesPerSample;
            private float _scale;
            private byte[]? _rawBytes;
            private int _bufByteOffset;
            private int _bufByteEnd;

            /// <summary>
            /// Shared-reader constructor: wraps an existing <see cref="FlakeReader"/> at
            /// its current position (used for playback after a seek).
            /// </summary>
            public FlakeReaderSampleProvider(AudioPCMConfig pcm, FlakeReader sharedReader)
            {
                _flakeReader = sharedReader;
                Init(pcm);
            }

            /// <summary>
            /// File-open constructor: opens an independent reader from position 0
            /// (kept for any caller that needs a standalone reader at the start of the file).
            /// </summary>
            public FlakeReaderSampleProvider(AudioPCMConfig pcm, string filePath)
            {
                // Open an independent FlakeReader so callers don't share read position
                _flakeReader = new FlakeReader(filePath, null);
                Init(pcm);
            }

            private void Init(AudioPCMConfig pcm)
            {
                _channels = pcm.ChannelCount;
                _bytesPerSample = (pcm.BitsPerSample + 7) / 8;
                _scale = pcm.BitsPerSample <= 16 ? 32768f
                       : pcm.BitsPerSample <= 24 ? 8388608f
                       : 2147483648f;
                _buf = new AudioBuffer(pcm, 4096);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(pcm.SampleRate, pcm.ChannelCount);
            }

            public WaveFormat WaveFormat { get; private set; } = null!;


            public int Read(float[] buffer, int offset, int count)
            {
                int written = 0;
                while (written < count)
                {
                    if (_bufByteOffset >= _bufByteEnd)
                    {
                        int framesRead = _flakeReader.Read(_buf, 4096);
                        if (framesRead == 0) break;
                        _rawBytes = _buf.Bytes;
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
            // VorbisWaveReader outputs IEEE float (32-bit); ToSampleProvider() wraps it correctly.
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

    // ── AAC (.m4a) ────────────────────────────────────────────────────────────

    internal sealed class AacReaderAdapter : IUnifiedAudioReader
    {
        private readonly MediaFoundationReader _reader;

        public AacReaderAdapter(string filePath) => _reader = new MediaFoundationReader(filePath);

        public WaveFormat WaveFormat => _reader.WaveFormat;
        public TimeSpan TotalTime => _reader.TotalTime;
        public TimeSpan CurrentTime { get => _reader.CurrentTime; set => _reader.CurrentTime = value; }

        public IWaveProvider AsWaveProvider() => _reader;
        public ISampleProvider AsSampleProvider() => _reader.ToSampleProvider();
        public int Read(byte[] buffer, int offset, int count) => _reader.Read(buffer, offset, count);

        public void Dispose() => _reader.Dispose();
    }
}
