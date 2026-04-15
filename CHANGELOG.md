# Changelog

All notable changes to PaDDY will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
