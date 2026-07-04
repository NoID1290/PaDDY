# WPF → WinUI 3 Migration Tasks

## Part 1: Deactivate OverlayEngine
- [x] Remove OverlayEngine from PaDDY.csproj (project ref, publish target, file exclusions)
- [x] Remove OverlayEngine from PaDDY.sln
- [x] Stub out overlay integration in MainWindow.xaml.cs

## Part 2: Project File & Dependency Updates
- [x] Update PaDDY.csproj (TFM, UseWinUI, WindowsAppSDK, resource build actions)
- [x] Update AssemblyInfo.cs (remove WPF ThemeInfo)
- [ ] Optionally align sub-project TFMs

## Part 3: UI & XAML Migration
- [/] App.xaml — namespace migration
- [ ] App.xaml.cs — OnStartup → OnLaunched, font loading, MessageBox
- [ ] AppTheme.xaml — namespace + WindowChrome style removal
- [ ] MainWindow.xaml — namespace, WindowChrome removal, control mapping
- [ ] MainWindow.xaml.cs — System.Windows → Microsoft.UI.Xaml, Dispatcher, events
- [ ] SettingsWindow.xaml + .cs
- [ ] AboutWindow.xaml + .cs
- [ ] CreditsWindow.xaml + .cs
- [ ] EffectsWindow.xaml + .cs
- [ ] AudioEditorWindow.xaml + .cs
- [ ] SplashWindow.xaml + .cs
- [ ] Controls/LoadingOverlay.xaml + .cs
- [ ] Controls/RecordingPadButton.xaml + .cs
- [ ] Controls/RenameDialog.xaml + .cs
- [ ] Controls/DeleteAllDialog.cs
- [ ] Controls/DragAdorner.cs
- [ ] Helpers/ThemeManager.cs
- [ ] Services/GlobalHotkeyService.cs
- [ ] Services/TrayIconService.cs

## Verification
- [ ] dotnet build compiles without errors
