using NAudio.CoreAudioApi;
using NAudio.Wave;
using NoIDSoftwork.AudioProcessor;

namespace PaDDY.Services
{
    public static class AudioOutputDeviceResolver
    {
        /// <summary>
        /// Current global audio engine backend. Defaults to NAudio unless configured otherwise.
        /// </summary>
        public static AudioEngineType ActiveAudioEngine { get; set; } = AudioEngineType.NAudio;

        /// <summary>
        /// Creates an output audio player using the specified (or currently active) audio engine backend.
        /// </summary>
        /// <param name="deviceIndex">Device index (-1 for default, 0..N-1 for specific device).</param>
        /// <param name="latencyMs">Desired latency in milliseconds.</param>
        /// <param name="engine">Optional explicit audio engine override.</param>
        /// <returns>An initialized <see cref="IWavePlayer"/> instance.</returns>
        public static IWavePlayer CreatePlayer(int deviceIndex, int latencyMs = 100, AudioEngineType? engine = null)
        {
            var targetEngine = engine ?? ActiveAudioEngine;
            var device = ResolveRenderDevice(deviceIndex);
            string? friendlyName = device?.FriendlyName;

            return AudioPlayerFactory.CreatePlayer(targetEngine, deviceIndex, latencyMs, friendlyName);
        }

        /// <summary>
        /// Explicitly creates an NAudio WASAPI output player.
        /// </summary>
        public static IWavePlayer CreateWasapiPlayer(int deviceIndex, int latencyMs)
        {
            return CreatePlayer(deviceIndex, latencyMs, AudioEngineType.NAudio);
        }

        /// <summary>
        /// Resolves an active MMDevice render endpoint by 0-based index.
        /// </summary>
        public static MMDevice? ResolveRenderDevice(int deviceIndex)
        {
            return AudioPlayerFactory.ResolveRenderDevice(deviceIndex);
        }
    }
}
