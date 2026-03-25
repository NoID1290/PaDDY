# Changelog

All notable changes to Paddy will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

Version format: `a.b.c.MMDD`

- `a` = Frontend (GUI) update — resets b and c to 0
- `b` = Backend update — resets c to 0
- `c` = Little fix / patch
- `MMDD` = Month and day of push (auto-generated)

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
