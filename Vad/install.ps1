#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs the Virtual Audio Driver (MTT) automatically.
    Equivalent to "Add Legacy Hardware" in Device Manager.

.PARAMETER InfPath
    Full path to VirtualAudioDriver.inf. Defaults to the script's directory.
#>
param([string]$InfPath = "")

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LogFile = "$env:TEMP\PaDDY-VadInstall.log"
function Log($msg) {
    $ts = Get-Date -Format 'HH:mm:ss'
    "$ts  $msg" | Tee-Object -FilePath $LogFile -Append | Write-Host
}
Log "=== VadInstall start ==="

function Get-VadEndpoints {
    try {
        return Get-PnpDevice -Class 'AudioEndpoint' -ErrorAction Stop |
            Where-Object {
                $_.FriendlyName -like '*Virtual Audio Driver*' -or
                $_.FriendlyName -like '*Virtual Mic Driver*'
            }
    } catch {
        Log "WARNING: Unable to query AudioEndpoint devices: $_"
        return @()
    }
}

function Test-VadEndpointsReady {
    param([System.Collections.IEnumerable]$Endpoints)

    if (-not $Endpoints) { return $false }

    $speaker = $Endpoints | Where-Object { $_.FriendlyName -like '*Virtual Audio Driver*' }
    $microphone = $Endpoints | Where-Object { $_.FriendlyName -like '*Virtual Mic Driver*' }

    if (-not $speaker -or -not $microphone) { return $false }

    $speakerReady = $speaker | Where-Object { $_.Status -eq 'OK' }
    $microphoneReady = $microphone | Where-Object { $_.Status -eq 'OK' }

    return ($speakerReady -and $microphoneReady)
}

if (-not $InfPath) { $InfPath = Join-Path $PSScriptRoot "VirtualAudioDriver.inf" }
$InfPath = [System.IO.Path]::GetFullPath($InfPath)
if (-not (Test-Path $InfPath)) { Log "ERROR: INF not found: $InfPath"; exit 1 }
Log "INF path: $InfPath"

