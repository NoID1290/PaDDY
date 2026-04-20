using System;
using System.Runtime.InteropServices;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PaDDY.Services
{
    /// <summary>
    /// Captures audio from a single process using Windows 10 2004+ process loopback.
    /// Implements <see cref="IWaveIn"/> so it can be used as a drop-in replacement
    /// for <see cref="WasapiLoopbackCapture"/> in <see cref="AudioCaptureService"/>.
    /// Uses raw COM vtable calls to avoid QueryInterface issues with the virtual audio device.
    /// </summary>
    public sealed class ProcessLoopbackCapture : IWaveIn
    {
        private static readonly Guid IID_IAudioCaptureClient =
            new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");

        private const int AUDCLNT_BUFFERFLAGS_SILENT = 0x2;

        private readonly uint _processId;
        private readonly bool _includeTree;
        private AudioClientHandle? _audioClient;
        private CaptureClientHandle? _captureClient;
        private WaveFormat? _captureFormat;  // raw multi-channel format from device
        private Thread? _captureThread;
        private volatile bool _isCapturing;
        private SynchronizationContext? _syncContext;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public WaveFormat WaveFormat
        {
            get => _captureFormat ?? new WaveFormat(48000, 32, 2);
            set { /* format is dictated by the audio engine; ignore external set */ }
        }

        public ProcessLoopbackCapture(uint processId, bool includeProcessTree = true)
        {
            _processId = processId;
            _includeTree = includeProcessTree;
        }

        public void StartRecording()
        {
            if (_isCapturing)
                throw new InvalidOperationException("Already capturing.");

            _syncContext = SynchronizationContext.Current;
            string step = "activating audio client";

            try
            {
                // Activate the IAudioClient — returns a raw vtable wrapper (no QI)
                step = $"activating audio client for PID {_processId}";
                var task = ProcessLoopbackInterop.ActivateProcessLoopbackAsync(_processId, _includeTree);
                task.Wait();
                _audioClient = task.Result;

                // The process-loopback virtual device does NOT implement GetMixFormat.
                // Query the default render endpoint's mix format — that's the native
                // format the audio engine delivers for loopback streams.
                step = "querying default render device mix format";
                using (var enumerator = new MMDeviceEnumerator())
                using (var renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console))
                {
                    _captureFormat = renderDevice.AudioClient.MixFormat;
                }

                // Expose native multi-channel format directly to consumers.
                // Downstream recorders handle channel conversion as needed.

                // The virtual loopback device is a render-type endpoint internally,
                // so AUDCLNT_STREAMFLAGS_LOOPBACK is required.
                const int AUDCLNT_SHAREMODE_SHARED = 0;
                const int AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
                const long REFTIMES_PER_SEC = 10_000_000;

                step = $"calling Initialize (shared, loopback, format={_captureFormat})";
                IntPtr formatPtr = WaveFormat.MarshalToPtr(_captureFormat);
                try
                {
                    _audioClient.Initialize(AUDCLNT_SHAREMODE_SHARED,
                        AUDCLNT_STREAMFLAGS_LOOPBACK, REFTIMES_PER_SEC, formatPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(formatPtr);
                }

                // Obtain capture client via GetService (raw pointer, no QI)
                step = "calling GetService for IAudioCaptureClient";
                IntPtr capturePtr = _audioClient.GetService(IID_IAudioCaptureClient);
                _captureClient = new CaptureClientHandle(capturePtr);

                _isCapturing = true;
                _captureThread = new Thread(CaptureLoop)
                {
                    IsBackground = true,
                    Name = "ProcessLoopbackCapture",
                    Priority = ThreadPriority.AboveNormal
                };
                _captureThread.Start();

                step = "calling Start";
                _audioClient.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Process loopback failed at step [{step}]: {ex.GetType().Name}: {ex.Message}", ex);
            }
        }

        private void CaptureLoop()
        {
            Exception? error = null;
            try
            {
                while (_isCapturing)
                {
                    Thread.Sleep(10); // ~100Hz poll

                    if (_captureClient == null) break;

                    Marshal.ThrowExceptionForHR(
                        _captureClient.GetNextPacketSize(out int packetSize));

                    while (packetSize > 0)
                    {
                        Marshal.ThrowExceptionForHR(
                            _captureClient.GetBuffer(out IntPtr dataPtr,
                                out int framesAvailable, out int flags,
                                out long _, out long _));

                        int bytesAvailable = framesAvailable * _captureFormat!.BlockAlign;
                        var buffer = new byte[bytesAvailable];

                        if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0)
                            Array.Clear(buffer, 0, bytesAvailable);
                        else
                            Marshal.Copy(dataPtr, buffer, 0, bytesAvailable);

                        Marshal.ThrowExceptionForHR(
                            _captureClient.ReleaseBuffer(framesAvailable));

                        // Pass native multi-channel data directly to consumers
                        DataAvailable?.Invoke(this, new WaveInEventArgs(buffer, bytesAvailable));

                        Marshal.ThrowExceptionForHR(
                            _captureClient.GetNextPacketSize(out packetSize));
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                RaiseRecordingStopped(error);
            }
        }

        public void StopRecording()
        {
            _isCapturing = false;
            _captureThread?.Join(2000);
            _captureThread = null;

            try { _audioClient?.Stop(); } catch { }
            try { _audioClient?.Reset(); } catch { }
        }

        public void Dispose()
        {
            StopRecording();
            _captureClient?.Dispose();
            _captureClient = null;
            _audioClient?.Dispose();
            _audioClient = null;
        }

        private void RaiseRecordingStopped(Exception? e)
        {
            var handler = RecordingStopped;
            if (handler == null) return;

            if (_syncContext != null)
                _syncContext.Post(_ => handler(this, new StoppedEventArgs(e)), null);
            else
                handler(this, new StoppedEventArgs(e));
        }
    }
}
