using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace PaDDY.Services
{
    [SupportedOSPlatform("windows")]
    internal static class VadService
    {
        private const string DriverFriendlyNameFragment = "Virtual Audio Driver";
        private const string MicFriendlyNameFragment = "Virtual Mic Driver";
        private const string VadHardwareIdUpper = "ROOT\\VIRTUALAUDIODRIVER";
        private static readonly Encoding LogEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        public static string GetVadDirectory() =>
            Path.Combine(AppContext.BaseDirectory, "vad");

        public static bool AreDriverFilesPresent()
        {
            string vadDir = GetVadDirectory();
            return File.Exists(Path.Combine(vadDir, "VirtualAudioDriver.inf"))
                && File.Exists(Path.Combine(vadDir, "VirtualAudioDriver.sys"))
                && File.Exists(Path.Combine(vadDir, "virtualaudiodriver.cat"));
        }

        public static string GetInstallLogPath() =>
            Path.Combine(Path.GetTempPath(), "PaDDY-VadInstall.log");

        public static string GetInstallLogTail(int maxLines = 16)
        {
            try
            {
                string path = GetInstallLogPath();
                if (!File.Exists(path)) return string.Empty;

                var lines = File.ReadAllLines(path);
                if (lines.Length <= maxLines) return string.Join(Environment.NewLine, lines);

                return string.Join(Environment.NewLine,
                    lines[^maxLines..]);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsDriverInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                bool hasSpeaker = ContainsEndpoint(enumerator, DataFlow.Render, DriverFriendlyNameFragment);
                bool hasMic = ContainsEndpoint(enumerator, DataFlow.Capture, MicFriendlyNameFragment);
                return hasSpeaker && hasMic;
            }
            catch { /* Ignore COM errors */ }
            return false;
        }

        public static async Task<bool> InstallDriverAsync()
        {
            if (!AreDriverFilesPresent()) return false;

            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--vad-install --vad-quiet",
                Verb = "runas",
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

        public static int InstallDriverNative(bool quiet)
        {
            ResetLog(GetInstallLogPath(), "=== VadInstall start ===");

            string infPath = Path.Combine(GetVadDirectory(), "VirtualAudioDriver.inf");
            if (!File.Exists(infPath))
            {
                LogInstall("ERROR: INF not found: " + infPath);
                return 1;
            }

            LogInstall("INF path: " + infPath);

            var addDriver = RunProcessCapture("pnputil.exe", $"/add-driver \"{infPath}\" /install");
            LogInstall($"pnputil /add-driver exit={addDriver.ExitCode} {addDriver.Output}");
            if (addDriver.ExitCode != 0)
                return addDriver.ExitCode;

            foreach (string instanceId in EnumerateVadInstanceIds())
            {
                var removeNode = RunProcessCapture("pnputil.exe", $"/remove-device \"{instanceId}\"");
                LogInstall($"pnputil /remove-device {instanceId} exit={removeNode.ExitCode} {removeNode.Output}");
            }

            int rc = CreateDeviceNode("ROOT\\VirtualAudioDriver");
            LogInstall($"CreateDeviceNode returned {rc}");
            if (rc != 0) return rc;

            var scan = RunProcessCapture("pnputil.exe", "/scan-devices");
            LogInstall($"pnputil /scan-devices exit={scan.ExitCode} {scan.Output}");
            if (scan.ExitCode != 0)
                return scan.ExitCode;

            // Force-bind the INF to newly created matching ROOT\VirtualAudioDriver nodes.
            var bind = RunProcessCapture("pnputil.exe", $"/add-driver \"{infPath}\" /install");
            LogInstall($"pnputil /add-driver (post-node) exit={bind.ExitCode} {bind.Output}");
            if (bind.ExitCode != 0)
                return bind.ExitCode;

            int updateRc = ForceUpdateDriverWithRetry("ROOT\\VirtualAudioDriver", infPath, retries: 20, delayMs: 1000);
            LogInstall($"UpdateDriverForPlugAndPlayDevices returned {updateRc}");
            const int ErrorNoSuchDevInst = unchecked((int)0xE000020B);
            if (updateRc != 0 && updateRc != ErrorNoSuchDevInst)
                return updateRc;

            if (updateRc == ErrorNoSuchDevInst)
            {
                LogInstall("WARNING: UpdateDriverForPlugAndPlayDevices did not find a ready devnode after retries. Continuing with restart and endpoint wait.");
            }

            foreach (string instanceId in EnumerateVadInstanceIds())
            {
                var restart = RunProcessCapture("pnputil.exe", $"/restart-device \"{instanceId}\"");
                LogInstall($"pnputil /restart-device {instanceId} exit={restart.ExitCode} {CondenseForLog(restart.Output)}");
            }

            LogInstall("VAD device snapshot after bind attempt: " + GetVadDeviceSnapshot());

            bool ready = WaitForVadEndpoints(timeoutMs: 45000);
            LogInstall(ready
                ? "SUCCESS: Virtual speaker and microphone endpoints are present."
                : "ERROR: Virtual endpoints did not materialize in time.");
            return ready ? 0 : 3;
        }

        public static int UninstallDriverNative(bool quiet)
        {
            string uninstallLogPath = Path.Combine(Path.GetTempPath(), "PaDDY-VadUninstall.log");
            ResetLog(uninstallLogPath, "=== VadUninstall start ===");

            bool hadErrors = false;

            foreach (string instanceId in EnumerateVadInstanceIds())
            {
                var removeNode = RunProcessCapture("pnputil.exe", $"/remove-device \"{instanceId}\"");
                LogUninstall($"pnputil /remove-device {instanceId} exit={removeNode.ExitCode} {removeNode.Output}");
                if (removeNode.ExitCode != 0) hadErrors = true;
            }

            foreach (string publishedName in EnumerateVadPublishedNames())
            {
                var removeDriver = RunProcessCapture("pnputil.exe", $"/delete-driver {publishedName} /uninstall /force");
                LogUninstall($"pnputil /delete-driver {publishedName} exit={removeDriver.ExitCode} {removeDriver.Output}");
                if (removeDriver.ExitCode != 0) hadErrors = true;
            }

            var scan = RunProcessCapture("pnputil.exe", "/scan-devices");
            LogUninstall($"pnputil /scan-devices exit={scan.ExitCode} {scan.Output}");
            if (scan.ExitCode != 0) hadErrors = true;

            LogUninstall(hadErrors ? "Completed with warnings." : "SUCCESS.");
            return hadErrors ? 1 : 0;
        }

        private static bool ContainsEndpoint(MMDeviceEnumerator enumerator, DataFlow flow, string nameFragment)
        {
            var devices = enumerator.EnumerateAudioEndPoints(flow, DeviceState.All);
            foreach (var device in devices)
            {
                try
                {
                    if (device.FriendlyName.Contains(nameFragment, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                finally { device.Dispose(); }
            }

            return false;
        }

        private static bool WaitForVadEndpoints(int timeoutMs)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (Environment.TickCount64 < deadline)
            {
                if (IsDriverInstalled()) return true;
                Thread.Sleep(2000);
            }
            return false;
        }

        private static IEnumerable<string> EnumerateVadInstanceIds()
        {
            var result = RunProcessCapture("pnputil.exe", "/enum-devices");
            if (result.ExitCode != 0) yield break;

            foreach (string rawLine in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                const string prefix = "Instance ID:";
                if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                string instanceId = line[prefix.Length..].Trim();
                if (instanceId.StartsWith(VadHardwareIdUpper, StringComparison.OrdinalIgnoreCase))
                    yield return instanceId;
            }
        }

        private static string GetVadDeviceSnapshot()
        {
            var result = RunProcessCapture("pnputil.exe", "/enum-devices");
            if (result.ExitCode != 0) return "Unable to enumerate devices.";

            var lines = result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var sb = new StringBuilder();
            bool inBlock = false;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();
                var trimmed = line.Trim();

                if (trimmed.StartsWith("Instance ID:", StringComparison.OrdinalIgnoreCase))
                {
                    inBlock = trimmed.Contains(VadHardwareIdUpper, StringComparison.OrdinalIgnoreCase);
                    if (inBlock)
                    {
                        if (sb.Length > 0) sb.Append(" | ");
                        sb.Append(trimmed);
                    }
                    continue;
                }

                if (inBlock)
                {
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        inBlock = false;
                    }
                    else if (trimmed.StartsWith("Status:", StringComparison.OrdinalIgnoreCase)
                          || trimmed.StartsWith("Problem Code:", StringComparison.OrdinalIgnoreCase)
                          || trimmed.StartsWith("Problem Status:", StringComparison.OrdinalIgnoreCase)
                          || trimmed.StartsWith("Driver Name:", StringComparison.OrdinalIgnoreCase)
                          || trimmed.StartsWith("Device Description:", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append("; ").Append(trimmed);
                    }
                }
            }

            return sb.Length == 0 ? "No ROOT\\VIRTUALAUDIODRIVER devices found." : sb.ToString();
        }

        private static IEnumerable<string> EnumerateVadPublishedNames()
        {
            var result = RunProcessCapture("pnputil.exe", "/enum-drivers");
            if (result.ExitCode != 0) yield break;

            string? publishedName = null;
            string? originalName = null;

            foreach (string rawLine in result.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("Published Name:", StringComparison.OrdinalIgnoreCase))
                {
                    publishedName = line["Published Name:".Length..].Trim();
                    originalName = null;
                    continue;
                }

                if (line.StartsWith("Original Name:", StringComparison.OrdinalIgnoreCase))
                {
                    originalName = line["Original Name:".Length..].Trim();
                    if (!string.IsNullOrWhiteSpace(publishedName) &&
                        string.Equals(originalName, "VirtualAudioDriver.inf", StringComparison.OrdinalIgnoreCase))
                    {
                        yield return publishedName;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    publishedName = null;
                    originalName = null;
                }
            }
        }

        private static (int ExitCode, string Output) RunProcessCapture(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process == null) return (-1, "Failed to start process.");

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                string merged = string.Join(Environment.NewLine, new[] { stdout, stderr }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.TrimEnd()));
                return (process.ExitCode, merged);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        private static string CondenseForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            return text.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static void LogInstall(string message) =>
            AppendLog(GetInstallLogPath(), message);

        private static void LogUninstall(string message) =>
            AppendLog(Path.Combine(Path.GetTempPath(), "PaDDY-VadUninstall.log"), message);

        private static void AppendLog(string path, string message)
        {
            try
            {
                string ts = DateTime.Now.ToString("HH:mm:ss");
                File.AppendAllText(path, $"{ts}  {message}{Environment.NewLine}", LogEncoding);
            }
            catch
            {
                // best effort logging
            }
        }

        private static void ResetLog(string path, string header)
        {
            try
            {
                string ts = DateTime.Now.ToString("HH:mm:ss");
                File.WriteAllText(path, $"{ts}  {header}{Environment.NewLine}", LogEncoding);
            }
            catch
            {
                // best effort logging
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        private static readonly Guid GuidDevClassMedia = new("4d36e96c-e325-11ce-bfc1-08002be10318");
        private static readonly IntPtr InvalidHandle = new(-1);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiCreateDeviceInfo(
            IntPtr deviceInfoSet,
            string deviceName,
            ref Guid classGuid,
            string deviceDescription,
            IntPtr hwndParent,
            uint creationFlags,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiSetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            byte[] propertyBuffer,
            uint propertyBufferSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiCallClassInstaller(
            uint installFunction,
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool DiInstallDevice(
            IntPtr hwndParent,
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            IntPtr driverInfoData,
            uint flags,
            out bool needReboot);

        [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool UpdateDriverForPlugAndPlayDevices(
            IntPtr hwndParent,
            string hardwareId,
            string fullInfPath,
            uint installFlags,
            out bool rebootRequired);

        private static int CreateDeviceNode(string hardwareId)
        {
            const uint DicdGenerateId = 0x00000001;
            const uint SpdrpHardwareId = 0x00000001;
            const uint DifRegisterDevice = 0x00000019;

            var classGuid = GuidDevClassMedia;
            IntPtr set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
            if (set == InvalidHandle) return Marshal.GetLastWin32Error();

            try
            {
                var did = new SP_DEVINFO_DATA
                {
                    cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
                };

                if (!SetupDiCreateDeviceInfo(set, "VirtualAudioDriver", ref classGuid,
                        "Virtual Audio Driver by MTT", IntPtr.Zero, DicdGenerateId, ref did))
                {
                    return Marshal.GetLastWin32Error();
                }

                byte[] hwIdBytes = Encoding.Unicode.GetBytes(hardwareId + "\0\0");
                if (!SetupDiSetDeviceRegistryProperty(set, ref did, SpdrpHardwareId,
                        hwIdBytes, (uint)hwIdBytes.Length))
                {
                    return Marshal.GetLastWin32Error();
                }

                if (!SetupDiCallClassInstaller(DifRegisterDevice, set, ref did))
                {
                    return Marshal.GetLastWin32Error();
                }

                // Explicitly configure/install the best matching function driver
                // for this root-enumerated devnode. Without this step, some systems
                // keep the node in CM_PROB_NOT_CONFIGURED.
                if (!DiInstallDevice(IntPtr.Zero, set, ref did, IntPtr.Zero, 0, out bool needReboot))
                {
                    int win32 = Marshal.GetLastWin32Error();
                    const int ErrorNoDriverSelected = unchecked((int)0xE0000203);
                    if (win32 != ErrorNoDriverSelected)
                    {
                        return win32;
                    }

                    LogInstall("WARNING: DiInstallDevice reported no selected driver (0xE0000203). Continuing with post-node bind steps.");
                }

                if (needReboot)
                {
                    LogInstall("WARNING: Windows reported reboot required after DiInstallDevice.");
                }

                return 0;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static int ForceUpdateDriver(string hardwareId, string infPath)
        {
            const uint InstallFlagForce = 0x00000001;
            bool ok = UpdateDriverForPlugAndPlayDevices(
                IntPtr.Zero,
                hardwareId,
                infPath,
                InstallFlagForce,
                out bool rebootRequired);

            if (!ok)
            {
                int win32 = Marshal.GetLastWin32Error();
                return win32 == 0 ? 1 : win32;
            }

            if (rebootRequired)
                LogInstall("WARNING: Windows reported reboot required after driver update.");

            return 0;
        }

        private static int ForceUpdateDriverWithRetry(string hardwareId, string infPath, int retries, int delayMs)
        {
            const int ErrorNoSuchDevInst = unchecked((int)0xE000020B);
            int lastRc = 0;

            for (int attempt = 1; attempt <= retries; attempt++)
            {
                lastRc = ForceUpdateDriver(hardwareId, infPath);
                if (lastRc == 0)
                {
                    if (attempt > 1)
                        LogInstall($"UpdateDriverForPlugAndPlayDevices succeeded on retry {attempt}/{retries}.");
                    return 0;
                }

                if (lastRc == ErrorNoSuchDevInst || lastRc == 259)
                {
                    LogInstall($"UpdateDriverForPlugAndPlayDevices attempt {attempt}/{retries} returned 0x{lastRc:X8}; retrying...");
                    Thread.Sleep(delayMs);
                    continue;
                }

                return lastRc;
            }

            return lastRc;
        }
    }
}
