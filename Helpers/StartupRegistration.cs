using System;
using System.Diagnostics;
using System.IO;
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
        public static bool SetRunOnStartup(bool enabled)
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                                         ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
                if (key == null) return false;

                if (enabled)
                {
                    string exe = GetExecutablePath();
                    if (string.IsNullOrEmpty(exe))
                        return false;

                    key.SetValue(ValueName, $"\"{exe}\"");
                }
                else if (key.GetValue(ValueName) != null)
                {
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                }

                return IsRunOnStartupEnabled() == enabled;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupRegistration] Failed to update startup state: {ex}");
                return false;
            }
        }

        /// <summary>Returns true if PaDDY is currently registered to run at startup.</summary>
        public static bool IsRunOnStartupEnabled()
        {
            try
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return TryGetRegisteredExecutablePath(key?.GetValue(ValueName) as string) != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StartupRegistration] Failed to read startup state: {ex}");
                return false;
            }
        }

        private static string GetExecutablePath()
        {
            // Prefer stable app-host paths near the running app base directory.
            string baseDir = AppContext.BaseDirectory;
            foreach (string candidate in new[] { "PaDDY.exe", "NoIDSoftwork.Core.exe" })
            {
                string candidatePath = Path.Combine(baseDir, candidate);
                if (File.Exists(candidatePath))
                    return candidatePath;
            }

            // Fall back to the currently running host process path.
            string? path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path) &&
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path))
                return path;

            using var proc = Process.GetCurrentProcess();
            string? mainModulePath = proc.MainModule?.FileName;
            if (!string.IsNullOrEmpty(mainModulePath) && File.Exists(mainModulePath))
                return mainModulePath;

            return string.Empty;
        }

        private static string? TryGetRegisteredExecutablePath(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return null;

            string value = rawValue.Trim();
            string path;
            if (value.StartsWith('"'))
            {
                int secondQuote = value.IndexOf('"', 1);
                if (secondQuote <= 1)
                    return null;
                path = value.Substring(1, secondQuote - 1);
            }
            else
            {
                int firstSpace = value.IndexOf(' ');
                path = firstSpace > 0 ? value.Substring(0, firstSpace) : value;
            }

            return File.Exists(path) ? path : null;
        }
    }
}
