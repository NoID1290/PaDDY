@echo off
setlocal enabledelayedexpansion
echo ===================================================
echo  PaDDY - Virtual Audio Driver Uninstaller
echo ===================================================
cd /d "%~dp0"

echo 1. Removing ROOT device instances...
if exist "..\..\PaDDY.exe" (
    "..\..\PaDDY.exe" --uninstall-virtual-driver
) else if exist "..\PaDDY.exe" (
    "..\PaDDY.exe" --uninstall-virtual-driver
) else if exist "PaDDY.exe" (
    "PaDDY.exe" --uninstall-virtual-driver
)

echo 2. Searching for published VirtualAudioDriver OEM INF in Driver Store...
for /f "tokens=1,2 delims=:" %%a in ('pnputil /enum-drivers') do (
    set "LINE_KEY=%%a"
    set "LINE_VAL=%%b"
    set "LINE_KEY=!LINE_KEY: =!"
    set "LINE_VAL=!LINE_VAL: =!"
    
    if /i "!LINE_KEY!"=="PublishedName" (
        set "CURRENT_OEM=!LINE_VAL!"
    )
    if /i "!LINE_KEY!"=="OriginalName" (
        if /i "!LINE_VAL!"=="VirtualAudioDriver.inf" (
            if defined CURRENT_OEM (
                echo Found published driver package: !CURRENT_OEM!
                echo Removing !CURRENT_OEM!...
                pnputil /delete-driver "!CURRENT_OEM!" /uninstall /force
            )
        )
    )
)

echo Done.
exit /b 0
