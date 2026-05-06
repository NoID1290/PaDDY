#Requires -RunAsAdministrator
<#!
.SYNOPSIS
    Uninstalls the Virtual Audio Driver device node and driver package.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$LogFile = "$env:TEMP\PaDDY-VadUninstall.log"
function Log($msg) {
    $ts = Get-Date -Format 'HH:mm:ss'
    "$ts  $msg" | Tee-Object -FilePath $LogFile -Append | Write-Host
}

Log "=== VadUninstall start ==="

$hadError = $false

try {
    $nodes = @(Get-PnpDevice -Class 'Media' -ErrorAction Stop |
        Where-Object { $_.InstanceId -like 'ROOT\VIRTUALAUDIODRIVER*' })
} catch {
    Log "WARNING: Failed to query Media devices: $_"
    $nodes = @()
}

if ($nodes.Count -gt 0) {
    foreach ($node in $nodes) {
        Log "Removing device node: $($node.InstanceId)"
        $out = & "$env:SystemRoot\System32\pnputil.exe" /remove-device "$($node.InstanceId)" 2>&1
        Log "remove-device exit=$LASTEXITCODE  $($out -join ' ')"
        if ($LASTEXITCODE -ne 0) {
            Log "ERROR: Failed removing device node $($node.InstanceId)"
            $hadError = $true
        }
    }
} else {
    Log "No ROOT\\VIRTUALAUDIODRIVER device node found."
}

$driverPublishedNames = @()
try {
    $enumOut = & "$env:SystemRoot\System32\pnputil.exe" /enum-drivers 2>&1
    $currentPublishedName = $null
    $currentOriginalName = $null

    foreach ($line in $enumOut) {
        if ($line -match '^\s*Published Name\s*:\s*(.+)$') {
            $currentPublishedName = $Matches[1].Trim()
            $currentOriginalName = $null
            continue
        }

        if ($line -match '^\s*Original Name\s*:\s*(.+)$') {
            $currentOriginalName = $Matches[1].Trim()
            if ($currentPublishedName -and $currentOriginalName -ieq 'VirtualAudioDriver.inf') {
                $driverPublishedNames += $currentPublishedName
            }
            continue
        }

        if ([string]::IsNullOrWhiteSpace($line)) {
            $currentPublishedName = $null
            $currentOriginalName = $null
        }
    }
} catch {
    Log "WARNING: Failed to enumerate driver packages: $_"
}

$driverPublishedNames = $driverPublishedNames | Select-Object -Unique

if ($driverPublishedNames.Count -gt 0) {
    foreach ($publishedName in $driverPublishedNames) {
        Log "Removing driver package: $publishedName"
        $out = & "$env:SystemRoot\System32\pnputil.exe" /delete-driver "$publishedName" /uninstall /force 2>&1
        Log "delete-driver exit=$LASTEXITCODE  $($out -join ' ')"
        if ($LASTEXITCODE -ne 0) {
            Log "ERROR: Failed removing driver package $publishedName"
            $hadError = $true
        }
    }
} else {
    Log "No VirtualAudioDriver.inf package found in driver store."
}

Log "Triggering PnP rescan..."
$out = & "$env:SystemRoot\System32\pnputil.exe" /scan-devices 2>&1
Log "scan-devices exit=$LASTEXITCODE  $($out -join ' ')"
if ($LASTEXITCODE -ne 0) {
    Log "WARNING: PnP rescan failed with exit code $LASTEXITCODE"
}

if ($hadError) {
    Log "Completed with errors."
    exit 1
}

Log "SUCCESS."
exit 0
