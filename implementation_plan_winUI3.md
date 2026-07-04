# Migrate PaDDY from WPF to WinUI 3 & Deactivate OverlayEngine

Migrate the main PaDDY application from WPF (`net10.0-windows`) to WinUI 3 (`Microsoft.WindowsAppSDK`) while fully disabling the OverlayEngine to keep scope manageable. This unlocks modern DirectX 11/12 hardware acceleration and DirectComposition.

---

## User Review Required

> [!IMPORTANT]
> **This is a massive migration** touching every XAML file, every code-behind, the theme system, dialogs, controls, services, and the project infrastructure. The app has **~2,800 lines** in `MainWindow.xaml.cs` alone, plus 7 other Windows/Views, 4 custom controls, a theme manager with 12+ theme palettes, and WPF-specific services (tray icon via WinForms, global hotkeys via `HwndSource`).

> [!WARNING]
> **Breaking changes that will affect users:**
> - `WindowChrome` (custom title bars) does not exist in WinUI 3 — we must use `AppWindow.TitleBar` APIs instead.
> - `AllowsTransparency` (SplashWindow) is not supported — must use compositor-based transparency or a borderless window with `AppWindow`.
> - `DragAdorner` (WPF `Adorner` class) has no WinUI 3 equivalent — must be re-implemented with a `Popup` or overlay `Canvas`.
> - `NotifyIcon` (system tray) uses `System.Windows.Forms` — will need `H.NotifyIcon.WinUI` or a Win32 P/Invoke wrapper since `UseWindowsForms` is incompatible with WinUI 3.
> - `GlobalHotkeyService` uses `HwndSource` (WPF's Win32 message hook) — must switch to WinUI 3's `InputNonClientPointerSource` or raw Win32 `SetWindowsHookEx`.
> - WPF resource dictionaries use `pack://application:,,,/` URIs — WinUI 3 uses `ms-appx:///` URIs.
> - WPF's `Fonts.GetFontFamilies()` (pack URI) is not available — must load fonts via `Microsoft.UI.Xaml.Media.FontFamily` with `ms-appx:///` paths.
> - Animations using `System.Windows.Media.Animation` → `Microsoft.UI.Xaml.Media.Animation` (different storyboard/easing APIs).
> - `RoutedPropertyChangedEventArgs<double>` → WinUI uses `RangeBaseValueChangedEventArgs` for slider events.
> - `MessageBox.Show()` → WinUI 3 uses `ContentDialog` or Win32 `MessageBox`.

> [!CAUTION]
> **WinUI 3 does NOT support `UseWindowsForms`** — the `TrayIconService` (which depends on `System.Windows.Forms.NotifyIcon`) will fail to compile. We must either replace it with a WinUI-compatible tray icon solution or stub it out temporarily.

---

## Open Questions

> [!IMPORTANT]
> 1. **System Tray Icon**: The current `TrayIconService` uses `System.Windows.Forms.NotifyIcon`. WinUI 3 is not compatible with `UseWindowsForms`. Options:
>    - **(A)** Install `H.NotifyIcon.WinUI` NuGet package (mature, well-maintained).
>    - **(B)** Stub out tray icon functionality for now & re-implement later.
>    - **(C)** Use raw Win32 P/Invoke for `Shell_NotifyIcon`.
>    - **Recommend:** Option (B) — stub it out to minimize scope.
>
> 2. **Global Hotkey Service**: Currently uses WPF `HwndSource` to hook into the Win32 message pump. In WinUI 3, the `Window` doesn't expose `HwndSource` the same way, but we can use `Microsoft.UI.Xaml.Window` → `WinRT.Interop.WindowNative.GetWindowHandle()` to get the HWND, then subclass via `SetWindowLongPtr` or use `Win32.PInvoke`. Should we:
>    - **(A)** Migrate to use `WindowNative.GetWindowHandle()` + `PInvoke.SetWindowSubclass()` (keeps functionality).
>    - **(B)** Stub out global hotkeys for now.
>    - **Recommend:** Option (A) — it's a small, isolated change.
>
> 3. **Windows App SDK Version**: Which version to target?
>    - **(A)** `1.7.x` (latest stable as of mid-2026).
>    - **(B)** A specific version you have in mind.
>    - **Recommend:** Latest stable `1.7`.
>
> 4. **Target Framework Moniker**: WinUI 3 requires a Windows version suffix. Options:
>    - `net10.0-windows10.0.19041.0` (Windows 10 2004+ — broadest compatibility)
>    - `net10.0-windows10.0.22621.0` (Windows 11 22H2+ — access to newer APIs)
>    - **Recommend:** `net10.0-windows10.0.19041.0` for broadest reach.
>
> 5. **AudioProcessor sub-project**: It currently has `<UseWindowsForms>true</UseWindowsForms>` because vendored NAudio uses WinForms types (e.g., `NAudio.WinForms`). This is a **library project** and can keep `UseWindowsForms` since it doesn't conflict with the WinUI 3 app host. However, want to confirm: should we leave AudioProcessor's csproj unchanged?

---

## Proposed Changes

### Part 1: Deactivate OverlayEngine

#### [MODIFY] [PaDDY.csproj](file:///s:/VScodeProjects/Paddy-dev/PaDDY.csproj)
- **Remove** `<ProjectReference Include="OverlayEngine\OverlayEngine.csproj" />` (line 30).
- **Remove** the `OverlayEngine` exclusion in the `ExcludeAppDllsFromSingleFile` target (line 44).
- **Remove** the `OverlayEngine\**` file exclusion block (lines 59–63).

#### [MODIFY] [PaDDY.sln](file:///s:/VScodeProjects/Paddy-dev/PaDDY.sln)
- **Remove** the OverlayEngine project entry (line 10–11) and all its build configuration entries (lines 46–57).

#### [MODIFY] [MainWindow.xaml.cs](file:///s:/VScodeProjects/Paddy-dev/Views/MainWindow.xaml.cs)
- **Comment out** the 4 `using NoIDSoftwork.OverlayEngine.*` imports (lines 20, 22–24).
- **Remove/stub** the `_overlayEngine` field declaration (line 38) — replace with a comment.
- **Comment out** all `_overlayEngine.*` calls:
  - Event hookup at line 290 and unhook at line 2789.
  - `_overlayEngine.Initialize(...)`, `.AttachToProcess(...)`, `.Show()` (lines 292–297).
  - `_overlayEngine.Dispose()` (line 2790).
- **Stub out** these methods to no-ops:
  - `BuildOverlayOptions()` → return `null` or remove entirely.
  - `ApplyOverlayOptionsFromSettings()` → empty body.
  - `OverlayEngine_DiagnosticEvent()` → remove entirely.
  - `UpdateOverlayTarget()` → empty body.
- **Keep** the overlay UI event handlers (`OverlayEnabledCheck_Changed`, `OverlayOpacitySlider_ValueChanged`, `OverlayFpsSlider_ValueChanged`) but make them save settings only (no engine calls).
- **Keep** the overlay settings in `AppSettings.cs` (they're just serialized properties, no engine dependency).

---

### Part 2: Project File & Dependency Updates

#### [MODIFY] [PaDDY.csproj](file:///s:/VScodeProjects/Paddy-dev/PaDDY.csproj)
Changes to the PropertyGroup:
- `<TargetFramework>` → `net10.0-windows10.0.19041.0`
- **Remove** `<UseWPF>true</UseWPF>`
- **Remove** `<UseWindowsForms>true</UseWindowsForms>`
- **Add** `<UseWinUI>true</UseWinUI>`
- **Add** `<WindowsPackageType>None</WindowsPackageType>` (unpackaged desktop app — no MSIX required)
- **Add** `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` (to bundle the WinAppSDK runtime)

New NuGet reference:
- **Add** `<PackageReference Include="Microsoft.WindowsAppSDK" Version="1.7.250513003" />` (or latest stable)

Resource items:
- **Change** `<Resource Include="PaDDY.ico" />` and font `<Resource>` items from WPF `Resource` build action to `<Content>` with `CopyToOutputDirectory` (WinUI 3 uses content files, not WPF embedded resources).

#### [MODIFY] [AudioProcessor.csproj](file:///s:/VScodeProjects/Paddy-dev/AudioProcessor/AudioProcessor.csproj)
- **Keep** `<UseWindowsForms>true</UseWindowsForms>` — this is fine for a library project.
- Optionally update `<TargetFramework>` to `net10.0-windows10.0.19041.0` for TFM consistency.

#### [MODIFY] [EffectProcessor.csproj](file:///s:/VScodeProjects/Paddy-dev/EffectProcessor/EffectProcessor.csproj)
- Optionally update `<TargetFramework>` to match `net10.0-windows10.0.19041.0`.

---

### Part 3: UI & XAML Migration

This is the largest part. Every `.xaml` and `.xaml.cs` file needs updates.

#### [MODIFY] [App.xaml](file:///s:/VScodeProjects/Paddy-dev/App.xaml)
```xml
<!-- WPF (before) -->
<Application x:Class="PaDDY.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="clr-namespace:PaDDY">

<!-- WinUI 3 (after) -->
<Application x:Class="PaDDY.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:PaDDY">
```
- Change `xmlns:local` from `clr-namespace:PaDDY` to `using:PaDDY`.
- The main presentation namespace stays the same (WinUI 3 uses the same default xmlns).
- The `ResourceDictionary` / `MergedDictionaries` structure stays the same.

#### [MODIFY] [App.xaml.cs](file:///s:/VScodeProjects/Paddy-dev/App.xaml.cs)
Major changes:
- Replace `using System.Windows` → `using Microsoft.UI.Xaml`
- Replace `using System.Windows.Media` → `using Microsoft.UI.Xaml.Media`
- Base class: `Application` (WinUI) — no `WpfApplication` alias needed.
- **`OnStartup`** → WinUI 3 uses **`OnLaunched(LaunchActivatedEventArgs)`** instead.
- **`Startup(StartupEventArgs e)`** → Command-line args come from `Environment.GetCommandLineArgs()` (WinUI 3's `LaunchActivatedEventArgs` doesn't carry CLI args for unpackaged apps).
- **`MessageBox.Show()`** → Use `Windows.Win32.PInvoke.MessageBox` or a `ContentDialog` (ContentDialog needs a `XamlRoot`, which requires the window to exist first).
- **`Fonts.GetFontFamilies()`** → Replace with `new FontFamily("ms-appx:///Themes/Fonts/ari-w9500-condensed-display.ttf#FamilyName")`.
- **`OnExit`** → Override `OnLaunched` cleanup or subscribe to `Application.Current.Exit` event.
- **Window creation**: `MainWindow = new MainWindow();` stays conceptually the same, but `Application.Current.MainWindow` doesn't exist in WinUI 3 — store a reference manually.

#### [MODIFY] [AssemblyInfo.cs](file:///s:/VScodeProjects/Paddy-dev/AssemblyInfo.cs)
- **Remove** `using System.Windows;`
- **Remove** the `[assembly: ThemeInfo(...)]` attribute (WPF-only concept).
- Keep assembly version/metadata attributes.

---

#### XAML Files — Namespace Pattern (applies to all 9 XAML files)

| WPF | WinUI 3 |
|-----|---------|
| `xmlns:local="clr-namespace:PaDDY"` | `xmlns:local="using:PaDDY"` |
| `xmlns:controls="clr-namespace:PaDDY.Controls"` | `xmlns:controls="using:PaDDY.Controls"` |
| `xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"` | *(remove entirely — no WindowChrome)* |
| `<Window>` root element | `<Window>` (same tag, but it's `Microsoft.UI.Xaml.Window`) |
| `<WindowChrome>` block | *(remove — use `AppWindow.TitleBar` in code-behind)* |
| `<Window.Resources>` | `<Window.Resources>` *(same)* |

#### [MODIFY] [MainWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/MainWindow.xaml) (1167 lines)
- Update namespaces per pattern above.
- **Remove** `WindowChrome.WindowChrome` block (lines 16–21).
- **Remove** WPF-specific `Window` attributes that don't exist in WinUI 3: `Icon`, `MinWidth`/`MinHeight` (set in code-behind via AppWindow), `Background`/`Foreground`/`FontFamily` (apply via root Grid or page-level resources).
- The overlay config panel XAML (`OverlayConfigPanel`) — **keep as-is** (it's just UI controls; they don't depend on OverlayEngine types).
- Meter overlay rectangles (`MeterOverlayL`, `MeterOverlayR`, etc.) — **keep as-is** (visual-only, no engine dependency).

#### [MODIFY] [MainWindow.xaml.cs](file:///s:/VScodeProjects/Paddy-dev/Views/MainWindow.xaml.cs) (2797 lines)
- Replace all `System.Windows.*` usings with `Microsoft.UI.Xaml.*` equivalents.
- **`Window`** base class → `Microsoft.UI.Xaml.Window`.
- **`Dispatcher.Invoke()`** → `DispatcherQueue.TryEnqueue()`.
- **`SolidColorBrush`** / **`Color`** → `Microsoft.UI.Xaml.Media.SolidColorBrush` / `Windows.UI.Color`.
- **Slider `ValueChanged`** handler signature: `RoutedPropertyChangedEventArgs<double>` → `RangeBaseValueChangedEventArgs`.
- **`SelectionChangedEventArgs`** stays the same namespace (`Microsoft.UI.Xaml.Controls`).
- **`WindowState`** → WinUI 3 uses `AppWindow.Presenter` (e.g., `OverlappedPresenter.Maximize()`).
- **`this.Activate()`** stays the same.
- **`this.Hide()` / `this.Show()`** → `AppWindow.Hide()` / `.Show()` or set `Visible`.
- **`ShowInTaskbar`** → `OverlappedPresenter.IsAlwaysOnTop` / show/hide via presenter settings.
- **`Opacity`** → Set on the root content element, not the Window itself.

#### [MODIFY] [SettingsWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/SettingsWindow.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Views/SettingsWindow.xaml.cs)
- Same namespace migration pattern.
- Remove `WindowChrome`.
- `ResizeMode="NoResize"` → set via `OverlappedPresenter.IsResizable = false` in code-behind.
- `WindowStartupLocation="CenterOwner"` → manual positioning via `AppWindow.Move()`.

#### [MODIFY] [AboutWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/AboutWindow.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Views/AboutWindow.xaml.cs)
- Same namespace migration pattern.
- Same WindowChrome / positioning changes.

#### [MODIFY] [CreditsWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/CreditsWindow.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Views/CreditsWindow.xaml.cs)
- Same namespace migration pattern.

#### [MODIFY] [EffectsWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/EffectsWindow.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Views/EffectsWindow.xaml.cs)
- Same namespace migration pattern.

#### [MODIFY] [AudioEditorWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/AudioEditorWindow.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Views/AudioEditorWindow.xaml.cs)
- Same namespace migration pattern.
- **Large file** (69KB XAML, 62KB CS) — may have complex WPF-specific animations/events.

#### [MODIFY] [SplashWindow.xaml](file:///s:/VScodeProjects/Paddy-dev/Views/SplashWindow.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Views/SplashWindow.xaml.cs)
- `AllowsTransparency="True"` and `WindowStyle="None"` → Use `OverlappedPresenter` with no title bar; transparency via `SystemBackdrop` or compositor layer.
- `Topmost="True"` → `OverlappedPresenter.IsAlwaysOnTop = true`.
- `ShowInTaskbar="False"` → hide from taskbar via presenter.

#### [MODIFY] [AppTheme.xaml](file:///s:/VScodeProjects/Paddy-dev/Themes/AppTheme.xaml) (791 lines)
- Update root `ResourceDictionary` namespace: remove `shell` xmlns.
- **All WPF-specific styles** (e.g., styles targeting `shell:WindowChrome` attached properties, `SystemDropShadowChrome`, etc.) need removal or replacement.
- `SolidColorBrush`, `LinearGradientBrush`, `Style`, `ControlTemplate`, `Setter` — these exist in both WPF and WinUI 3 with the same syntax but different CLR types. The XAML markup is largely compatible.
- Any references to WPF-only controls (e.g., `RepeatButton` in ScrollBar templates from PresentationFramework) need mapping.

#### [MODIFY] [Controls/DeleteAllDialog.cs](file:///s:/VScodeProjects/Paddy-dev/Controls/DeleteAllDialog.cs)
- Currently extends `Window` and builds UI entirely in C# code.
- Migrate to extend `Microsoft.UI.Xaml.Window` or convert to a `ContentDialog` (more idiomatic in WinUI 3).
- Replace `DropShadowEffect` → WinUI 3's `Shadow` or `ThemeShadow`.
- Replace `SizeToContent` → manual sizing or `Grid` auto-sizing.

#### [MODIFY] [Controls/DragAdorner.cs](file:///s:/VScodeProjects/Paddy-dev/Controls/DragAdorner.cs)
- WPF `Adorner` class doesn't exist in WinUI 3.
- Replace with a `Popup` positioned near the cursor, or a `Canvas` overlay element.
- `VisualBrush` → `Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap` for snapshot rendering.

#### [MODIFY] [Controls/LoadingOverlay.xaml](file:///s:/VScodeProjects/Paddy-dev/Controls/LoadingOverlay.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Controls/LoadingOverlay.xaml.cs)
- Namespace migration.
- `UserControl` base class → `Microsoft.UI.Xaml.Controls.UserControl`.
- `Visibility.Visible/Collapsed` → same API in WinUI 3.

#### [MODIFY] [Controls/RecordingPadButton.xaml](file:///s:/VScodeProjects/Paddy-dev/Controls/RecordingPadButton.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Controls/RecordingPadButton.xaml.cs)
- Namespace migration.
- `DoubleAnimation` → `Microsoft.UI.Xaml.Media.Animation.DoubleAnimation`.
- `Storyboard.Begin()` API differences.
- `UserControl` base class change.

#### [MODIFY] [Controls/RenameDialog.xaml](file:///s:/VScodeProjects/Paddy-dev/Controls/RenameDialog.xaml) + [.cs](file:///s:/VScodeProjects/Paddy-dev/Controls/RenameDialog.cs)
- If it's a `Window`, convert to `ContentDialog`.
- Namespace migration.

#### [MODIFY] [Helpers/ThemeManager.cs](file:///s:/VScodeProjects/Paddy-dev/Helpers/ThemeManager.cs)
- Replace 8+ `System.Windows.*` usings with WinUI 3 equivalents.
- `Application.Current.Resources` → same in WinUI 3 (`Application.Current.Resources`).
- `ResourceDictionary` API is similar but uses different CLR types.
- `SolidColorBrush`, `Color`, `LinearGradientBrush` → `Microsoft.UI.Xaml.Media.*`.
- `ColorConverter.ConvertFromString()` → Parse hex manually or use `Microsoft.UI.ColorHelper`.
- `Fonts.GetFontFamilies()` → WinUI `FontFamily` constructor.

#### [MODIFY] [Services/GlobalHotkeyService.cs](file:///s:/VScodeProjects/Paddy-dev/Services/GlobalHotkeyService.cs)
- Replace `System.Windows.Interop.HwndSource` → get HWND via `WinRT.Interop.WindowNative.GetWindowHandle(window)` and subclass the window procedure via `SetWindowSubclass`.
- The `RegisterHotKey`/`UnregisterHotKey` P/Invoke calls stay the same.

#### [MODIFY] [Services/TrayIconService.cs](file:///s:/VScodeProjects/Paddy-dev/Services/TrayIconService.cs)
- **Stub out** (Option B from open questions) — `System.Windows.Forms.NotifyIcon` is not available without `UseWindowsForms`.
- Make methods no-op; add TODO for future re-implementation.

---

### File Summary

| Category | Files | Key Challenge |
|----------|-------|--------------|
| **Project/Build** | `PaDDY.csproj`, `PaDDY.sln`, `AssemblyInfo.cs` | SDK switch, TFM, resource build actions |
| **App Entry** | `App.xaml`, `App.xaml.cs` | `OnStartup` → `OnLaunched`, font loading |
| **Main UI** | `MainWindow.xaml` (1167 lines), `MainWindow.xaml.cs` (2797 lines) | Overlay stubbing, massive namespace migration, WindowChrome → AppWindow |
| **Secondary Windows** | 5 windows (Settings, About, Credits, Effects, AudioEditor, Splash) | WindowChrome removal, positioning, transparency |
| **Custom Controls** | `DeleteAllDialog`, `DragAdorner`, `LoadingOverlay`, `RecordingPadButton`, `RenameDialog` | Adorner system, modal dialogs, animations |
| **Theme System** | `AppTheme.xaml` (791 lines), `ThemeManager.cs` | Brush/Color API migration, WindowChrome styles |
| **Services** | `GlobalHotkeyService`, `TrayIconService` | HwndSource migration, WinForms removal |

---

## Verification Plan

### Automated Tests
```bash
# Build verification (must compile without errors)
dotnet build PaDDY.csproj -c Debug

# Restore + build full solution
dotnet build PaDDY.sln -c Debug
```

### Manual Verification
1. **App launches** without crashes — the main window appears.
2. **Custom title bar** renders correctly (using `AppWindow.TitleBar`).
3. **Theme system** works — all 12 themes apply correctly (colors, brushes).
4. **Audio capture** (microphone + loopback) still functions (AudioProcessor is unchanged).
5. **Recording pads** display and play audio.
6. **Overlay controls** are present in the UI but non-functional (graceful no-ops).
7. **Settings window** opens and saves preferences.
8. **Global hotkey** registers and triggers recording.
9. **System tray** icon is gracefully absent (no crash).
10. **Splash screen** appears on startup.
