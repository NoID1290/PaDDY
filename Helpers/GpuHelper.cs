using System;
using System.Management;
using System.Runtime.Versioning;

namespace PaDDY.Helpers
{
    /// <summary>
    /// Detects whether an NVIDIA GPU is present on the system by querying
    /// WMI. The result is cached because GPU hardware does not change at
    /// runtime.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class GpuHelper
    {
        private static bool? _isNvidiaAvailable;

        /// <summary>
        /// Returns <c>true</c> when at least one NVIDIA video adapter is
        /// reported by the OS.
        /// </summary>
        public static bool IsNvidiaGpuAvailable
        {
            get
            {
                _isNvidiaAvailable ??= DetectNvidiaGpu();
                return _isNvidiaAvailable.Value;
            }
        }

        private static bool DetectNvidiaGpu()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_VideoController");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string? name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name) &&
                        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // WMI unavailable – assume no NVIDIA GPU.
            }

            return false;
        }
    }
}
