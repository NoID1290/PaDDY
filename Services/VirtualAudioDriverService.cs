using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace PaDDY.Services
{
    /// <summary>
    /// Manages detection, SetupAPI root device creation, 1-click installation,
    /// uninstallation, and status inspection for the bundled open-source MIT Virtual Audio Driver
    /// (VirtualDrivers/Virtual-Audio-Driver by MikeTheTech).
    /// </summary>
    public static class VirtualAudioDriverService
    {
        public const string VirtualSpeakerKeyword = "Virtual Audio Driver";
        public const string VirtualMicKeyword = "Virtual Mic Driver";
        public const string HardwareId = "ROOT\\VirtualAudioDriver";
        public const string InfFileName = "VirtualAudioDriver.inf";

        #region Native SetupAPI / NewDev P/Invoke

        private const uint DICD_GENERATE_ID = 0x00000001;
        private const uint SPDRP_HARDWAREID = 0x00000001;
        private const uint DIF_REGISTERDEVICE = 0x00000019;
        private const uint DIF_REMOVE = 0x00000005;
        private const uint INSTALLFLAG_FORCE = 0x00000001;
        private const uint DIGCF_ALLCLASSES = 0x00000004;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiGetINFClass(
            string InfName,
            out Guid ClassGuid,
            StringBuilder ClassName,
            uint ClassNameSize,
            out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SetupDiCreateDeviceInfoList(
            ref Guid ClassGuid,
            IntPtr hwndParent);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiCreateDeviceInfo(
            IntPtr DeviceInfoSet,
            string DeviceName,
            ref Guid ClassGuid,
            string? DeviceDescription,
            IntPtr hwndParent,
            uint CreationFlags,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiSetDeviceRegistryProperty(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            uint Property,
            byte[] PropertyBuffer,
            uint PropertyBufferSize);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool SetupDiCallClassInstaller(
            uint InstallFunction,
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
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

        [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool UpdateDriverForPlugAndPlayDevices(
            IntPtr hwndParent,
            string HardwareId,
            string FullInfPath,
            uint InstallFlags,
            out bool bRebootRequired);

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
        /// Gets whether the Virtual Audio Driver (playback endpoint) is detected and active.
        /// </summary>
        public static bool IsSpeakerInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                return devices.Any(d => d.FriendlyName.Contains(VirtualSpeakerKeyword, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets whether the Virtual Microphone Driver (capture endpoint) is detected and active.
        /// </summary>
        public static bool IsMicInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                return devices.Any(d => d.FriendlyName.Contains(VirtualMicKeyword, StringComparison.OrdinalIgnoreCase));
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
        /// Inspects PnP status for the Virtual Audio Driver via WMI.
        /// Returns 52 (CM_PROB_UNSIGNED_DRIVER) if signature enforcement is blocking the driver,
        /// 0 if working normally, or -1 if no device found.
        /// </summary>
        public static int GetDriverProblemErrorCode()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE Name LIKE '%Virtual Audio Driver%' OR DeviceID LIKE '%VirtualAudioDriver%'");
                
                foreach (ManagementObject obj in searcher.Get())
                {
                    if (obj["ConfigManagerErrorCode"] is uint uCode)
                        return (int)uCode;
                    if (obj["ConfigManagerErrorCode"] is int iCode)
                        return iCode;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Resolves the 1-based index (where 0 = Default, 1..N = Render devices) for the Virtual Speaker.
        /// Returns -1 if not found.
        /// </summary>
        public static int FindVirtualSpeakerIndex()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                for (int i = 0; i < devices.Count; i++)
                {
                    if (devices[i].FriendlyName.Contains(VirtualSpeakerKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return i + 1; // 1-based index (0 = default)
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// Resolves the absolute path to the bundled Virtual Audio Driver INF file.
        /// </summary>
        public static string? GetDriverInfPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidatePaths =
            {
                Path.Combine(baseDir, "drivers", "VirtualAudioDriver", InfFileName),
                Path.Combine(baseDir, "Resources", "Drivers", "VirtualAudioDriver", InfFileName),
                Path.Combine(Directory.GetCurrentDirectory(), "Resources", "Drivers", "VirtualAudioDriver", InfFileName)
            };

            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }

            return null;
        }

        /// <summary>
        /// Low-level SetupAPI routine that cleans duplicates, creates the ROOT\VirtualAudioDriver device node,
        /// and installs the driver via UpdateDriverForPlugAndPlayDevices.
        /// Requires Administrator privileges.
        /// </summary>
        public static (bool Success, string Message) CreateAndInstallRootDevice(string infPath)
        {
            try
            {
                if (!File.Exists(infPath))
                    return (false, $"Driver INF file not found at: {infPath}");

                string fullInfPath = Path.GetFullPath(infPath);

                // Clean up any stale/duplicate root instances first
                RemoveRootDevice();

                // Pre-stage in driver store with pnputil to ensure certificate and packages are registered
                try
                {
                    var pnpProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "pnputil.exe",
                        Arguments = $"/add-driver \"{fullInfPath}\" /install",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    pnpProcess?.WaitForExit(30000);
                }
                catch { }

                // 1. Resolve Class GUID and Class Name from INF
                var classNameBuilder = new StringBuilder(256);
                if (!SetupDiGetINFClass(fullInfPath, out Guid classGuid, classNameBuilder, (uint)classNameBuilder.Capacity, out _))
                {
                    classGuid = new Guid("{4d36e96c-e325-11ce-bfc1-08002be10318}");
                    classNameBuilder.Clear().Append("MEDIA");
                }
                string className = classNameBuilder.ToString();

                // 2. Create Device Info Set
                IntPtr devInfoSet = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
                if (devInfoSet == (IntPtr)(-1) || devInfoSet == IntPtr.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    return (false, $"SetupDiCreateDeviceInfoList failed (Error 0x{err:X8}).");
                }

                try
                {
                    // 3. Create Device Info Element
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();

                    if (!SetupDiCreateDeviceInfo(devInfoSet, className, ref classGuid, "Virtual Audio Driver by MTT", IntPtr.Zero, DICD_GENERATE_ID, ref devInfoData))
                    {
                        int err = Marshal.GetLastWin32Error();
                        return (false, $"SetupDiCreateDeviceInfo failed (Error 0x{err:X8}).");
                    }

                    // 4. Set SPDRP_HARDWAREID (double-null-terminated Unicode string for REG_MULTI_SZ)
                    byte[] hwidBytes = Encoding.Unicode.GetBytes(HardwareId + "\0\0");
                    if (!SetupDiSetDeviceRegistryProperty(devInfoSet, ref devInfoData, SPDRP_HARDWAREID, hwidBytes, (uint)hwidBytes.Length))
                    {
                        int err = Marshal.GetLastWin32Error();
                        return (false, $"SetupDiSetDeviceRegistryProperty failed (Error 0x{err:X8}).");
                    }

                    // 5. Register Device in system
                    if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, devInfoSet, ref devInfoData))
                    {
                        int err = Marshal.GetLastWin32Error();
                        return (false, $"SetupDiCallClassInstaller(DIF_REGISTERDEVICE) failed (Error 0x{err:X8}).");
                    }

                    // 6. Bind & Update Driver for the newly registered root device
                    bool rebootRequired;
                    if (!UpdateDriverForPlugAndPlayDevices(IntPtr.Zero, HardwareId, fullInfPath, INSTALLFLAG_FORCE, out rebootRequired))
                    {
                        int err = Marshal.GetLastWin32Error();
                        return (false, $"UpdateDriverForPlugAndPlayDevices failed (Error 0x{err:X8}).");
                    }

                    return (true, "Virtual Audio Driver device node created and driver started successfully.");
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfoSet);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception during root device creation: {ex.Message}");
            }
        }

        /// <summary>
        /// Removes any ROOT\VirtualAudioDriver device instances from Windows.
        /// </summary>
        public static (bool Success, string Message) RemoveRootDevice()
        {
            try
            {
                IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
                if (devInfoSet == (IntPtr)(-1) || devInfoSet == IntPtr.Zero)
                {
                    return (false, "Could not enumerate devices.");
                }

                try
                {
                    var devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
                    uint memberIndex = 0;
                    int removedCount = 0;

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
                                    removedCount++;
                                    continue; // Do not increment memberIndex
                                }
                            }
                        }

                        memberIndex++;
                    }

                    return (true, $"Removed {removedCount} device instance(s).");
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfoSet);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error removing root device: {ex.Message}");
            }
        }

        /// <summary>
        /// Cleans up published driver package from Driver Store.
        /// </summary>
        public static void UninstallDriverFromStore()
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -Command \"Get-WindowsDriver -Online -All | Where-Object { $_.OriginalFileName -like '*VirtualAudioDriver.inf*' } | ForEach-Object { pnputil /delete-driver $_.Driver /uninstall /force }\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit(30000);
            }
            catch { }
        }

        /// <summary>
        /// Installs the bundled Virtual Audio Driver. If not elevated, requests UAC elevation.
        /// </summary>
        public static async Task<(bool Success, string Message)> InstallDriverAsync()
        {
            string? infPath = GetDriverInfPath();
            if (string.IsNullOrEmpty(infPath) || !File.Exists(infPath))
            {
                return (false, "Driver package files (VirtualAudioDriver.inf) could not be located in application bundle.");
            }

            return await Task.Run(() =>
            {
                if (IsAdministrator())
                {
                    return CreateAndInstallRootDevice(infPath);
                }

                try
                {
                    string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "PaDDY.exe";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "--install-virtual-driver",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Normal
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                        return (false, "Failed to launch elevated installer process.");

                    process.WaitForExit(60000);

                    // Check actual device status after installation
                    int pnpError = GetDriverProblemErrorCode();
                    if (IsFullyOperational())
                    {
                        return (true, "Virtual Audio Driver was successfully installed and is active.");
                    }

                    if (pnpError == 52)
                    {
                        return (false, "Driver installed, but Windows blocked the kernel module (Code 52 - CM_PROB_UNSIGNED_DRIVER).\n\n" +
                                       "Because this open-source driver is not WHQL attestation signed by Microsoft, Windows requires Test-Signing mode:\n" +
                                       "1. Open Command Prompt as Administrator\n" +
                                       "2. Run: bcdedit /set testsigning on\n" +
                                       "3. Restart your computer.");
                    }

                    if (process.ExitCode == 0)
                    {
                        return (true, "Virtual Audio Driver was registered.");
                    }

                    return (false, $"Driver installation completed with exit code: {process.ExitCode}");
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
        /// Uninstalls the Virtual Audio Driver. If not elevated, requests UAC elevation.
        /// </summary>
        public static async Task<(bool Success, string Message)> UninstallDriverAsync()
        {
            return await Task.Run(() =>
            {
                if (IsAdministrator())
                {
                    var removeRes = RemoveRootDevice();
                    UninstallDriverFromStore();
                    return removeRes;
                }

                try
                {
                    string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "PaDDY.exe";
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = "--uninstall-virtual-driver",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Normal
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                        return (false, "Failed to launch elevated uninstaller process.");

                    process.WaitForExit(45000);
                    return (true, "Virtual Audio Driver removal completed.");
                }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                {
                    return (false, "Driver uninstallation was cancelled by the user (UAC elevation declined).");
                }
                catch (Exception ex)
                {
                    return (false, $"Driver uninstallation error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Enables Windows Test-Signing mode via elevated bcdedit.exe.
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
                        return (true, "Windows Test-Signing mode has been enabled successfully in BCD!\n\n" +
                                       "Please RESTART your computer now to allow Windows to load the Virtual Audio Driver.");
                    }

                    return (false, $"bcdedit exited with code {process.ExitCode}. If Secure Boot is enabled in your UEFI/BIOS, Windows may block testsigning until Secure Boot is disabled.");
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
