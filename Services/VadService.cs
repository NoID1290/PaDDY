using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace PaDDY.Services
{
    [SupportedOSPlatform("windows")]
    internal static class VadService
    {
        private const string DriverFriendlyNameFragment = "Virtual Audio Driver";

        public static string GetVadDirectory() =>
            Path.Combine(AppContext.BaseDirectory, "vad");

        public static bool AreDriverFilesPresent()
        {
            string vadDir = GetVadDirectory();
            return File.Exists(Path.Combine(vadDir, "VirtualAudioDriver.inf"))
                && File.Exists(Path.Combine(vadDir, "install.ps1"));
        }

        public static bool IsDriverInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
                {
                    var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.All);
                    foreach (var device in devices)
                    {
                        try
                        {
                            if (device.FriendlyName.Contains(DriverFriendlyNameFragment,
                                    StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                        finally { device.Dispose(); }
                    }
                }
            }
            catch { /* Ignore COM errors */ }
            return false;
        }

        public static async Task<bool> InstallDriverAsync()
        {
            string vadDir = GetVadDirectory();
            string scriptPath = Path.Combine(vadDir, "install.ps1");
            string infPath    = Path.Combine(vadDir, "VirtualAudioDriver.inf");

            if (!File.Exists(scriptPath) || !File.Exists(infPath)) return false;

            // Run the install script elevated. It handles both pnputil (driver
            // store) AND device node creation via SetupAPI — the two steps that
            // "Add Legacy Hardware" in Device Manager performs manually.
            var psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-ExecutionPolicy Bypass -NonInteractive -WindowStyle Hidden" +
                                  $" -File \"{scriptPath}\" -InfPath \"{infPath}\"",
                Verb            = "runas",
                UseShellExecute = true,
            };

            try
            {
                var process = Process.Start(psi);
                if (process == null) return false;
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
