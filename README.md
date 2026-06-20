# PaDDY

![PaDDY Hero](logo/github/PaDDY-wordmark-font-transparent-2x.png)

[![Version](https://img.shields.io/badge/version-1.4.1.0620-darkgreen)](CHANGELOG.md)
[![Platform](https://img.shields.io/badge/platform-Windows-blue)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

Fast voice and system-audio capture for Windows, built around a pad-based workflow for recording, organizing, monitoring, trimming, and replaying short clips.

- Website: <https://noid1290.github.io/PaDDY/>
- Releases: <https://github.com/NoID1290/PaDDY/releases>

## Contents

- [Overview](#overview)
- [Highlights](#highlights)
- [Feature Details](#feature-details)
- [Settings](#settings)
- [Installation](#installation)
- [Build from Source](#build-from-source)
- [Usage](#usage)
- [Screenshots](#screenshots)
- [Changelog](#changelog)
- [License](#license)

## Overview

PaDDY is a Windows desktop recorder focused on low-friction capture.
It supports microphone input, system loopback capture, and app-specific loopback capture, then lets you manage clips from a visual pad interface with favorites, trimming, naming rules, monitoring, and playback routing.

## Highlights

- **Two recording modes**
  - **AutoVAD**: automatically starts and stops clips based on input activity
  - **Key Buffer**: saves the last _N_ seconds using a global hotkey
- **Multiple capture sources**
  - microphone / line input
  - full system loopback
  - app-specific loopback capture
- **Flexible recording formats**
  - WAV
  - MP3
  - Opus
  - Ogg Vorbis
  - FLAC
- **Modern monitoring and metering**
  - live input, output, and monitor RMS meters
  - threshold indicator and peak indicators
  - optional separate monitor output device
- **Pad-based clip workflow**
  - instant playback from recording pads
  - favorites section with collapsible panel
  - rename, delete, clear, and sort recordings
  - SQLite-backed recording management
- **Built-in trim editor**
  - waveform-based trimming
  - preview playback
  - save trim in place or as a copy
  - optionally add trimmed output to favorites
- **Effects processing**
  - gain
  - fade in / fade out
  - noise gate
  - echo
  - 5-band equalizer
- **Customization and convenience**
  - configurable buffer duration
  - global hotkey assignment
  - automatic cleanup with favorites exemption
  - custom naming templates with placeholders
  - focused app naming support
  - trim editor output device selection
  - font variant selection
  - single-instance protection
  - update notice in the main window

## Feature Details

### Recording modes

#### AutoVAD
AutoVAD watches the incoming signal and creates clips automatically based on sensitivity and silence timeout settings.
This is useful when you want hands-free voice capture.

#### Key Buffer
Key Buffer continuously keeps a rolling buffer and saves the most recent audio when you press the configured global hotkey.
This is useful when you want to capture something that already happened.

### Input and capture

PaDDY supports several audio input flows:

- **Mic / line capture** for voice or external input devices
- **System loopback** for recording what Windows is playing
- **App loopback** for targeting a specific audio-producing application

The UI includes source selection, input device switching, input volume control, and mode-aware controls.

### Playback and monitoring

Playback can be routed to a selected output device, and pad monitoring can be enabled separately with its own device and volume control.
The main window also exposes dedicated RMS meters for:

- input
- playback output
- monitor output

### Trim editor and effects

The trim editor provides waveform-based editing with draggable trim handles, live preview, and quick save actions.
You can save the edited result directly or create a copy.

Available processing and editing controls include:

- gain
- fade in / fade out
- noise gate
- echo
- 5-band EQ at **80Hz**, **250Hz**, **1kHz**, **4kHz**, and **12kHz**

## Settings

PaDDY includes settings for:

- recording codec selection
- buffer history duration
- buffer trigger hotkey
- max recordings auto-cleanup
- default pad naming template
- focused app naming
- trim editor output device
- font variant / appearance

Naming placeholders currently include:

- `{timestamp}`
- `{codec}`
- `{app}`

## Installation

### Download a release

1. Open [Releases](https://github.com/NoID1290/PaDDY/releases).
2. Download the latest zip package.
3. Extract the files.
4. Run `PaDDY.exe`.

## Build from source

### Requirements

- Windows 10 or Windows 11
- .NET 10 SDK
- x64 environment

### Build

```powershell
dotnet restore PaDDY.sln
dotnet build PaDDY.csproj --configuration Release
```

### Project details

- **Framework:** `net10.0-windows`
- **UI:** WPF
- **Architecture:** `win-x64`
- **Deployment:** self-contained

## Usage

1. Choose an input source.
2. Select the relevant input or loopback device.
3. Pick a recording mode:
   - **AutoVAD** for automatic clip detection
   - **Key Buffer** for hotkey-triggered capture
4. Configure output, monitoring, or sensitivity settings as needed.
5. Start monitoring / recording from the main window.
6. Use recording pads to play, favorite, rename, trim, or delete clips.
7. Open Settings to customize codecs, naming, cleanup, hotkeys, and editor playback.

## Screenshots

![Main Window](logo/github/PaDDY_1tpzjJSm1D.png)
![Trim Editor](logo/github/PaDDY_1fndG4WBlK.png)

## Recent additions

Some of the newer capabilities added after the original README include:

- FLAC support
- .NET 10 migration
- app-specific loopback capture
- SQLite-backed recording storage
- improved playback and monitoring meters
- new naming features and placeholders
- collapsible favorites section
- trim editor output device settings
- effect processing pipeline for editing
- single-instance protection

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full release history.

## License

This project is licensed under the MIT License.
NoID Softwork © 2020-2026.