# ── Minimal SetupAPI helper — only CreateDeviceNode ───────────────────────────
try {
Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class VadInstaller
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint   cbSize;
        public Guid   ClassGuid;
        public uint   DevInst;
        public IntPtr Reserved;
    }

    private static readonly IntPtr INVALID_HANDLE = new IntPtr(-1);
    public  static readonly Guid   GUID_DEVCLASS_MEDIA =
        new Guid("{4d36e96c-e325-11ce-bfc1-08002be10318}");

    private const uint DICD_GENERATE_ID   = 0x00000001;
    private const uint SPDRP_HARDWAREID   = 0x00000001;
    private const uint DIF_REGISTERDEVICE = 0x00000019;

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiCreateDeviceInfoList(
        ref Guid ClassGuid, IntPtr hwnd);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiCreateDeviceInfo(
        IntPtr DevInfoSet, string DeviceName, ref Guid ClassGuid,
        string Description, IntPtr hwnd, uint CreationFlags,
        ref SP_DEVINFO_DATA DevInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetDeviceRegistryProperty(
        IntPtr DevInfoSet, ref SP_DEVINFO_DATA DevInfoData,
        uint Property, byte[] Buffer, uint BufferSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(
        uint InstallFunction, IntPtr DevInfoSet,
        ref SP_DEVINFO_DATA DevInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DevInfoSet);

    public static int CreateDeviceNode(string hwid)
    {
        var classGuid = GUID_DEVCLASS_MEDIA;
        IntPtr set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (set == INVALID_HANDLE) return Marshal.GetLastWin32Error();
        try
        {
            var did = new SP_DEVINFO_DATA
                { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };

            if (!SetupDiCreateDeviceInfo(set, "VirtualAudioDriver",
                    ref classGuid, "Virtual Audio Driver by MTT",
                    IntPtr.Zero, DICD_GENERATE_ID, ref did))
                return Marshal.GetLastWin32Error();

            // Hardware ID as REG_MULTI_SZ (double-null-terminated UTF-16)
            byte[] hwIdBytes = Encoding.Unicode.GetBytes(hwid + "\0\0");
            if (!SetupDiSetDeviceRegistryProperty(set, ref did,
                    SPDRP_HARDWAREID, hwIdBytes, (uint)hwIdBytes.Length))
                return Marshal.GetLastWin32Error();

            // Register the device node in the system device tree
            if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, ref did))
                return Marshal.GetLastWin32Error();

            return 0;
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
'@
} catch {
    Log "ERROR: Failed to compile helper: $_"
    exit 1
}

$hwid = "ROOT\VirtualAudioDriver"

# ── Step 1: Stage the driver package ─────────────────────────────────────────
Log "Step 1: Staging driver with pnputil..."
$out = & "$env:SystemRoot\System32\pnputil.exe" /add-driver "$InfPath" /install 2>&1
Log "pnputil exit=$LASTEXITCODE  $($out -join ' ')"
if ($LASTEXITCODE -ne 0) {
    Log "ERROR: pnputil /add-driver failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# ── Step 2: Remove any existing zombie device node ────────────────────────────
$node = Get-PnpDevice -Class 'Media' -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -like 'ROOT\VIRTUALAUDIODRIVER*' }
if ($node) {
    Log "Step 2: Removing existing device node (Status=$($node.Status))..."
    $out = & "$env:SystemRoot\System32\pnputil.exe" /remove-device $node.InstanceId 2>&1
    Log "remove-device: exit=$LASTEXITCODE  $($out -join ' ')"
    Start-Sleep -Milliseconds 1000
} else {
    Log "Step 2: No existing device node."
}

# ── Step 3: Create fresh root-enumerated device node ─────────────────────────
Log "Step 3: Creating device node..."
$rc = [VadInstaller]::CreateDeviceNode($hwid)
Log "CreateDeviceNode returned: $rc"
if ($rc -ne 0) { Log "ERROR: CreateDeviceNode failed (Win32=$rc)"; exit $rc }

# ── Step 4: PnP scan triggers automatic driver match + install ────────────────
# After DIF_REGISTERDEVICE, the PnP manager finds the staged oem*.inf that
# matches ROOT\VirtualAudioDriver and installs it automatically.
Log "Step 4: Triggering PnP rescan..."
$out = & "$env:SystemRoot\System32\pnputil.exe" /scan-devices 2>&1
Log "scan-devices: exit=$LASTEXITCODE  $($out -join ' ')"
if ($LASTEXITCODE -ne 0) {
    Log "ERROR: pnputil /scan-devices failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Wait for Windows Audio to enumerate the new device
Log "Waiting for device enumeration..."
$deadline = (Get-Date).AddSeconds(30)
$status = "UNKNOWN"
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    $n = Get-PnpDevice -Class 'Media' -ErrorAction SilentlyContinue |
        Where-Object { $_.InstanceId -like 'ROOT\VIRTUALAUDIODRIVER*' }
    if ($n) {
        $status = $n.Status
        Log "  Device status: $status"
        if ($status -eq 'OK') { break }
    }
}

Log "Final device status: $status"
if ($status -ne 'OK') {
    Log "ERROR: Driver root node did not reach OK status (Final='$status')."
    exit 2
}

# ── Step 5: Verify speaker + microphone endpoints are visible and healthy ───
Log "Step 5: Verifying audio endpoints..."
$endpointDeadline = (Get-Date).AddSeconds(40)
$lastEndpoints = @()
while ((Get-Date) -lt $endpointDeadline) {
    Start-Sleep -Seconds 2
    $endpoints = @(Get-VadEndpoints)
    $lastEndpoints = $endpoints
    if ($endpoints.Count -gt 0) {
        $summary = ($endpoints | ForEach-Object { "$($_.FriendlyName) [$($_.Status)]" }) -join '; '
        Log "  Endpoint snapshot: $summary"
    }

    if (Test-VadEndpointsReady -Endpoints $endpoints) {
        Log "SUCCESS: Virtual speaker and microphone endpoints are present and OK."
        exit 0
    }
}

if ($lastEndpoints.Count -eq 0) {
    Log "ERROR: No virtual audio endpoints detected after installation."
} else {
    $finalSummary = ($lastEndpoints | ForEach-Object { "$($_.FriendlyName) [$($_.Status)]" }) -join '; '
    Log "ERROR: Endpoints detected but not ready: $finalSummary"
}
exit 3

