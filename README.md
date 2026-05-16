# PaDDY

![PaDDY Hero](logo/github/PaDDY-wordmark-font-transparent-2x.png)

[![Version](https://img.shields.io/badge/version-1.2.4.0515-blue)](CHANGELOG.md)
[![Build](https://img.shields.io/badge/build-passing-brightgreen)](https://github.com/NoID1290/PaDDY/actions)
[![Platform](https://img.shields.io/badge/platform-Windows-blue)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Windows recording pad for quickly capturing, organizing, and replaying short audio clips.

Latest app version: **1.2.4.0515**

## Quick Links

- [Why PaDDY](#why-paddy)
- [Features](#features)
- [Install](#install)
- [Build from Source](#build-from-source)
- [Usage](#usage)
- [Screenshots](#screenshots)
- [Changelog](#changelog)
- [License](#license)

## Why PaDDY

PaDDY is built for fast capture with minimal friction:

- monitor microphone or system output
- auto-save clips with voice activity detection (AutoVAD)
- capture a rolling buffer on demand with a global hotkey (Key Buffer mode)
- play, favorite, rename, trim, and delete clips from a pad-style UI

## Features

- **Two recording modes**
  - **AutoVAD:** starts and stops clips based on sensitivity and silence timeout
  - **Key Buffer:** saves the last _N_ seconds when the hotkey is pressed
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

## Install

### Option 1: Download the latest release

1. Open [Releases](https://github.com/NoID1290/PaDDY/releases).
2. Download the latest artifact or zip package.
3. Extract the files.
4. Run `PaDDY.exe`.

## Build from Source

### Requirements

- **OS:** Windows 10/11
- **SDK:** [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Build commands

```powershell
dotnet restore PaDDY.sln
dotnet build PaDDY.csproj --configuration Release
```

## Usage

1. Select **Source** (Mic or Loopback) and devices.
2. Choose **Mode**:
   - **AutoVAD** for automatic clipping
   - **Key Buffer** for hotkey-triggered capture
3. Click **Start**.
4. New clips appear as pads in the recordings area.
5. Click a pad to play or stop. Use pad actions to favorite, rename, trim, or delete.

## Screenshots

![Main Windows](logo/github/PaDDY_1tpzjJSm1D.png)
![Trim Editor](logo/github/PaDDY_1fndG4WBlK.png)



## Changelog

Release history is maintained in [CHANGELOG.md](CHANGELOG.md).

## License

This project is licensed under the MIT License.
NoID Softwork © 2020-2026.
