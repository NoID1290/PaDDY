using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PaDDY.Services
{
    public static class AudioOutputDeviceResolver
    {
        public static IWavePlayer CreateWasapiPlayer(int deviceIndex, int latencyMs)
        {
            MMDevice? device = ResolveRenderDevice(deviceIndex);
            return device != null
                ? new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs)
                : new WasapiOut(AudioClientShareMode.Shared, true, latencyMs);
        }

        public static MMDevice? ResolveRenderDevice(int deviceIndex)
        {
            if (deviceIndex < 0)
                return null;

            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            return deviceIndex < devices.Count ? devices[deviceIndex] : null;
        }
    }
}
