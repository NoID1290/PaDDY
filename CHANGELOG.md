# Changelog

All notable changes to PaDDY will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [1.0.0.0427-Pre-release_7] - 2026-04-27

- Add wizard-small.bmp image for installer wizard interface

## [1.0.0.0427-Pre-release_6] - 2026-04-27

- inno stage 2

## [1.0.0.0427-Pre-release_5] - 2026-04-27

- Inno stage 1
- Remove unused button for opening recordings folder in the top bar
- Update installer name in build script to "PaDDY-$newVersion-Installer"

## [1.0.0.0427-Pre-release_4] - 2026-04-27

- Update output name in Inno Setup script to "PaDDY-Installer"

## [1.0.0.0427-Pre-release_3] - 2026-04-27

- Update version and source directory i
- Refactor confirmation message formatting for clarity in uninstall prompt

## [1.0.0.0427-Pre-release_2] - 2026-04-27

- Refactor AppDataPaths for legacy migration support and update Inno Setup script for improved installer configuration

## [1.0.0.0427-Pre-release_1] - 2026-04-27

- Refactor file paths to use AppDataPaths helper class for better maintainability and migration support

## [1.0.0.0426-Pre-release_6] - 2026-04-26

- Update Inno Setup executable path in push.ps1 for installer build

## [1.0.0.0426-Pre-release_5] - 2026-04-26

- Add installer creation and upload functionality to push.ps1
- inno gitigniore
- Update Export button alignment and margin in RecordingPadButton for improved layout
- Implement recording deletion and storage compaction: update ClearPadsButton to delete non-favorite recordings and add Compact method for database optimization.
- Implement recording management with SQLite backend: add RecordingStore service, enhance RecordingEntry model, and integrate export functionality in RecordingPadButton.
- Enhance versioning logic: freeze release timestamp to avoid MMDD/date drift and normalize version date in changelog updates

## [1.0.0.0426-Pre-release_4] - 2026-04-26

- Enhance versioning logic to support 'none' type for MMDD updates and adjust pre-release versioning in AssemblyInfo and CHANGELOG
- Refactor pre-release versioning logic to retain numeric values in .csproj files while applying suffix to tags, release, and CHANGELOG
- Implement custom window chrome: Add title bars and close buttons to multiple windows for improved UI consistency
- Refactor font loading logic: Simplify URI creation for font families in ApplyFont method
- Refactor code structure for improved readability and maintainability
- Refactor font resource handling: Change FontFamily bindings to use DynamicResource for better runtime flexibility
- Enhance UI and functionality: Added config panel toggle, updated settings window layout, and introduced new app theme resources.
- Rename "CAPTURE SOURCE" to "INPUT" and add Input Volume control with slider and label
- Enhance version display in AboutWindow to include pre-release suffix and update project file to add InformationalVersion and other metadata
- 3
- Version bump
- Enhance asset management: Build and attach artifacts during GitHub release creation
- Fix GitHub release notes handling: write to temp file to avoid shell escaping issues
- Update AboutWindow and MainWindow for improved clarity and consistency; adjust SettingsWindow height and codec descriptions for better accuracy
- Refactor RenameDialog to use XAML for UI definition and simplify code structure
- Add drop shadow effects to close buttons in About, Audio Editor, Credits, and Settings windows for improved visual appeal
- Add drop shadow effects to window control buttons for enhanced visual appeal
- Refactor logo animation and remove unused elements for improved performance
- Refactor UI elements and styles across multiple windows for improved consistency and aesthetics
- Update UI labels and gradient colors in MainWindow.xaml
- Enhance UI elements: adjust button font size, modify window chrome thickness, and improve minimize/maximize button templates for better user experience
- Version bump
- Refactor toolbar layout by removing unused reflow logic and adjusting widths for better responsiveness
- Update MainWindow layout and add dynamic toolbar card reflow functionality
- Add runtime info display and update check functionality in MainWindow
- Enhance PCM processing by adding support for 24-bit audio and improving gain application logic
- Refactor code for consistent formatting in audio processing classes
- Added FLAC support
- Set consistent background and foreground colors for the RenameDialog text box
- Update CHANGELOG.md

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
