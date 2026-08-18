# PaDDY

<p align="center">
  <img src="logo/github/PaDDY-wordmark-font-transparent-2x.png" alt="PaDDY Hero" width="400">
</p>

<p align="center">
  <a href="CHANGELOG.md"><img src="https://img.shields.io/badge/version-2.3.0.0818-darkgreen?style=flat-square" alt="Version"></a>
  <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Platform"></a>
  <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-10.0-blue?style=flat-square" alt=".NET"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" alt="License"></a>
</p>

<p align="center">
  <strong>Fast voice and system-audio capture for Windows, built around a pad-based workflow for recording, organizing, monitoring, trimming, and replaying short clips.</strong>
</p>

<p align="center">
  🌐 <a href="https://noid1290.github.io/PaDDY/">Official Website</a> • 📦 <a href="https://github.com/NoID1290/PaDDY/releases">Releases</a>
</p>

---

## 📖 Table of Contents
- [Overview](#-overview)
- [Core Highlights](#-core-highlights)
- [Deep Feature Breakdown](#-deep-feature-breakdown)
- [Configuration & Placeholders](#-configuration--placeholders)
- [Installation & Requirements](#-installation--requirements)
- [Building From Source](#-building-from-source)
- [Workflow Guide](#-workflow-guide)
- [Screenshots](#-screenshots)
- [License](#-license)

---

## 🔍 Overview

**PaDDY** is a specialized Windows audio recorder engineered for **low-friction capture**. It simultaneously supports standard microphone input, full system loopback, and targeted, app-specific loopback. Captured clips are piped straight into a highly visual, pad-based interface where you can quickly preview, manage, trim, and apply real-time DSP effects—all backed by a lightning-fast SQLite storage layer.

---

## ✨ Core Highlights

| Feature | Capabilities |
| :--- | :--- |
| **Dual Capture Modes** | **AutoVAD** (Voice Activity Detection) or **Key Buffer** (rolling retro-active cache). |
| **Flexible Sources** | Microphone, line-in, entire Windows audio subsystem, or targeted app loopback. |
| **Premium Formats** | Export flawlessly to **WAV**, **MP3**, **Opus**, **Ogg Vorbis**, **AAC** or **FLAC**. |
| **Pro Level Metering** | Live input, playback output, and independent monitor RMS meters with peak indicators. |

---

## 🚀 Deep Feature Breakdown

### Audio Capture Modes

#### AutoVAD Mode
Voice Activity Detection tracks voice thresholds dynamically. When speech starts, it automatically launches a clip sequence. When silence persists past the configured timeout window, it stops recording cleanly.

#### Key Buffer Mode
Also called rolling cache or retroactive capture. This maintains a silent, zero-impact background allocation array that continuously fills in memory. You can instantly extract and commit the last N seconds of history via a global hotkey context.

---

## 🎚️ Configuration & Placeholders

### Environment Variables

| Variable | Description | Default Value |
|----------|-------------|---------------|
| `PADDY_AUTOVAD_ENABLE` | Enable or disable AutoVAD mode | `true` |
| `PADDY_BUFFER_SECONDS` | Rolling cache duration in seconds | `5` |
| `PADDY_SILENCE_TIMEOUT` | Silence timeout for AutoVAD in ms | `1000` |

---

## 📦 Installation & Requirements

### System Prerequisites

- Windows 10/11 (64-bit)
- .NET 10.0 SDK or later
- Audio interface with proper drivers installed
- At least 512 MB RAM (8 GB recommended)

---

## 🔨 Building From Source

```bash
# Clone the repository
git clone https://github.com/NoID1290/PaDDY.git
cd PaDDY

# Restore dependencies and build
dotnet restore && dotnet build

# Run locally
dotnet run
```

---

## 🔄 Workflow Guide

### Recording with AutoVAD

1. Launch **PaDDY** from the Start Menu or desktop shortcut
2. Select your preferred audio capture mode: **AutoVAD** or **Key Buffer**
3. Choose a source (microphone, system-wide, or app-specific)
4. Click **Start Record** and speak naturally when ready
5. When silence lasts longer than your configured timeout, PaDDY auto-stops recording

### Recording with Key Buffer

1. Launch **PaDDY** from the Start Menu or desktop shortcut
2. Select **Key Buffer** mode in settings
3. Choose your preferred source (microphone, system-wide, or app-specific)
4. Click **Start Record** and continue normal activity
5. Press `Alt+R` (or your configured hotkey) at any time to capture the last N seconds

---

## 📸 Screenshots

<p align="center">
  <img src="https://raw.githubusercontent.com/NoID1290/PaDDY/master/Assets/Screenshot.png" alt="PaDDY Screenshot">
</p>

---

## 📜 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

<div align="center">
  <small>Built with ❤️ by PaDDY contributors</small>
</div>