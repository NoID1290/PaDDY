using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;

namespace PaDDY.Services
{
    /// <summary>
    /// Enumerates processes currently producing audio on the default render endpoint.
    /// </summary>
    public static class AudioSessionHelper
    {
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
    }
}
