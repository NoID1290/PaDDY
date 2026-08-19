using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using PaDDY.Helpers;

namespace PaDDY.Services
{
    /// <summary>
    /// Manages detection, automated installation, uninstallation, routing preset discovery,
    /// and status inspection for the Microsoft WHQL-certified VB-Audio Virtual Cable (VB-CABLE).
    /// </summary>
    public static class VirtualAudioDriverService
    {
        public const string DefaultSpeakerKeyword = "CABLE Input";
        public const string DefaultMicKeyword = "CABLE Output";
        public const string VbCableBrandKeyword = "VB-Audio Virtual Cable";

        private static readonly string[] SpeakerKeywords = { "CABLE Input", "VB-Audio Virtual Cable", "Virtual Audio Driver" };
        private static readonly string[] MicKeywords = { "CABLE Output", "VB-Audio Virtual Cable", "Virtual Mic Driver" };

        #region Native SetupAPI / P/Invoke for cleanup & fallback

        private const uint DIF_REMOVE = 0x00000005;
        private const uint SPDRP_HARDWAREID = 0x00000001;
        private const uint DIGCF_ALLCLASSES = 0x00000004;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            IntPtr ClassGuid,
            string? Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(
            IntPtr DeviceInfoSet,
            uint MemberIndex,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            out uint PropertyRegDataType,
            byte[]? PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiCallClassInstaller(
            uint InstallFunction,
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        #endregion

        /// <summary>
        /// Checks whether current process is running with Administrator privileges.
        /// </summary>
        public static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets whether the Virtual Audio Cable (playback endpoint e.g. "CABLE Input") is active.
        /// </summary>
        public static bool IsSpeakerInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                return devices.Any(d => SpeakerKeywords.Any(k => d.FriendlyName.Contains(k, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets whether the Virtual Audio Cable (capture endpoint e.g. "CABLE Output") is active.
        /// </summary>
        public static bool IsMicInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                return devices.Any(d => MicKeywords.Any(k => d.FriendlyName.Contains(k, StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if either the speaker or microphone virtual endpoints are active.
        /// </summary>
        public static bool IsInstalled() => IsSpeakerInstalled() || IsMicInstalled();

        /// <summary>
        /// Returns true if both the speaker and microphone virtual endpoints are active.
        /// </summary>
        public static bool IsFullyOperational() => IsSpeakerInstalled() && IsMicInstalled();

        /// <summary>
        /// Inspects PnP status for VB-Audio Virtual Cable or other virtual devices via WMI.
        /// Returns 0 if working normally, 52 if signature enforcement is blocking (e.g. legacy unsigned driver),
        /// or -1 if no device found.
        /// </summary>
        public static int GetDriverProblemErrorCode()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Name LIKE '%VB-Audio%' OR Name LIKE '%CABLE%' OR DeviceID LIKE '%VBCABLE%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["ConfigManagerErrorCode"] is uint uCode && uCode != 0)
                        return (int)uCode;
                    if (obj["ConfigManagerErrorCode"] is int iCode && iCode != 0)
                        return iCode;
                }

                // If any matching device exists with code 0, return 0
                foreach (ManagementObject obj in searcher.Get())
                {
                    return 0;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Resolves the 1-based index (where 0 = Default, 1..N = Render devices) for the Virtual Speaker (CABLE Input).
        /// Returns -1 if not found.
        /// </summary>
        public static int FindVirtualSpeakerIndex()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                
                // Prioritize exact CABLE Input match first
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].FriendlyName.Contains(DefaultSpeakerKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return i + 1; // 1-based index (0 = default)
                    }
                }

                // Fall back to any matching speaker keyword
                for (int i = 0; i < devices.Count; i++)
                {
                    if (SpeakerKeywords.Any(k => devices[i].FriendlyName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        return i + 1;
                    }
                }
            }
            catch { }
            return -1;
        }

        public const string VbCablePackageUrl = "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip";

        /// <summary>
        /// Local directory in AppData where the official driver pack is cached.
        /// </summary>
        public static string DriverCacheDir => Path.Combine(AppDataPaths.AppDataRoot, "drivers", "VBCable");

        /// <summary>
        /// Ensures the official VB-CABLE driver package is downloaded from vb-audio.com and extracted to AppData.
        /// Returns the path to VBCABLE_Setup_x64.exe (or VBCABLE_Setup.exe for 32-bit).
        /// </summary>
        public static async Task<string?> EnsureDriverDownloadedAsync(IProgress<string>? progress = null)
        {
            string is64 = Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";
            string targetExe = Path.Combine(DriverCacheDir, is64);

            if (File.Exists(targetExe))
                return targetExe;

            Directory.CreateDirectory(DriverCacheDir);
            string tempZip = Path.Combine(DriverCacheDir, "VBCABLE_Driver_Pack.zip");

            try
            {
                progress?.Report("Connecting to VB-Audio server...");
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                httpClient.DefaultRequestHeaders.Add("User-Agent", "PaDDY-Downloader/1.0 (Windows NT 10.0; Win64; x64)");

                progress?.Report("Downloading official driver pack (~1.3 MB)...");
                using (var response = await httpClient.GetAsync(VbCablePackageUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var sourceStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
                    await sourceStream.CopyToAsync(fileStream);
                }

                progress?.Report("Extracting driver archive...");
                ZipFile.ExtractToDirectory(tempZip, DriverCacheDir, overwriteFiles: true);

                if (File.Exists(targetExe))
                    return targetExe;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VirtualAudioDriverService] Download error: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip))
                        File.Delete(tempZip);
                }
                catch { }
            }

            return File.Exists(targetExe) ? targetExe : null;
        }

        /// <summary>
        /// Resolves the absolute path to the cached or local VB-CABLE setup executable (VBCABLE_Setup_x64.exe or VBCABLE_Setup.exe).
        /// </summary>
        public static string? GetInstallerExePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string is64 = Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";

            string[] candidatePaths =
            {
                Path.Combine(DriverCacheDir, is64),
                Path.Combine(DriverCacheDir, "VBCABLE_Setup_x64.exe"),
                Path.Combine(DriverCacheDir, "VBCABLE_Setup.exe"),
                Path.Combine(baseDir, "drivers", "VBCable", is64),
                Path.Combine(baseDir, "drivers", "VBCable", "VBCABLE_Setup_x64.exe"),
                Path.Combine(baseDir, "drivers", "VBCable", "VBCABLE_Setup.exe")
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return null;
        }

        /// <summary>
        /// Resolves the absolute path to the VB-CABLE INF file.
        /// </summary>
        public static string? GetDriverInfPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string infName = "vbMmeCable64_win10.inf";

            string[] candidatePaths =
            {
                Path.Combine(DriverCacheDir, infName),
                Path.Combine(baseDir, "drivers", "VBCable", infName)
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return null;
        }

        /// <summary>
        /// Removes legacy unsigned MikeTheTech driver device nodes if present on the machine.
        /// </summary>
        public static void RemoveLegacyMikeTheTechDevice()
        {
            try
            {
                IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
                if (devInfoSet == (IntPtr)(-1) || devInfoSet == IntPtr.Zero)
                    return;

                try
                {
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
                    uint memberIndex = 0;

                    while (SetupDiEnumDeviceInfo(devInfoSet, memberIndex, ref devInfoData))
                    {
                        byte[] buffer = new byte[1024];
                        if (SetupDiGetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID, out _, buffer, (uint)buffer.Length, out uint reqSize))
                        {
                            string hwid = Encoding.Unicode.GetString(buffer, 0, (int)reqSize);
                            if (hwid.Contains("VirtualAudioDriver", StringComparison.OrdinalIgnoreCase))
                            {
                                if (SetupDiCallClassInstaller(DIF_REMOVE, devInfoSet, ref devInfoData))
                                {
                                    continue;
                                }
                            }
                        }
                        memberIndex++;
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfoSet);
                }
            }
            catch { }
        }

        /// <summary>
        /// Installs the Microsoft WHQL-certified VB-Audio Virtual Cable.
        /// Downloads the official package on-demand if not already cached.
        /// </summary>
        public static async Task<(bool Success, string Message)> InstallDriverAsync(IProgress<string>? progress = null)
        {
            // Clean up any legacy unsigned driver first
            RemoveLegacyMikeTheTechDevice();

            string? setupExe = GetInstallerExePath();
            if (string.IsNullOrEmpty(setupExe) || !File.Exists(setupExe))
            {
                try
                {
                    setupExe = await EnsureDriverDownloadedAsync(progress);
                }
                catch (Exception ex)
                {
                    return (false, $"Failed to download VB-CABLE driver pack from official website: {ex.Message}");
                }
            }

            if (string.IsNullOrEmpty(setupExe) || !File.Exists(setupExe))
            {
                return (false, "VB-CABLE installer could not be retrieved from official server.");
            }

            return await Task.Run(() =>
            {
                try
                {
                    progress?.Report("Installing driver package...");
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = setupExe,
                        Arguments = "-i -h", // Silent install flag
                        UseShellExecute = true,
                        Verb = IsAdministrator() ? "" : "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                        return (false, "Failed to launch VB-CABLE installer process.");

                    process.WaitForExit(60000);

                    // Give Windows audio endpoint builder a moment to enumerate the new endpoints
                    System.Threading.Thread.Sleep(1500);

                    if (IsFullyOperational() || IsSpeakerInstalled())
                    {
                        return (true, "VB-Audio Virtual Cable (WHQL Signed) installed successfully!");
                    }

                    // If silent flag finished, check status
                    if (process.ExitCode == 0)
                    {
                        return (true, "VB-Audio Virtual Cable was registered in Windows.");
                    }

                    return (false, $"VB-CABLE installer completed with code: {process.ExitCode}. A system restart may be required.");
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return (false, "Driver installation was cancelled by the user (UAC elevation declined).");
                }
                catch (Exception ex)
                {
                    return (false, $"Driver installation error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Uninstalls the VB-Audio Virtual Cable. If not elevated, requests UAC elevation.
        /// </summary>
        public static async Task<(bool Success, string Message)> UninstallDriverAsync()
        {
            return await Task.Run(() =>
            {
                string? setupExe = GetInstallerExePath();
                if (!string.IsNullOrEmpty(setupExe) && File.Exists(setupExe))
                {
                    try
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = setupExe,
                            Arguments = "-u -h", // Silent uninstall flag
                            UseShellExecute = true,
                            Verb = IsAdministrator() ? "" : "runas",
                            WindowStyle = ProcessWindowStyle.Hidden
                        };

                        using var process = Process.Start(startInfo);
                        if (process == null)
                            return (false, "Failed to launch VB-CABLE uninstaller process.");

                        process.WaitForExit(45000);
                        return (true, "VB-Audio Virtual Cable removal completed.");
                    }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                    {
                        return (false, "Driver uninstallation was cancelled by the user (UAC elevation declined).");
                    }
                    catch (Exception ex)
                    {
                        return (false, $"Driver uninstallation error: {ex.Message}");
                    }
                }

                // Fallback: Driver store cleanup via pnputil
                try
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -Command \"Get-WindowsDriver -Online -All | Where-Object { $_.OriginalFileName -like '*vbMmeCable*' -or $_.OriginalFileName -like '*VirtualAudioDriver*' } | ForEach-Object { pnputil /delete-driver $_.Driver /uninstall /force }\"",
                        UseShellExecute = true,
                        Verb = IsAdministrator() ? "" : "runas",
                        CreateNoWindow = true
                    });
                    process?.WaitForExit(30000);
                    return (true, "Virtual audio driver packages removed from driver store.");
                }
                catch (Exception ex)
                {
                    return (false, $"Error removing driver: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Enables Windows Test-Signing mode via elevated bcdedit.exe (retained as diagnostic utility).
        /// </summary>
        public static async Task<(bool Success, string Message)> EnableTestSigningAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "bcdedit.exe",
                        Arguments = "/set testsigning on",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Normal
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                        return (false, "Failed to launch elevated bcdedit.exe process.");

                    process.WaitForExit(15000);

                    if (process.ExitCode == 0)
                    {
                        return (true, "Windows Test-Signing mode has been enabled in BCD.\nPlease RESTART your computer.");
                    }

                    return (false, $"bcdedit exited with code {process.ExitCode}.");
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return (false, "Operation cancelled by user (UAC elevation declined).");
                }
                catch (Exception ex)
                {
                    return (false, $"Error executing bcdedit: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Opens the Windows 10/11 Modern Sound Settings.
        /// </summary>
        public static void OpenSoundSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>
        /// Opens the legacy Windows Sound Control Panel applet (mmsys.cpl).
        /// </summary>
        public static void OpenSoundControlPanel()
        {
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl sounds") { UseShellExecute = true });
            }
            catch { }
        }

        /// <summary>
        /// Opens Windows Device Manager.
        /// </summary>
        public static void OpenDeviceManager()
        {
            try
            {
                Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
            }
            catch { }
        }
    }
}
