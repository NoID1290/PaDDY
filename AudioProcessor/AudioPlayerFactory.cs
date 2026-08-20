using System;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Factory for creating audio output player instances based on the active <see cref="AudioEngineType"/>.
    /// </summary>
    public static class AudioPlayerFactory
    {
        /// <summary>
        /// Creates an <see cref="IWavePlayer"/> instance using the requested engine and device parameters.
        /// </summary>
        /// <param name="engineType">The desired audio engine (NAudio WASAPI or ManagedBass).</param>
        /// <param name="deviceIndex">The device index (-1 for default, 0..N-1 for specific device).</param>
        /// <param name="latencyMs">Desired latency in milliseconds (default 100ms).</param>
        /// <param name="targetDeviceFriendlyName">Optional device friendly name for device matching.</param>
        /// <returns>An initialized <see cref="IWavePlayer"/> instance.</returns>
        public static IWavePlayer CreatePlayer(
            AudioEngineType engineType,
            int deviceIndex,
            int latencyMs = 100,
            string? targetDeviceFriendlyName = null)
        {
            if (engineType == AudioEngineType.ManagedBass && BassEngineManager.IsAvailable)
            {
                try
                {
                    int bassDevice = BassEngineManager.ResolveBassDeviceIndex(deviceIndex, targetDeviceFriendlyName);
                    System.Diagnostics.Debug.WriteLine($"[AudioEngine] Active Backend: ManagedBass (BASS Engine) | BASS Device: {bassDevice} (Requested: {deviceIndex}, Name: '{targetDeviceFriendlyName ?? "Default"}') | Latency: {latencyMs}ms");
                    return new BassWavePlayer(bassDevice, latencyMs);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AudioEngine] BASS player creation failed: {ex.Message}. Falling back to NAudio WASAPI.");
                }
            }

            // NAudio WASAPI backend
            System.Diagnostics.Debug.WriteLine($"[AudioEngine] Active Backend: NAudio (WASAPI Shared) | Device Index: {deviceIndex} | Latency: {latencyMs}ms");
            return CreateWasapiPlayer(deviceIndex, latencyMs);
        }

        /// <summary>
        /// Creates an NAudio WASAPI output player.
        /// </summary>
        /// <param name="deviceIndex">The device index (-1 for default, 0..N-1 for specific device).</param>
        /// <param name="latencyMs">Desired latency in milliseconds.</param>
        /// <returns>A new <see cref="WasapiOut"/> instance.</returns>
        public static IWavePlayer CreateWasapiPlayer(int deviceIndex, int latencyMs)
        {
            MMDevice? device = ResolveRenderDevice(deviceIndex);
            return device != null
                ? new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs)
                : new WasapiOut(AudioClientShareMode.Shared, true, latencyMs);
        }

        /// <summary>
        /// Resolves an active MMDevice render endpoint by 0-based index.
        /// </summary>
        public static MMDevice? ResolveRenderDevice(int deviceIndex)
        {
            if (deviceIndex < 0)
                return null;

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                return deviceIndex < devices.Count ? devices[deviceIndex] : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
