# Changelog

All notable changes to PaDDY will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

﻿## [1.5.0.0628] - 2026-06-28

- Added Nvidia CUDA GPU acceleration for speech-to-text engine.
- Add Digital Dots options for Audio Meters
- Added sets of New Themes
- Updated Icon and Wizard installation themes
- Registering PADBACK File Association

## [1.4.2.0623] - 2026-06-23

- docs updated
- fix pads color

## [1.4.1.0620] - 2026-06-20

- Fix vulnerable dependency on SQLite.
- Fix trimming editor not showing real pad name
- Updated installer theme

## [1.4.0.0618] - 2026-06-18

- Fix UI themes not being load correctly in some windows
- Fix sepia theme
- Fix performance drop when too much pads on screen
- Backend optimization
- Added backup and restore features

## [1.3.3.0614] - 2026-06-14

- Fix tray icon visibility when PaDDY is open
- Fix Rename windows UI

## [1.3.2.0613] - 2026-06-13

- .NET Security Update

## [1.3.1.0606] - 2026-06-06

- Fix recording pad text layout.
- Fix AudioProcessor versioning mismatch.

## [1.3.0.0603] - 2026-06-03

- Added Whisper AR-TTS support for text-to-speech generation with multiple voice options and adjustable parameters.
- Added system tray icon with context menu for quick access to main features and settings.
- Added Startup option to launch PaDDY on system startup.
- Added customizable favorites management with the ability to organize recordings into folders and subfolders.
- Added performance mode settings to optimize resource usage during recording and editing.
- Added new audio effects in the editor.
- Added new theme options.
- Loopback recording now supports selecting specific audio-producing applications, allowing for more targeted recording of system audio.
- Fix for audio playback issues with certain codecs and improved overall stability.
- Updated documentation.

## [1.2.4.0515] - 2026-05-15

- Fix EQ sliders being stuck at 0db

## [1.2.3.0514] - 2026-05-14

- Updated UI
- Favorites section can now being collapsed

## [1.2.0.0511] - 2026-05-11

- .Net10 migration
- Add trim editor output device settings
- New pad naming features
- Fixing OGG IndexOutOfRangeException
- Enhance audio playback
- Add monitor playback RMS meter
- Added meeter reset when stoping monitoring
- Ogg-critical patches and fix libmp3lame publish error
- Refactor audio processing for improved error handling and alignment in recording

## [1.1.4.0506] - 2026-05-06

- Preventing PaDDY to be launch twice
- UI enhancement

## [1.1.0.0504] - 2026-05-04

 **Add new effect processing pipeline with future support for VST3 plugins (currently only in trim editor)**

- Add Echo effect with delay and feedback parameters
- Add 5-band equalizer
- Add Noise gate with threshold and release parameters
- Add Fade in/out effect with adjustable duration

## [1.0.1.0501] - 2026-05-01

- Temporary fix codec fallback to WAVE when recording source is multichannel

## [1.0.0.0429] - 2026-04-29

- Refactor file paths to use AppDataPaths helper class for better maintainability and migration support
- Add Inno Setup installer script for easy installation and uninstallation of the application
- Implement recording management with SQLite backend
- Refactor code structure for improved readability and maintainability
- Major UI overhaul with new design and layout for better user experience
- Remove unused elements for improved performance
- Refactor UI elements and styles across multiple windows for improved consistency and aesthetics
- Add runtime info display and update check functionality in MainWindow
- Enhance PCM processing by adding support for 32-bit float audio and improving gain application logic
- Added FLAC support

## [0.9.2.0421] - 2026-04-21

- Meeter on editor trim are now showing correctly multichannel
- All sliders are now apply in real-time
- Fix output/monitor audio level intensity
- Fix input meeter showing mono signal on multichannel input

## [0.9.1.0420] - 2026-04-20

- Added multichannels inputs support up to 8 channels (7.1)
- Now support 24bit bit depth
- Now support sampling rate up to 96kHz
- Audio settings are now handle automatically based on the input source capabilities
- New downmixing pipeline for codecs that only support 2 channels
- New audio core pipeline
- Improving layout and remove unnecessary UI elements

## [0.8.0.0415] - 2026-04-15

