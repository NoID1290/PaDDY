using System;
using System.IO;
using ManagedBass;
using NAudio.Wave;
using WaveFormat = NAudio.Wave.WaveFormat;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// NAudio <see cref="WaveStream"/> wrapper around BASS decode streams.
    /// Eliminates all Windows Media Foundation COM apartment / IMFSourceReader issues.
    /// </summary>
    public class BassReaderStream : WaveStream
    {
        private readonly int _streamHandle;
        private readonly WaveFormat _waveFormat;
        private readonly long _length;
        private readonly object _lock = new();
        private bool _disposed;

        public BassReaderStream(string fileName)
        {
            BassEngineManager.EnsureNativeLibraryLoaded();
            BassEngineManager.EnsureDeviceInitialized(0);
            _streamHandle = Bass.CreateStream(fileName, 0, 0, BassFlags.Decode);
            if (_streamHandle == 0)
            {
                throw new InvalidOperationException($"BASS failed to open '{Path.GetFileName(fileName)}': {Bass.LastError}");
            }

            var info = Bass.ChannelGetInfo(_streamHandle);
            _waveFormat = new WaveFormat(info.Frequency, 16, info.Channels);
            _length = Bass.ChannelGetLength(_streamHandle, PositionFlags.Bytes);
        }

        public override WaveFormat WaveFormat => _waveFormat;
        public override long Length => _length;

        public override long Position
        {
            get
            {
                lock (_lock)
                {
                    if (_disposed || _streamHandle == 0) return 0;
                    return Bass.ChannelGetPosition(_streamHandle, PositionFlags.Bytes);
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_disposed || _streamHandle == 0) return;
                    Bass.ChannelSetPosition(_streamHandle, value, PositionFlags.Bytes);
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_disposed || _streamHandle == 0) return 0;
                if (offset == 0)
                {
                    int read = Bass.ChannelGetData(_streamHandle, buffer, count);
                    return Math.Max(0, read);
                }
                else
                {
                    byte[] temp = new byte[count];
                    int read = Bass.ChannelGetData(_streamHandle, temp, count);
                    if (read > 0)
                    {
                        Buffer.BlockCopy(temp, 0, buffer, offset, read);
                    }
                    return Math.Max(0, read);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_lock)
                {
                    if (!_disposed)
                    {
                        _disposed = true;
                        if (_streamHandle != 0)
                        {
                            Bass.StreamFree(_streamHandle);
                        }
                    }
                }
            }
            base.Dispose(disposing);
        }
    }
}
