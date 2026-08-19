using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace PaDDY.Services
{
    [SupportedOSPlatform("windows")]
    internal static class VadService
    {
        public static string GetVadDirectory() =>
            Path.Combine(AppContext.BaseDirectory, "drivers", "VBCable");

        public static bool AreDriverFilesPresent() =>
            !string.IsNullOrEmpty(VirtualAudioDriverService.GetInstallerExePath()) ||
            !string.IsNullOrEmpty(VirtualAudioDriverService.GetDriverInfPath());

        public static bool IsDriverInstalled() =>
            VirtualAudioDriverService.IsInstalled();

        public static async Task<bool> InstallDriverAsync()
        {
            var (success, _) = await VirtualAudioDriverService.InstallDriverAsync();
            return success;
        }
    }
}