- Add missing using directives and set window icon in RenameDialog
- Adjust playback latency and buffer for improved audio performance
- Update input device labels for clarity and consistency
- Refactor status bar layout and improve meter labels for clarity
- Add volume controls and playback metering features
- Fix assembly name casing in project file and update executable references
- Gain in Audio Editor now show in realtime the waveform being edited Add the option to save edited audio as a copy Add the option to save edited audio directly in the favorite Add keyboard shortcut on pad

## [0.7.2.0413] - 2026-04-13

- Improving layout and remove unnecessary text

## [0.7.1.0413] - 2026-04-13

- Fix minimum width windows

## [0.7.0.0412] - 2026-04-12

- Enhance folder button in MainWindow.xaml with an emoji icon for better visual representation
- Enhance audio seeking for Opus files by implementing decode-and-discard method to ensure accurate playback position
- Enhance OpusRecorder to support dynamic resampling and channel downmixing for improved audio quality
- Refactor buffer manipulation for audio processing to improve readability
- Add sorting functionality for recordings and gain control in audio editor
- Remove outdated configuration and development sections from README.md

## [0.6.3.0412] - 2026-04-12

- Update AssemblyName to 'noidsoftwork.core.paddy' and add targets for renaming executable

## [0.6.2.0412] - 2026-04-12

- Rename 'Paddy' to 'PaDDY' across the project
- name project edited in .csproj and .sln

## [0.6.1.0411] - 2026-04-11

- First public release
- fix: Correct spelling of 'PaDDY' to 'PaDDY' in various files
- Fix spelling of 'PaDDY' to 'PaDDY' in README
- Update README description for PaDDY application
- Enhance README with detailed application information

## [0.6.0.0325] - 2026-03-25

- refactor: Replace AudioFileReader with AudioReaderFactory for improved audio handling and update copyright year in push script

## [0.5.2.0325] - 2026-03-25

- fix: Correct copyright year in AssemblyInfo and ensure proper assembly attributes are set

## [0.5.1.0325] - 2026-03-25

- style: Refactor code formatting for consistency in AudioEditorWindow and Services
- feat: Implement audio codec selection and recording functionality; add support for MP3, Opus, and Ogg Vorbis formats
- feat: Update AboutWindow text and copyright year; modify MainWindow border color and empty hint message

## [0.5.0.0323] - 2026-03-23

- feat: Update window properties in AudioEditorWindow and SettingsWindow for consistency
- feat: Add Trim functionality with AudioEditorWindow for audio file editing
- style: Format button properties in RenameDialog for improved readability
- feat: Refactor RecordingPadButton and RenameDialog for improved functionality and UI updates

## [0.4.0.0322] - 2026-03-22

- feat: Add PreRelease option for GitHub releases in push script
- feat: Add MIT License file and update copyright notice in README
- feat: Enhance audio meter functionality with decay animation and improve AutoVAD sensitivity mapping
- feat: Implement peak hold indicators and dB meter for audio levels in MainWindow
- feat: Add entrance animation to RecordingPadButton and enhance UI with max recordings feature

## [0.3.2.0321] - 2026-03-21

- feat: enhance ComboBox styles and improve buffer duration handling
- refactor: streamline GitHub Actions workflow for .NET Core desktop app
- Add GitHub Actions workflow for .NET Core desktop app

## [0.3.1.0321] - 2026-03-21

- fix: update favorite and playing icons for better clarity

## [0.3.0.0321] - 2026-03-21

- Refactor XAML files for improved readability and consistency
- feat: Add favorite functionality to recordings

## [0.2.3.0321] - 2026-03-21

- feat: add SupportedOSPlatform attribute for Windows in RecordingPadButton and MainWindow
- Delete .vscode directory
- Delete .vs directory
- docs: add Visual Studio cache directories to .gitignore

## [0.2.2.0320] - 2026-03-20

- First dev build
- fix: update artifact path resolution to use script root

## [0.2.1.0320] - 2026-03-20

- docs: Add blank line for better readability in CHANGELOG

## [0.2.0.0320] - 2026-03-20

- Version bump

## [0.1.0.0101] - 2026-01-01

- Initial release
