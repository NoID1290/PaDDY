using System;
using System.IO;
using NAudio.Wave;
using OggVorbisEncoder;

namespace PaDDY.Services
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

            _vorbisInfo = VorbisInfo.InitVariableBitRate(format.Channels, format.SampleRate, 0.5f);
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

            int bytesPerSample = _format.BitsPerSample / 8;
            int interleaved = count / bytesPerSample;
            int channels = _format.Channels;
            int frames = interleaved / channels;

            // Convert to float[][] planar
            var pcm = new float[channels][];
            for (int ch = 0; ch < channels; ch++)
                pcm[ch] = new float[frames];

            for (int i = 0; i < frames; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    int idx = offset + (i * channels + ch) * bytesPerSample;
                    float sample;
                    if (_format.BitsPerSample == 16)
                        sample = BitConverter.ToInt16(buffer, idx) / 32768f;
                    else
                        sample = Math.Clamp(BitConverter.ToSingle(buffer, idx), -1f, 1f);
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
