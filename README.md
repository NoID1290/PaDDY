# PaDDY

[![Version](https://img.shields.io/badge/version-0.6.1-blue)](CHANGELOG.md)
[![Platform](https://img.shields.io/badge/platform-Windows-blue)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Windows application recording pad for quickly capturing, organizing, and replaying short audio clips.

## Why PaDDY

PaDDY is designed for fast voice/audio capture with minimal friction:

- monitor microphone or system output
- auto-save clips with voice activity detection (AutoVAD)
- capture a rolling buffer on demand with a global hotkey (Key Buffer mode)
- play, favorite, rename, trim, and delete clips from a pad-style UI

## Features

- **Two recording modes**
  - **AutoVAD:** starts/stops clips based on sensitivity + silence timeout
  - **Key Buffer:** saves the last _N_ seconds when hotkey is pressed
- **Input sources**
  - microphone capture
  - output loopback capture
- **Output formats**
  - WAV, MP3, Opus, Ogg Vorbis
- **Audio controls**
  - sensitivity slider with threshold marker
  - silence timeout control
  - live L/R RMS meter with peak indicators
- **Clip management**
  - favorites panel
  - trim editor
  - rename, delete single clip, clear pads, delete all files
  - optional max-record auto cleanup (favorites exempt)
- **Playback routing**
  - choose playback output device
  - optional separate listen/monitor output

## Requirements

- **OS:** Windows 10/11
- **Runtime:** [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Getting Started

### Option 1: Use a release build

1. Download the latest release artifact.
2. Extract it.
3. Run `PaDDY.exe`.

### Option 2: Build from source

```powershell
dotnet restore PaDDY.sln
dotnet build PaDDY.csproj --configuration Release
```

> Note: PaDDY targets `net8.0-windows` (WPF), so building is expected on Windows.

## Usage

1. Select **Source** (Mic or Loopback) and devices.
2. Choose **Mode**:
   - **AutoVAD** for automatic clipping
   - **Key Buffer** for hotkey-triggered capture
3. Click **Start**.
4. New clips appear as pads in the recordings area.
5. Click a pad to play/stop. Use pad actions to favorite, rename, trim, or delete.

## Configuration

Settings are saved in `appsettings.json` next to the app executable.

Configurable options include:

- capture source/device and playback devices
- codec, sample rate, bit depth, channels
- save folder
- AutoVAD sensitivity and silence timeout
- Key Buffer duration and global hotkey (default `Ctrl+F9`)
- max recordings limit

## Repository Structure

- `/Controls` - reusable UI controls (recording pad, dialogs)
- `/Services` - capture, encoding, playback, hotkey, and audio IO services
- `/Models` - data models
- `MainWindow.*` - primary app UI and interaction logic
- `SettingsWindow.*` - settings UI and persistence wiring
- `AudioEditorWindow.*` - waveform trim editor

## Development

CI workflow restores and builds the app on Windows via:

- `.github/workflows/dotnet-desktop.yml`

## Contributing

Issues and pull requests are welcome.

## License

This project is licensed under the MIT License.  
NoID Softwork © 2020-2026.

