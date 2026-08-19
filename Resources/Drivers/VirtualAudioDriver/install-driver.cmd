@echo off
setlocal
echo ===================================================
echo  PaDDY - Virtual Audio Driver Installer
echo ===================================================
cd /d "%~dp0"

echo 1. Staging driver package in Windows DriverStore...
pnputil /add-driver "VirtualAudioDriver.inf" /install

echo 2. Instantiating ROOT device node via PaDDY engine...
if exist "..\..\PaDDY.exe" (
    "..\..\PaDDY.exe" --install-virtual-driver
) else if exist "..\PaDDY.exe" (
    "..\PaDDY.exe" --install-virtual-driver
) else if exist "PaDDY.exe" (
    "PaDDY.exe" --install-virtual-driver
)

echo Done.
exit /b %ERRORLEVEL%
