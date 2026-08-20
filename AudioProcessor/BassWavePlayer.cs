using System;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using NAudio.Wave;
using NAudioPlaybackState = NAudio.Wave.PlaybackState;
using NAudioWaveFormat = NAudio.Wave.WaveFormat;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Implements NAudio's <see cref="IWavePlayer"/> interface using the ManagedBass (BASS 2.4) audio engine.
    /// Provides low-latency, hardware-accelerated playback while seamlessly accepting any NAudio <see cref="IWaveProvider"/>
    /// (including DSP effect chains, VST plugins, volume providers, and RMS meters).
    /// </summary>
    public sealed class BassWavePlayer : IWavePlayer
    {
        private readonly int _bassDeviceIndex;
        private readonly int _latencyMs;
        private readonly object _stateLock = new();
        private readonly object _readLock = new();

        private int _streamHandle;
        private IWaveProvider? _waveProvider;
        private StreamProcedure? _streamProcedure;
        private SyncProcedure? _endSyncProcedure;
        private byte[]? _buffer;
        private float _volume = 1.0f;
        private volatile NAudioPlaybackState _playbackState = NAudioPlaybackState.Stopped;
        private bool _disposed;

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        /// <summary>
        /// Initializes a new instance of <see cref="BassWavePlayer"/>.
        /// </summary>
        /// <param name="bassDeviceIndex">BASS device index (-1 for default, 1..N for specific device).</param>
        /// <param name="latencyMs">Desired playback buffer latency in milliseconds (default 100ms).</param>
        public BassWavePlayer(int bassDeviceIndex = -1, int latencyMs = 100)
        {
            _bassDeviceIndex = bassDeviceIndex;
            _latencyMs = latencyMs;
        }

        public NAudioPlaybackState PlaybackState => _playbackState;

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0f, 1.0f);
                int handle = _streamHandle;
                if (handle != 0)
                {
                    Bass.ChannelSetAttribute(handle, ChannelAttribute.Volume, _volume);
                }
            }
        }

        public NAudioWaveFormat OutputWaveFormat => _waveProvider?.WaveFormat ?? NAudioWaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public void Init(IWaveProvider waveProvider)
        {
            ArgumentNullException.ThrowIfNull(waveProvider);

            lock (_stateLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(BassWavePlayer));

                // Clean up previous stream if any
                FreeStream();

                _waveProvider = waveProvider;
                var format = waveProvider.WaveFormat;

                // Ensure BASS is initialized on this device
                int targetDev = _bassDeviceIndex == -1 ? BassEngineManager.GetDefaultDeviceIndex() : _bassDeviceIndex;

                // Configure BASS low-latency playback buffer and 10ms update period for fluid meter animations
                try
                {
                    Bass.UpdatePeriod = 10;
                    Bass.PlaybackBufferLength = Math.Clamp(_latencyMs, 50, 200);
                    Bass.DeviceBufferLength = 10;
                }
                catch { }

                if (!BassEngineManager.EnsureDeviceInitialized(_bassDeviceIndex, format.SampleRate))
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize BASS on device index {_bassDeviceIndex}. Last error: {Bass.LastError}");
                }

                // Select target device for stream creation
                if (targetDev > 0 && targetDev < Bass.DeviceCount)
                {
                    try
                    {
                        Bass.CurrentDevice = targetDev;
                    }
                    catch { }
                }

                // Determine flags based on WaveFormat
                var flags = BassFlags.Default;
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    flags |= BassFlags.Float;
                }

                // Keep delegate alive to avoid Garbage Collection while unmanaged code holds pointer
                _streamProcedure = StreamCallback;

                _streamHandle = Bass.CreateStream(
                    format.SampleRate,
                    format.Channels,
                    flags,
                    _streamProcedure,
                    IntPtr.Zero);

                if (_streamHandle == 0)
                {
                    throw new InvalidOperationException(
                        $"Failed to create BASS stream for format {format}. Last error: {Bass.LastError}");
                }

                // Apply initial volume
                Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, _volume);

                // Set up end of stream sync notification
                _endSyncProcedure = (handle, channel, data, user) =>
                {
                    ThreadPool.QueueUserWorkItem(_ => OnStreamEnded());
                };
                Bass.ChannelSetSync(_streamHandle, SyncFlags.End, 0, _endSyncProcedure, IntPtr.Zero);
            }
        }

        private int StreamCallback(int handle, IntPtr buffer, int length, IntPtr user)
        {
            if (_disposed || _waveProvider == null)
            {
                return unchecked((int)0x80000000); // BASS_STREAMPROC_END
            }

            var state = _playbackState;
            if (state == NAudioPlaybackState.Stopped)
            {
                return unchecked((int)0x80000000);
            }

            if (state == NAudioPlaybackState.Paused)
            {
                // Return 0 when paused to stall the stream without terminating it
                return 0;
            }

            lock (_readLock)
            {
                if (_disposed || _waveProvider == null || _playbackState == NAudioPlaybackState.Stopped)
                {
                    return unchecked((int)0x80000000);
                }

                if (_buffer == null || _buffer.Length < length)
                {
                    _buffer = new byte[length];
                }

                int bytesRead = _waveProvider.Read(_buffer, 0, length);
                if (bytesRead <= 0)
                {
                    return unchecked((int)0x80000000); // End of stream
                }

                Marshal.Copy(_buffer, 0, buffer, bytesRead);
                return bytesRead;
            }
        }

        public void Play()
        {
            lock (_stateLock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(BassWavePlayer));

                if (_streamHandle == 0)
                    throw new InvalidOperationException("Player must be initialized with Init() before calling Play().");

                if (_playbackState != NAudioPlaybackState.Playing)
                {
                    // Transition state to Playing BEFORE starting channel so StreamCallback delivers data immediately
                    _playbackState = NAudioPlaybackState.Playing;

                    bool success = Bass.ChannelPlay(_streamHandle, false);
                    if (!success)
                    {
                        // Try restarting if channel was previously at end
                        success = Bass.ChannelPlay(_streamHandle, true);
                        if (!success)
                        {
                            _playbackState = NAudioPlaybackState.Stopped;
                        }
                    }
                }
            }
        }

        public void Pause()
        {
            lock (_stateLock)
            {
                if (_disposed || _streamHandle == 0) return;

                if (_playbackState == NAudioPlaybackState.Playing)
                {
                    _playbackState = NAudioPlaybackState.Paused;
                    Bass.ChannelPause(_streamHandle);
                }
            }
        }

        public void Stop()
        {
            bool shouldFireStopped = false;
            int handleToStop = 0;

            lock (_stateLock)
            {
                if (_playbackState != NAudioPlaybackState.Stopped)
                {
                    _playbackState = NAudioPlaybackState.Stopped;
                    handleToStop = _streamHandle;
                    shouldFireStopped = true;
                }
            }

            if (handleToStop != 0)
            {
                try
                {
                    Bass.ChannelStop(handleToStop);
                }
                catch { }
            }

            if (shouldFireStopped)
            {
                PlaybackStopped?.Invoke(this, new StoppedEventArgs());
            }
        }

        private void OnStreamEnded()
        {
            bool shouldFire = false;
            lock (_stateLock)
            {
                if (_playbackState == NAudioPlaybackState.Playing)
                {
                    _playbackState = NAudioPlaybackState.Stopped;
                    shouldFire = true;
                }
            }

            if (shouldFire)
            {
                PlaybackStopped?.Invoke(this, new StoppedEventArgs());
            }
        }

        private void FreeStream()
        {
            int handle = Interlocked.Exchange(ref _streamHandle, 0);
            if (handle != 0)
            {
                try
                {
                    Bass.ChannelStop(handle);
                    Bass.StreamFree(handle);
                }
                catch { }
            }
            _streamProcedure = null;
            _endSyncProcedure = null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            int handleToFree = 0;
            lock (_stateLock)
            {
                if (_disposed) return;
                _disposed = true;
                _playbackState = NAudioPlaybackState.Stopped;
                handleToFree = Interlocked.Exchange(ref _streamHandle, 0);
            }

            if (handleToFree != 0)
            {
                try
                {
                    Bass.ChannelStop(handleToFree);
                    Bass.StreamFree(handleToFree);
                }
                catch { }
            }

            lock (_readLock)
            {
                _waveProvider = null;
                _buffer = null;
            }

            _streamProcedure = null;
            _endSyncProcedure = null;
        }
    }
}
