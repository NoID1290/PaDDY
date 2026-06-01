using System;
using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PaDDY.Helpers
{
    /// <summary>
    /// Registers (or removes) PaDDY in the per-user Windows startup list via the
    /// HKCU Run key. No elevation required; affects only the current user.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class StartupRegistration
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PaDDY";

        /// <summary>Adds or removes the HKCU Run entry to match <paramref name="enabled"/>.</summary>
        public static void SetRunOnStartup(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return;

                if (enabled)
                {
                    string exe = GetExecutablePath();
                    if (!string.IsNullOrEmpty(exe))
                        key.SetValue(ValueName, $"\"{exe}\"");
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }
            }
            catch { /* non-critical */ }
        }

        /// <summary>Returns true if PaDDY is currently registered to run at startup.</summary>
        public static bool IsRunOnStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }

        private static string GetExecutablePath()
        {
            // Prefer the real host .exe rather than the managed dll.
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) &&
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return path;

            using var proc = Process.GetCurrentProcess();
            return proc.MainModule?.FileName ?? string.Empty;
        }
    }
}
