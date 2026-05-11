using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace PaDDY.Services
{
    /// <summary>
    /// Enumerates processes currently producing audio on the default render endpoint.
    /// </summary>
    public static class AudioSessionHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>
        /// Returns a list of (ProcessId, ProcessName) for all processes that have
        /// an active audio session on any active render device.
        /// </summary>
        public static List<(uint ProcessId, string ProcessName)> GetAudioProcesses()
        {
            var result = new Dictionary<uint, string>();

            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            foreach (var device in devices)
            {
                try
                {
                    var sessionManager = device.AudioSessionManager;
                    if (sessionManager?.Sessions == null) continue;

                    for (int i = 0; i < sessionManager.Sessions.Count; i++)
                    {
                        var session = sessionManager.Sessions[i];
                        uint pid = (uint)session.GetProcessID;
                        if (pid == 0) continue; // system session
                        if (result.ContainsKey(pid)) continue;

                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            string name = !string.IsNullOrEmpty(proc.MainWindowTitle)
                                ? $"{proc.ProcessName} — {proc.MainWindowTitle}"
                                : proc.ProcessName;
                            result[pid] = name;
                        }
                        catch
                        {
                            // Process may have exited
                        }
                    }
                }
                catch
                {
                    // Some devices may not support session enumeration
                }
            }

            return result
                .Select(kv => (kv.Key, kv.Value))
                .OrderBy(x => x.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Attempts to resolve a label for the app currently owning the foreground window.
        /// Prefers window title, then falls back to process name.
        /// </summary>
        public static bool TryGetFocusedApplicationLabel(out string label)
        {
            label = string.Empty;
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return false;

                _ = GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0)
                    return false;

                using var process = Process.GetProcessById((int)pid);
                string title = process.MainWindowTitle?.Trim() ?? string.Empty;
                string processName = process.ProcessName?.Trim() ?? string.Empty;

                label = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : processName;

                return !string.IsNullOrWhiteSpace(label);
            }
            catch
            {
                return false;
            }
        }
    }
}
