#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Virtual Audio Driver (MTT) by adding it to the driver store
    AND creating the root-enumerated device node.

.DESCRIPTION
    pnputil /add-driver alone only registers the INF in the Windows Driver
    Store. For root-enumerated (software) devices like ROOT\VirtualAudioDriver
    there is no bus that triggers plug-and-play enumeration, so the device
    node must be created explicitly — equivalent to "Add Legacy Hardware" in
    Device Manager. This script automates that step via SetupAPI P/Invoke.

.PARAMETER InfPath
    Full path to VirtualAudioDriver.inf. Defaults to the directory containing
    this script.
#>
param(
    [string]$InfPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $InfPath) {
    $InfPath = Join-Path $PSScriptRoot "VirtualAudioDriver.inf"
}
$InfPath = [System.IO.Path]::GetFullPath($InfPath)

if (-not (Test-Path $InfPath)) {
    Write-Error "INF file not found: $InfPath"
    exit 1
}

# ── Check for existing installation ──────────────────────────────────────────
$existing = Get-PnpDevice -Class 'Media' -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -like '*Virtual Audio Driver*' -and $_.Status -eq 'OK' }
if ($existing) {
    Write-Host "Virtual Audio Driver is already installed."
    exit 0
}

# ── Step 1: Add the driver package to the Windows Driver Store ────────────────
Write-Host "Adding driver package to driver store..."
& "$env:SystemRoot\System32\pnputil.exe" /add-driver "$InfPath" /install
# Non-zero exit from pnputil may mean it was already staged — continue anyway.

# ── Step 2: Create root-enumerated device node + install driver via SetupAPI ─
Write-Host "Creating device node (ROOT\VirtualAudioDriver)..."

Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class VadDeviceInstaller
{
    // ── Structures ──────────────────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint   cbSize;
        public Guid   ClassGuid;
        public uint   DevInst;
        public IntPtr Reserved;
    }

    // ── Constants ────────────────────────────────────────────────────────────
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    // MEDIA device class GUID  {4d36e96c-e325-11ce-bfc1-08002be10318}
    public static readonly Guid GUID_DEVCLASS_MEDIA =
        new Guid("{4d36e96c-e325-11ce-bfc1-08002be10318}");

    private const uint DICD_GENERATE_ID    = 0x00000001;  // generate new instance ID
    private const uint SPDRP_HARDWAREID    = 0x00000001;
    private const uint DIF_REGISTERDEVICE  = 0x00000019;
    public  const string HARDWARE_ID       = "ROOT\\VirtualAudioDriver";

    // ── SetupAPI imports ──────────────────────────────────────────────────────
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(
        ref Guid ClassGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiCreateDeviceInfo(
        IntPtr DeviceInfoSet, string DeviceName, ref Guid ClassGuid,
        string DeviceDescription, IntPtr hwndParent, uint CreationFlags,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetDeviceRegistryProperty(
        IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData,
        uint Property, byte[] PropertyBuffer, uint PropertyBufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(
        uint InstallFunction, IntPtr DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

    // ── newdev.dll import ─────────────────────────────────────────────────────
    // DiInstallDevice operates directly on the SP_DEVINFO_DATA we just created,
    // so it finds the driver without needing a separate enumeration pass.
    [DllImport("newdev.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool DiInstallDevice(
        IntPtr       hwndParent,
        IntPtr       DeviceInfoSet,
        ref SP_DEVINFO_DATA DeviceInfoData,
        IntPtr       DriverInfoData,   // NULL → pick best match from store
        uint         Flags,
        out bool     NeedReboot);

    // ── Public entry point ────────────────────────────────────────────────────
    /// <returns>Win32 error code; 0 = success.</returns>
    public static int CreateAndInstall(string infPath)
    {
        var classGuid  = GUID_DEVCLASS_MEDIA;
        IntPtr devInfoSet = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (devInfoSet == INVALID_HANDLE_VALUE)
            return Marshal.GetLastWin32Error();

        try
        {
            var did = new SP_DEVINFO_DATA();
            did.cbSize = (uint)Marshal.SizeOf(did);

            // Create a new device information element (generates instance ID)
            if (!SetupDiCreateDeviceInfo(devInfoSet, "VirtualAudioDriver",
                    ref classGuid, "Virtual Audio Driver by MTT",
                    IntPtr.Zero, DICD_GENERATE_ID, ref did))
                return Marshal.GetLastWin32Error();

            // Set hardware ID as REG_MULTI_SZ (double-null-terminated UTF-16)
            byte[] hwIdBytes = Encoding.Unicode.GetBytes(HARDWARE_ID + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(devInfoSet, ref did,
                    SPDRP_HARDWAREID, hwIdBytes, (uint)hwIdBytes.Length))
                return Marshal.GetLastWin32Error();

            // Commit the device node to the system device tree
            if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, devInfoSet, ref did))
                return Marshal.GetLastWin32Error();

            // Install the best matching driver from the driver store
            bool needReboot;
            if (!DiInstallDevice(IntPtr.Zero, devInfoSet, ref did,
                    IntPtr.Zero, 0, out needReboot))
                return Marshal.GetLastWin32Error();

            return 0;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(devInfoSet);
        }
    }
}
'@

$rc = [VadDeviceInstaller]::CreateAndInstall($InfPath)
if ($rc -eq 0) {
    Write-Host "Virtual Audio Driver installed successfully."
    exit 0
} else {
    Write-Error "Device creation failed. Win32 error code: $rc"
    exit $rc
}
