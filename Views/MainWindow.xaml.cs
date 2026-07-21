using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Data.Sqlite;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NoIDSoftwork.AudioProcessor;
using NoIDSoftwork.OverlayEngine.Diagnostics;
using NoIDSoftwork.EffectProcessor;
using NoIDSoftwork.OverlayEngine.Configuration;
using NoIDSoftwork.OverlayEngine.Core;
using NoIDSoftwork.OverlayEngine.Models;
using PaDDY.Controls;
using PaDDY.Helpers;
using PaDDY.Models;
using PaDDY.Services;
using PaDDY.Views;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private readonly AudioCaptureService _captureService = new();
        private readonly GlobalHotkeyService _hotkeyService = new();
        private readonly IOverlayEngine _overlayEngine = new OverlayEngine();
        private RecordingStore _recordingStore = new();
        private readonly Dictionary<string, RecordingPadButton> _padCache = new();
        private AppSettings _settings = AppSettings.Load();
        private EffectSettings _effectSettings = EffectSettingsManager.Load();
        private IEffectChain _globalCaptureChain = EffectChainFactory.CreateGlobal();
        private readonly List<CaptureSourceMode> _captureSourceModes = new();
        private List<(string Id, string Name)> _loopbackDevices = new();
        private List<(uint ProcessId, string ProcessName)> _appLoopbackProcesses = new();
        private int _outputDeviceIndex = 0;
        private Services.TrayIconService? _trayIcon;
        private bool _forceExit;
        private PadPage? _activePadPage;
        private Services.SpeechRecognitionService? _speechService;
        private readonly LiveMicModulatorService _liveMicModulator = new();

        private TcpIpcServer? _ipcServer;
        private bool _isRecording;

        // ── Fullscreen state ───────────────────────────────────────────────────
        private bool _isFullscreen;
        private WindowState _preFullscreenWindowState;
        private WindowStyle _preFullscreenWindowStyle;
        private ResizeMode _preFullscreenResizeMode;
        private Rect _preFullscreenBounds;
        private double _preFullscreenChromeHeight;

        private SplashWindow? _splashWindow;

        public void ShowLoadingOverlay(string message = "Processing...")
        {
            Dispatcher.Invoke(() =>
            {
                _splashWindow?.UpdateMessage(message);
                MainLoadingOverlay.Show(message);
            });
        }

        public void HideLoadingOverlay()
        {
            Dispatcher.Invoke(() =>
            {
                MainLoadingOverlay.Hide();
                if (_splashWindow != null)
                {
                    var dispatcher = _splashWindow.Dispatcher;
                    dispatcher.Invoke(() =>
                    {
                        _splashWindow.Close();
                        System.Windows.Threading.Dispatcher.FromThread(System.Threading.Thread.CurrentThread)?.InvokeShutdown();
                    });
                    _splashWindow = null;

                    this.ShowInTaskbar = true;
                    this.Opacity = 1;
                    this.IsHitTestVisible = true;

                    if (!_startHiddenInTray)
                    {
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                    }
                }
            });
        }

        private void UpdateLoadingOverlayTheme()
        {
            try
            {
                var palette = Helpers.ThemeManager.GetPalette(_settings.Theme);
                if (palette != null)
                {
                    var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["AccentGreenBrush"]);
                    var secondary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["SubtleTextBrush"]);
                    var text = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["PrimaryTextBrush"]);
                    var bg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["WindowBgBrush"]);

                    MainLoadingOverlay.ApplyThemeColors(accent, secondary, text);
                    
                    // For MainLoadingOverlay (the solid one in MainWindow), we use a semi-transparent version of the theme background
                    MainLoadingOverlay.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, bg.R, bg.G, bg.B));
                }
            }
            catch
            {
                // Fallback gracefully on any conversion/loading error
            }
        }
        private bool _performanceMode;
        private bool _pauseAnimationsWhenUnfocused;
        private DateTime _lastInputMeterTick;
        private DateTime _lastOutputMeterTick;
        private DateTime _lastMonitorMeterTick;
        private static readonly SolidColorBrush PeakHotBrush = new(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush PeakColdBrush = new(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
        static MainWindow()
        {
            PeakHotBrush.Freeze();
            PeakColdBrush.Freeze();
        }

        private void SetInfoLabel(TextBlock label, string prefix, string value)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => SetInfoLabel(label, prefix, value));
                return;
            }
            label.Inlines.Clear();

            var runPrefix = new System.Windows.Documents.Run(prefix);
            runPrefix.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "SubtleTextBrush");

            var runValue = new System.Windows.Documents.Run(value);
            runValue.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "SecondaryTextBrush");

            label.Inlines.Add(runPrefix);
            label.Inlines.Add(runValue);
        }

        private bool _suppressSelectionEvents = true;
        private bool _inputMeterUpdatesEnabled;
        private RecordingPadButton? _hoveredPad;

        // Volume controls
        private float _outputVolume = 1.0f;
        private float _padListenVolume = 1.0f;

        // Peak hold state (input)
        private const double PeakThresholdDb = -1.0;
        private const double PeakHoldSeconds = 1.5;
        private const double MeterMinDb = -60.0;
        private DateTime _peakHoldTimeL = DateTime.MinValue;
        private DateTime _peakHoldTimeR = DateTime.MinValue;

        // Peak hold state (output)
        private DateTime _outputPeakHoldTimeL = DateTime.MinValue;
        private DateTime _outputPeakHoldTimeR = DateTime.MinValue;

        // Peak hold state (monitor)
        private DateTime _monitorPeakHoldTimeL = DateTime.MinValue;
        private DateTime _monitorPeakHoldTimeR = DateTime.MinValue;

        // Last known meter levels (linear)
        private double _lastRmsL;
        private double _lastRmsR;
        private double _lastOutputRmsL;
        private double _lastOutputRmsR;
        private double _lastMonitorRmsL;
        private double _lastMonitorRmsR;

        // Meter decay animation (input)
        private System.Windows.Threading.DispatcherTimer? _meterDecayTimer;
        private System.Windows.Threading.DispatcherTimer? _inputMeterResetTimer;
        private double _decayTargetL;
        private double _decayTargetR;
        private double _decayCurrentL;
        private double _decayCurrentR;
        private const int DecaySteps = 18; // ~288ms at 16ms/tick
        private int _decayStep;

        // Meter decay animation (output)
        private System.Windows.Threading.DispatcherTimer? _outputMeterDecayTimer;
        private double _outputDecayTargetL;
        private double _outputDecayTargetR;
        private double _outputDecayCurrentL;
        private double _outputDecayCurrentR;
        private int _outputDecayStep;
        private static readonly Uri ReleasesPageUri = new("https://github.com/NoID1290/PaDDY/releases");
        private const string ReleasesApiEndpoint = "https://api.github.com/repos/NoID1290/PaDDY/releases/latest";

        private bool _configPanelVisible = true;

        public MainWindow()
        {
            // Decide up-front whether we should start hidden in the tray. When we do,
            // open the window minimized, non-activated and off the taskbar BEFORE the
            // first paint so the OS never flashes a black/unpainted window on screen.
            _startHiddenInTray = _settings.StartMinimizedInTray &&
                                 (_settings.MinimizeToTray || _settings.CloseToTray);

            var splashReadyEvent = new System.Threading.ManualResetEvent(false);
            var splashThread = new System.Threading.Thread(() =>
            {
                _splashWindow = new SplashWindow();
                _splashWindow.Show();
                splashReadyEvent.Set();
                System.Windows.Threading.Dispatcher.Run();
            });
            splashThread.SetApartmentState(System.Threading.ApartmentState.STA);
            splashThread.IsBackground = true;
            splashThread.Start();
            splashReadyEvent.WaitOne(2000);

            if (_startHiddenInTray)
            {
                // Open minimized (and without stealing focus) so no black/unpainted
                // window flashes on screen, but keep the taskbar entry so the user can
                // still find and restore the app from the taskbar.
                ShowActivated = false;
                WindowState = WindowState.Minimized;
                _initialTrayMinimize = true;
                this.Opacity = 0; // Hide the main window while it loads
                this.IsHitTestVisible = false;
            }
            else
            {
                this.ShowActivated = false;
                this.WindowState = WindowState.Minimized;
                this.Opacity = 0; // Hide the main window while it loads
                this.IsHitTestVisible = false;
            }

            InitializeComponent();
            App.DebugModeChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    LiveMicBtn.Visibility = App.IsDebugMode ? Visibility.Visible : Visibility.Collapsed;
                });
            };
            LiveMicBtn.Visibility = App.IsDebugMode ? Visibility.Visible : Visibility.Collapsed;
            UpdateLoadingOverlayTheme();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            Activated += OnWindowActivated;
            Deactivated += OnWindowDeactivated;
            ThresholdCanvas.SizeChanged += (_, _) =>
            {
                UpdateThresholdMarker();
                Helpers.ThemeManager.UpdateMeterSkinSize(ThresholdCanvas.ActualWidth);
                UpdateInputMeterOverlaysLayout();
                UpdateOutputMeterOverlaysLayout();
            };
            ThresholdCanvasR.SizeChanged += (_, _) => UpdateThresholdMarker();
            this.PreviewKeyDown += OnPadHotKey;
            PadMonitorMeterHostL.SizeChanged += (_, _) => UpdateMonitorMeterOverlaysLayout();
            PadMonitorMeterHostR.SizeChanged += (_, _) => UpdateMonitorMeterOverlaysLayout();
            
            App.DebugModeChanged += () =>
            {
                OverlayConfigPanel.Visibility = App.IsDebugMode ? Visibility.Visible : Visibility.Collapsed;
            };
        }

        // ── Custom Window Chrome ───────────────────────────────────────────────
        private void ChromeMinimize_Click(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void ChromeMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
                ChromeMaxIcon.Text = "\uE922"; // Maximize (Segoe MDL2 Assets)
                ChromeMaxRestoreBtn.ToolTip = "Maximize";
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
                ChromeMaxIcon.Text = "\uE923"; // Restore (Segoe MDL2 Assets)
                ChromeMaxRestoreBtn.ToolTip = "Restore";
            }
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e)
            => SystemCommands.CloseWindow(this);

        // ── Fullscreen (F11) ──────────────────────────────────────────────────
        private void ChromeFullscreen_Click(object sender, RoutedEventArgs e)
            => ToggleFullscreen();

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
                ExitFullscreen();
            else
                EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (_isFullscreen) return;

            // Save current state for restoration
            _preFullscreenWindowState = WindowState;
            _preFullscreenWindowStyle = WindowStyle;
            _preFullscreenResizeMode = ResizeMode;
            _preFullscreenBounds = new Rect(Left, Top, Width, Height);

            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            _preFullscreenChromeHeight = chrome?.CaptionHeight ?? 60;

            // Must restore first if maximized, then set style, then maximize again.
            // This avoids the WPF bug where WindowStyle change doesn't take effect
            // while already maximized.
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            // Remove chrome caption so the title bar area becomes content space
            if (chrome != null)
                chrome.CaptionHeight = 0;

            WindowState = WindowState.Maximized;
            _isFullscreen = true;

            // Update maximize button icon to reflect state
            ChromeMaxIcon.Text = "\uE923"; // Restore icon
            ChromeMaxRestoreBtn.ToolTip = "Restore";

            // Update fullscreen button
            ChromeFullscreenIcon.Text = "\uE73F"; // Exit fullscreen icon
            ChromeFullscreenBtn.ToolTip = "Exit Fullscreen (F11)";
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;

            _isFullscreen = false;

            // Restore window chrome
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome != null)
                chrome.CaptionHeight = _preFullscreenChromeHeight;

            // Restore window style
            WindowState = WindowState.Normal;
            WindowStyle = _preFullscreenWindowStyle;
            ResizeMode = _preFullscreenResizeMode;

            // Restore position and size
            Left = _preFullscreenBounds.Left;
            Top = _preFullscreenBounds.Top;
            Width = _preFullscreenBounds.Width;
            Height = _preFullscreenBounds.Height;

            // Restore previous window state (e.g. if was maximized before)
            WindowState = _preFullscreenWindowState;

            // Update maximize button icon
            if (WindowState == WindowState.Maximized)
            {
                ChromeMaxIcon.Text = "\uE923"; // Restore icon
                ChromeMaxRestoreBtn.ToolTip = "Restore";
            }
            else
            {
                ChromeMaxIcon.Text = "\uE922"; // Maximize icon
                ChromeMaxRestoreBtn.ToolTip = "Maximize";
            }

            // Update fullscreen button
            ChromeFullscreenIcon.Text = "\uE740"; // Enter fullscreen icon
            ChromeFullscreenBtn.ToolTip = "Fullscreen (F11)";
        }

        private void ToggleConfigPanel_Click(object sender, RoutedEventArgs e)
        {
            _configPanelVisible = !_configPanelVisible;
            var target = _configPanelVisible ? 290.0 : 0.0;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(target,
                TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            ConfigPanelBorder.BeginAnimation(MaxHeightProperty, anim);
            ConfigToggleText.Text = _configPanelVisible ? "▲" : "▼";
        }

        private void OnPadHotKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // ── F11: toggle fullscreen ──────────────────────────────────────
            if (e.Key == Key.F11)
            {
                e.Handled = true;
                ToggleFullscreen();
                return;
            }

            // ── Escape: exit fullscreen ─────────────────────────────────────
            if (e.Key == Key.Escape && _isFullscreen)
            {
                e.Handled = true;
                ExitFullscreen();
                return;
            }

            var isD = e.Key == Key.D || (e.Key == Key.System && e.SystemKey == Key.D);
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt) && isD)
            {
                e.Handled = true;
                App.ToggleDebugMode();
                return;
            }

#if DEBUG
            // ── Debug only: Ctrl+Alt+O — force-show the overlay engine ──────────────
            // This bypasses the Enabled check and attaches to PaDDY itself so the
            // overlay is visible without needing a loopback process configured.
            var isO = e.Key == Key.O || (e.Key == Key.System && e.SystemKey == Key.O);
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt) && isO)
            {
                e.Handled = true;

                // 1. Initialise if engine has never been started
                if (_overlayEngine.State == OverlayEngineState.Created)
                {
                    _overlayEngine.Initialize(BuildOverlayOptions());
                    System.Diagnostics.Debug.WriteLine($"[Overlay:DBG] Initialized. State={_overlayEngine.State}");
                }

                // 2. Force Enabled=true — without this, Show() silently returns
                var forceOptions = BuildOverlayOptions();
                forceOptions.Enabled = true;
                _overlayEngine.UpdateOptions(forceOptions);
                System.Diagnostics.Debug.WriteLine($"[Overlay:DBG] Options forced Enabled=true. State={_overlayEngine.State}");

                // 3. Attach to the PaDDY process itself so bounds.Width > 0.
                //    Without a valid attached window the render loop always hides the overlay.
                uint selfPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                bool attached = _overlayEngine.AttachToProcess(selfPid);
                System.Diagnostics.Debug.WriteLine($"[Overlay:DBG] AttachToProcess(self={selfPid}) -> attached={attached}. State={_overlayEngine.State}");

                // 4. Push a clearly labelled debug frame
                _overlayEngine.UpdateFrame(new OverlayFrame
                {
                    Title = "[DEBUG] PaDDY Overlay",
                    Lines = new[]
                    {
                        "Force-shown via Ctrl+Alt+O",
                        $"Attached: {attached}  State: {_overlayEngine.State}"
                    }
                });

                // 5. Show — state after AttachToProcess is already Running when Enabled=true,
                //    so Show() is a belt-and-suspenders call but costs nothing.
                _overlayEngine.Show();
                System.Diagnostics.Debug.WriteLine($"[Overlay:DBG] Show() called. Final State={_overlayEngine.State}");
                return;
            }
#endif

            if (_hoveredPad == null) return;
            // Don't intercept when a text-entry control has keyboard focus
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                Keyboard.FocusedElement is System.Windows.Controls.ComboBox) return;
            if (e.Key == Key.E) { e.Handled = true; _hoveredPad.OpenAudioEditor(); }
            else if (e.Key == Key.R) { e.Handled = true; _hoveredPad.OpenRename(); }
        }

        // ── Startup ────────────────────────────────────────────────────────────
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ShowLoadingOverlay("Core starting up");
            // Yield to the UI thread so the loading overlay can actually render before we block it
            await Task.Delay(100);

            ShowLoadingOverlay("Warming up engine");
            await Task.Delay(3000);
            PopulateCaptureSourceModes();
            PopulateInputDevices();
            PopulateLoopbackDevices();
            PopulateAppLoopbackProcesses();
            PopulateOutputDevices();
            PopulateListenOutputDevices();
            PopulateRecordingModes();
            PopulateSortOrderCombo();

            ShowLoadingOverlay("Applying settings");
            await Task.Delay(500);
            ApplySettings();
            ShowLoadingOverlay("Cleaning up temp files");
            await Task.Delay(50);
            _recordingStore.CleanupInternalTempRecordings();
            _recordingStore.CleanupAllTempFiles();

            InitializePadPages();
            await PreloadAllPadsAsync();
            RecordingPadButton.SuppressEntranceAnimation++;
            LoadFavoritesFromStore();
            LoadNonFavoritesFromStore();
            RecordingPadButton.SuppressEntranceAnimation--;
            _suppressSelectionEvents = false;

            ShowLoadingOverlay("Initializing audio effects");
            await Task.Delay(400);
            _globalCaptureChain?.Reset();

            ShowLoadingOverlay("VST plugins startup");
            await Task.Delay(600);
            var currentSettings = AppSettings.Load();
            bool vstSettingsChanged = false;
            if (!string.IsNullOrEmpty(currentSettings.VstPluginPath) && !File.Exists(currentSettings.VstPluginPath))
            {
                currentSettings.VstPluginPath = string.Empty;
                vstSettingsChanged = true;
            }
            if (!string.IsNullOrEmpty(currentSettings.Vst3PluginPath) && !File.Exists(currentSettings.Vst3PluginPath) && !Directory.Exists(currentSettings.Vst3PluginPath))
            {
                currentSettings.Vst3PluginPath = string.Empty;
                vstSettingsChanged = true;
            }
            if (vstSettingsChanged) currentSettings.Save();

            _captureService.RmsLevelChanged += OnRmsChanged;
            _captureService.RecordingCompleted += OnRecordingCompleted;
            _captureService.RecordingStateChanged += OnRecordingStateChanged;
            _captureService.CodecCompatibilityWarning += OnCodecCompatibilityWarning;

            _overlayEngine.DiagnosticEvent += OverlayEngine_DiagnosticEvent;  // NOT READY YET! CAN BE CALL WITH DEV KEY BUT NEED TO BE UNCOMMENT

            ShowLoadingOverlay("Features starting");
            await Task.Delay(50);
            if (_settings.OverlayEnabled)
            {
                _overlayEngine.Initialize(BuildOverlayOptions());
                if (_settings.AppLoopbackProcessId != 0)
                {
                    _overlayEngine.AttachToProcess(_settings.AppLoopbackProcessId);
                    _overlayEngine.Show();
                }
            }

            RefreshOutputFormatInfo();
            RefreshInputFormatInfo();
            WhisperARTTStatus();
            Forget(RefreshStorageInfoAsync());

            // ── Auto-update / update check ──────────────────────────────────
            if (Services.UpdateService.HasPendingRestore())
            {
                // Post-update restart: restore the auto-backup
                ShowLoadingOverlay("Restoring your data...");
                await Task.Delay(100);
                var restoreService = new Services.UpdateService();
                restoreService.StatusChanged += msg => ShowLoadingOverlay(msg);
                restoreService.RestorePostUpdateBackup();
                await Task.Delay(500);
            }
            else if (_settings.AutoInstallUpdates)
            {
                // Auto-update: check → download → backup → install
                ShowLoadingOverlay("Checking for updates...");
                await Task.Delay(1000);
                var updateService = new Services.UpdateService(_settings.DownloadBetaUpdates);
                updateService.StatusChanged += msg => ShowLoadingOverlay(msg);
                updateService.DownloadProgressChanged += fraction =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _splashWindow?.UpdateProgress(fraction);
                        MainLoadingOverlay.ShowProgress(fraction);
                    });
                };

                var updateResult = await updateService.CheckForUpdateAsync();
                if (updateResult != null)
                {
                    ShowLoadingOverlay($"Downloading update v{updateResult.LatestVersion}...");
                    var installerPath = await updateService.DownloadInstallerAsync(
                        updateResult.InstallerDownloadUrl, updateResult.AssetSizeBytes);

                    if (installerPath != null)
                    {
                        // Hide progress bar before backup phase
                        Dispatcher.Invoke(() =>
                        {
                            _splashWindow?.HideProgress();
                            MainLoadingOverlay.HideProgress();
                        });

                        ShowLoadingOverlay("Backing up your data...");
                        await Task.Delay(100);
                        bool backupOk = updateService.CreatePreUpdateBackup();

                        if (backupOk)
                        {
                            // Launch installer and exit — this method does not return
                            updateService.LaunchInstallerAndExit(installerPath);
                            return; // App is shutting down
                        }
                        else
                        {
                            // Backup failed — skip update, continue normal startup
                            ShowLoadingOverlay("Backup failed — skipping update");
                            await Task.Delay(1500);
                        }
                    }
                    else
                    {
                        // Download failed — continue normal startup
                        ShowLoadingOverlay("Download failed — skipping update");
                        Dispatcher.Invoke(() =>
                        {
                            _splashWindow?.HideProgress();
                            MainLoadingOverlay.HideProgress();
                        });
                        await Task.Delay(1500);
                    }
                }
                // else: up-to-date, continue normal startup
            }
            else
            {
                // Passive update check (just shows the update notice link)
                ShowLoadingOverlay("Checking for new updates");
                await Task.Delay(50);
                _ = CheckForUpdateAsync();
            }

            ShowLoadingOverlay("Loading AR-STT model");
            await Task.Delay(50);
            try
            {
                _speechService = new Services.SpeechRecognitionService();
                await _speechService.PreloadModelAsync(_settings.SpeechModel, _settings.UseCudaForSpeech);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to preload Whisper model: {ex.Message}");
            }

            ShowLoadingOverlay("Starting services");
            await Task.Delay(50);
            InitializeTrayIcon();

            ShowLoadingOverlay("Starting third-party services");
            await Task.Delay(50);
            if (_settings.DiscordRichPresenceEnabled)
            {
                DiscordService.Instance.Initialize(true, _settings.DiscordClientId);
            }

            _ipcServer = new TcpIpcServer(12900);
            _ipcServer.MessageReceived += IpcServer_MessageReceived;
            _ipcServer.ConnectionCountChanged += (s, count) =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (count > 0)
                    {
                        StreamDeckStatusLabel.Visibility = Visibility.Visible;
                        SetInfoLabel(StreamDeckStatusLabel, "Stream Deck plugin: ", count == 1 ? "connected" : $"{count} clients");
                    }
                    else
                    {
                        StreamDeckStatusLabel.Visibility = Visibility.Collapsed;
                    }
                });
            };
            _ipcServer.Start();

            HideLoadingOverlay();

            // Register global hotkey
            _hotkeyService.Register(this, _settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
            _hotkeyService.HotkeyPressed += OnBufferHotkeyPressed;

            // If the app was launched by opening a .PADBACK file, prompt for restore.
            if (App.PendingRestoreFilePath != null)
            {
                await HandlePendingBackupRestore();
            }
        }

        /// <summary>
        /// Prompts the user for confirmation before restoring a .PADBACK file
        /// that was opened via file association. This is destructive — it replaces
        /// all current settings, effect presets, and recordings.
        /// </summary>
        private async Task HandlePendingBackupRestore()
        {
            string filePath = App.PendingRestoreFilePath!;
            string fileName = System.IO.Path.GetFileName(filePath);

            var result = System.Windows.MessageBox.Show(
                this,
                $"You are about to restore from a backup file:\n\n" +
                $"\"{fileName}\"\n\n" +
                $"⚠ This will ERASE and REPLACE all of the following:\n\n" +
                $"   • All your current recordings\n" +
                $"   • All application settings\n" +
                $"   • All effect presets\n\n" +
                $"This action cannot be undone.\n\n" +
                $"Do you want to continue?",
                "Restore Backup — PaDDY",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result != MessageBoxResult.Yes)
            {
                SetStatus("Backup restore cancelled.", "#FFFFC107");
                return;
            }

            ShowLoadingOverlay("Restoring backup...");
            await Task.Delay(50); // Let the overlay render

            try
            {
                PrepareRecordingDataRestore();

                var backupService = new BackupService();
                if (backupService.RestoreBackup(filePath))
                {
                    await ReloadRecordingDataFromDiskAsync();
                    System.Windows.MessageBox.Show(
                        this,
                        "Backup restored successfully.\nAll recordings and settings have been reloaded.",
                        "Restore Complete — PaDDY",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        this,
                        "Failed to restore backup.\nPlease ensure the file is a valid PaDDY backup.",
                        "Restore Failed — PaDDY",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            finally
            {
                HideLoadingOverlay();
            }
        }

        // When configured to start in the tray, the window is opened minimized in the
        // constructor to avoid a brief black/unpainted window flashing on screen while
        // still keeping a taskbar entry.
        private bool _startHiddenInTray;
        // Suppresses the automatic minimize-to-tray on the very first startup minimize
        // so the window remains visible in the taskbar.
        private bool _initialTrayMinimize;
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
        }

        // ── System tray ────────────────────────────────────────────────────────
        private void InitializeTrayIcon()
        {
            _trayIcon = new Services.TrayIconService("PaDDY");
            _trayIcon.ShowRequested += RestoreFromTray;
            _trayIcon.SettingsRequested += () =>
            {
                RestoreFromTray();
                SettingsButton_Click(this, new RoutedEventArgs());
            };
            _trayIcon.ExitRequested += () =>
            {
                _forceExit = true;
                Close();
            };
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Show();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                // Don't collapse the initial tray-start minimize into the tray; keep it
                // visible in the taskbar so the user can see the app is running.
                if (_initialTrayMinimize)
                {
                    _initialTrayMinimize = false;
                    return;
                }

                if (_settings.MinimizeToTray)
                {
                    Hide();
                }
            }
        }

        // ── Focus-based animation suspension ──────────────────────────────────

        private void OnWindowActivated(object? sender, EventArgs e)
        {
            if (_pauseAnimationsWhenUnfocused)
                SetAnimationsPaused(false);
        }

        private void OnWindowDeactivated(object? sender, EventArgs e)
        {
            if (_pauseAnimationsWhenUnfocused)
                SetAnimationsPaused(true);
        }

        /// <summary>
        /// Pauses or resumes all decorative animation rendering:
        /// meter decay DispatcherTimers and any running XAML Storyboards.
        /// Audio capture and recording are unaffected.
        /// </summary>
        private void SetAnimationsPaused(bool paused)
        {
            // ── Meter decay timers ──────────────────────────────────────────
            if (paused)
            {
                _meterDecayTimer?.Stop();
                _outputMeterDecayTimer?.Stop();
                _inputMeterResetTimer?.Stop();
            }
            else
            {
                // Only restart timers that were actually running (i.e. metering is active)
                if (_inputMeterUpdatesEnabled)
                {
                    if (_meterDecayTimer != null) _meterDecayTimer.Start();
                    if (_outputMeterDecayTimer != null) _outputMeterDecayTimer.Start();
                }
            }

            // ── XAML Storyboards (glow pulses, hover effects, etc.) ─────────
            // Walk the visual tree to find and pause/resume all ClockGroups
            // driven by Storyboards attached to UI elements.
            var oldTraceLevel = System.Diagnostics.PresentationTraceSources.AnimationSource.Switch.Level;
            try
            {
                System.Diagnostics.PresentationTraceSources.AnimationSource.Switch.Level = System.Diagnostics.SourceLevels.Error;
                PauseResumeStoryboards(this, paused);
            }
            finally
            {
                System.Diagnostics.PresentationTraceSources.AnimationSource.Switch.Level = oldTraceLevel;
            }
        }

        /// <summary>
        /// Recursively walks the visual tree from <paramref name="root"/> and
        /// pauses or resumes every active <see cref="System.Windows.Media.Animation.Storyboard"/>
        /// clock found on each element.
        /// </summary>
        private static void PauseResumeStoryboards(System.Windows.DependencyObject root, bool pause)
        {
            // Pause/resume Storyboards stored in the element's trigger collection.
            // Note: GetCurrentState(), Pause(), and Resume() all throw InvalidOperationException
            // when the storyboard has never been interactively applied to the element (e.g. a
            // hover animation on an element the user never hovered). A try/catch per-call is the
            // only safe guard — there is no pre-flight query that avoids the exception.
            if (root is System.Windows.FrameworkElement fe)
            {
                foreach (System.Windows.TriggerBase trigger in fe.Triggers)
                {
                    if (trigger is System.Windows.EventTrigger et)
                    {
                        foreach (System.Windows.TriggerAction action in et.Actions)
                        {
                            if (action is System.Windows.Media.Animation.BeginStoryboard bsb &&
                                bsb.Storyboard != null)
                            {
                                try
                                {
                                    if (pause) bsb.Storyboard.Pause(fe);
                                    else       bsb.Storyboard.Resume(fe);
                                }
                                catch (InvalidOperationException) { /* storyboard not yet started on this element */ }
                            }
                        }
                    }
                }
            }

            // Recurse into children
            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                try
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                    PauseResumeStoryboards(child, pause);
                }
                catch { /* child may be in a disconnected or unusual state */ }
            }
        }

        private void PopulateCaptureSourceModes()
        {
            CaptureSourceCombo.Items.Clear();
            _captureSourceModes.Clear();

            AddCaptureSourceMode(CaptureSourceMode.Microphone, "Mic/Line input");
            AddCaptureSourceMode(CaptureSourceMode.OutputLoopback, "Output loopback");
            AddCaptureSourceMode(CaptureSourceMode.AppLoopback, "App loopback");
        }

        private void AddCaptureSourceMode(CaptureSourceMode mode, string label)
        {
            _captureSourceModes.Add(mode);
            CaptureSourceCombo.Items.Add(label);
        }

        private void PopulateRecordingModes()
        {
            RecordingModeCombo.Items.Clear();
            RecordingModeCombo.Items.Add("Auto VAD");
            RecordingModeCombo.Items.Add("Adaptive VAD");
            RecordingModeCombo.Items.Add("Key Buffer");
        }

        // Mode combo maps to a (RecordingMode, DetectionAlgorithm) pair:
        //   0 = Auto VAD      → AutoVAD,  RMS (0)
        //   1 = Adaptive VAD  → AutoVAD,  Adaptive (1)
        //   2 = Key Buffer    → KeyBuffer
        private const int ModeComboKeyBufferIndex = 2;

        private static int ModeToComboIndex(int recordingMode, int detectionAlgorithm)
        {
            if (recordingMode == (int)AudioRecordingMode.KeyBuffer) return ModeComboKeyBufferIndex;
            return detectionAlgorithm == 1 ? 1 : 0;
        }

        private void ApplyModeComboIndex(int idx)
        {
            switch (idx)
            {
                case 1: // Adaptive VAD
                    _captureService.RecordingMode = AudioRecordingMode.AutoVAD;
                    _captureService.DetectionAlgorithm = 1;
                    _settings.RecordingMode = (int)AudioRecordingMode.AutoVAD;
                    _settings.DetectionAlgorithm = 1;
                    break;
                case ModeComboKeyBufferIndex: // Key Buffer
                    _captureService.RecordingMode = AudioRecordingMode.KeyBuffer;
                    _settings.RecordingMode = (int)AudioRecordingMode.KeyBuffer;
                    break;
                default: // Auto VAD
                    _captureService.RecordingMode = AudioRecordingMode.AutoVAD;
                    _captureService.DetectionAlgorithm = 0;
                    _settings.RecordingMode = (int)AudioRecordingMode.AutoVAD;
                    _settings.DetectionAlgorithm = 0;
                    break;
            }
        }

        private static readonly string[] SortOrderLabels =
        {
            "Newest first",
            "Oldest first",
            "Name A\u2192Z",
            "Name Z\u2192A",
            "Longest",
            "Shortest",
            "Custom (drag)"
        };

        private void PopulateSortOrderCombo()
        {
            SortOrderCombo.Items.Clear();
            foreach (var label in SortOrderLabels)
                SortOrderCombo.Items.Add(label);
            SortOrderCombo.SelectedIndex = Math.Clamp(_settings.PadSortOrder, 0, SortOrderLabels.Length - 1);
        }

        private void SortOrderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _settings.PadSortOrder = SortOrderCombo.SelectedIndex;
            _settings.Save();
            if (_settings.PadSortOrder != SortOrderCombo.SelectedIndex)
            {
                _settings.PadSortOrder = SortOrderCombo.SelectedIndex;
                _settings.Save();
            }
            SortPadPanel();
        }

        private void SortPadPanel()
        {
            var buttons = PadPanel.Children.OfType<RecordingPadButton>().ToList();
            if (buttons.Count < 2) return;

            IEnumerable<RecordingPadButton> sorted = _settings.PadSortOrder switch
            {
                1 => buttons.OrderBy(b => b.Entry?.CreatedAt ?? DateTime.MinValue),
                2 => buttons.OrderBy(b => b.Entry?.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                3 => buttons.OrderByDescending(b => b.Entry?.FileName ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                4 => buttons.OrderByDescending(b => b.Entry?.Duration ?? TimeSpan.Zero),
                5 => buttons.OrderBy(b => b.Entry?.Duration ?? TimeSpan.Zero),
                6 => buttons.OrderBy(b => b.Entry?.SortOrder ?? 0).ThenByDescending(b => b.Entry?.CreatedAt ?? DateTime.MinValue),
                _ => buttons.OrderByDescending(b => b.Entry?.CreatedAt ?? DateTime.MinValue) // 0 = Newest first
            };

            var sortedList = sorted.ToList();
            bool orderChanged = false;
            for (int i = 0; i < Math.Min(PadPanel.Children.Count, sortedList.Count); i++)
                if (PadPanel.Children[i] != sortedList[i]) { orderChanged = true; break; }

            if (!orderChanged && PadPanel.Children.Count == sortedList.Count) return;

            RecordingPadButton.SuppressEntranceAnimation++;
            PadPanel.Children.Clear();
            foreach (var btn in sortedList) PadPanel.Children.Add(btn);
            RecordingPadButton.SuppressEntranceAnimation--;
        }

        // ── Pad drag-and-drop (move between panels/pages + reorder) ───────────────

        private RecordingPadButton? _draggedPad;
        private Controls.DragAdorner? _dragAdorner;
        private System.Windows.Documents.AdornerLayer? _dragAdornerLayer;

        private static RecordingPadButton? GetDraggedPad(System.Windows.DragEventArgs e)
            => e.Data.GetDataPresent(RecordingPadButton.PadDragFormat)
                ? e.Data.GetData(RecordingPadButton.PadDragFormat) as RecordingPadButton
                : null;

        /// <summary>Sets up the floating ghost and dims the source pad when a drag begins.</summary>
        private void BeginPadDragVisual(RecordingPadButton pad)
        {
            _draggedPad = pad;
            _dragAdornerLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(MainRootGrid);
            if (_dragAdornerLayer != null)
            {
                _dragAdorner = new Controls.DragAdorner(MainRootGrid, pad, pad.DragGrabOffset);
                _dragAdornerLayer.Add(_dragAdorner);
            }
            pad.Opacity = 0.35;
        }

        /// <summary>Tears down the ghost and commits the pad's final location after the drag loop ends.</summary>
        private void FinalizePadDrop(RecordingPadButton pad)
        {
            if (_dragAdorner != null && _dragAdornerLayer != null)
                _dragAdornerLayer.Remove(_dragAdorner);
            _dragAdorner = null;
            _dragAdornerLayer = null;
            pad.Opacity = 1.0;
            _draggedPad = null;

            if (pad.Entry == null) { UpdatePadState(); return; }

            var parent = pad.Parent;
            if (ReferenceEquals(parent, FavoritesPanel))
            {
                if (!pad.IsFavorite)
                {
                    pad.IsFavorite = true;
                    pad.Entry.IsFavorite = true;
                    _recordingStore.SetFavorite(pad.Entry.RecordingId, true);
                    string pageId = _activePadPage != null && !_activePadPage.IsFavorites ? _activePadPage.Id : string.Empty;
                    pad.Entry.PadPage = pageId;
                    _recordingStore.SetPadPage(pad.Entry.RecordingId, pageId);
                }
                PersistFavoritesOrder();
            }
            else if (ReferenceEquals(parent, PadPanel))
            {
                if (pad.IsFavorite)
                {
                    pad.IsFavorite = false;
                    pad.Entry.IsFavorite = false;
                    _recordingStore.SetFavorite(pad.Entry.RecordingId, false);
                    pad.Entry.PadPage = string.Empty;
                    _recordingStore.SetPadPage(pad.Entry.RecordingId, string.Empty);
                }
                SwitchToCustomSort();
                PersistRecordingsOrder();
                EnforceMaxRecords();
            }
            // Otherwise the pad was moved to another page (detached) and already committed.

            UpdatePadState();
        }

        private void FavoritesPanel_DragOver(object sender, System.Windows.DragEventArgs e)
            => HandlePanelDragOver(FavoritesPanel, e);

        private void PadPanel_DragOver(object sender, System.Windows.DragEventArgs e)
            => HandlePanelDragOver(PadPanel, e);

        private DateTime _lastDragOverUpdate = DateTime.MinValue;
        /// <summary>
        /// Live-preview drag: moves the dragged pad to the hovered slot in real time so the
        /// user sees it physically slide into place, and keeps the floating ghost under the cursor.
        /// </summary>
        private void HandlePanelDragOver(System.Windows.Controls.Panel panel, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                Window_DragOver(sender: panel, e);
                return;
            }

            var pad = GetDraggedPad(e);
            e.Effects = pad != null ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
            e.Handled = true;
            if (pad == null) return;

            UpdateDragAdorner(e);

            // Throttle layout-heavy preview moves to prevent UI freeze
            var now = DateTime.UtcNow;
            if ((now - _lastDragOverUpdate).TotalMilliseconds < 16) return;
            _lastDragOverUpdate = now;

            int index = ComputeDropIndex(panel, e, pad);
            LivePreviewMove(panel, pad, index);
        }

        private void FavoritesPanel_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                Window_Drop(sender, e);
                return;
            }
            // The pad has already been live-moved into place; commit happens in FinalizePadDrop.
            e.Handled = true;
        }

        private void PadPanel_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                Window_Drop(sender, e);
                return;
            }
            e.Handled = true;
        }

        private void UpdateDragAdorner(System.Windows.DragEventArgs e)
        {
            if (_dragAdorner != null)
                _dragAdorner.UpdatePosition(e.GetPosition(MainRootGrid));
        }

        /// <summary>Computes the target child index for a drop, ignoring the dragged pad itself.</summary>
        private static int ComputeDropIndex(System.Windows.Controls.Panel panel, System.Windows.DragEventArgs e, RecordingPadButton dragged)
        {
            var pos = e.GetPosition(panel);
            int visibleIndex = 0;
            for (int i = 0; i < panel.Children.Count; i++)
            {
                if (panel.Children[i] is not FrameworkElement fe) continue;
                if (ReferenceEquals(fe, dragged)) continue;

                var topLeft = fe.TranslatePoint(new System.Windows.Point(0, 0), panel);
                double midX = topLeft.X + fe.ActualWidth / 2;
                double bottom = topLeft.Y + fe.ActualHeight;
                if (pos.Y < topLeft.Y) return visibleIndex;            // pointer above this row
                if (pos.Y <= bottom && pos.X < midX) return visibleIndex; // same row, left half
                visibleIndex++;
            }
            return visibleIndex;
        }

        /// <summary>Moves the dragged pad to <paramref name="targetIndex"/> within <paramref name="target"/> (cross-panel aware).</summary>
        private static void LivePreviewMove(System.Windows.Controls.Panel target, RecordingPadButton pad, int targetIndex)
        {
            var current = pad.Parent as System.Windows.Controls.Panel;
            if (ReferenceEquals(current, target))
            {
                int cur = target.Children.IndexOf(pad);
                if (cur < 0) return;
                // targetIndex was computed ignoring the dragged pad; translate to a real insert index.
                int insert = targetIndex;
                if (insert > cur) { /* slots after removal shift left */ }
                insert = Math.Clamp(insert, 0, target.Children.Count - 1);
                if (insert == cur) return;
                target.Children.RemoveAt(cur);
                target.Children.Insert(insert, pad);
            }
            else
            {
                current?.Children.Remove(pad);
                targetIndex = Math.Clamp(targetIndex, 0, target.Children.Count);
                target.Children.Insert(targetIndex, pad);
            }
        }

        private void MovePadToPage(RecordingPadButton pad, string pageId)
        {
            if (pad.Entry == null) return;

            var page = _settings.PadPages.FirstOrDefault(p => p.Id == pageId);
            bool toFavoritesPage = page != null && page.IsFavorites;

            pad.IsFavorite = true;
            pad.Entry.IsFavorite = true;
            string targetPage = toFavoritesPage ? string.Empty : pageId;
            pad.Entry.PadPage = targetPage;
            _recordingStore.SetFavorite(pad.Entry.RecordingId, true);
            _recordingStore.SetPadPage(pad.Entry.RecordingId, targetPage);

            // The pad now belongs to another page; remove it from the current view.
            (pad.Parent as System.Windows.Controls.Panel)?.Children.Remove(pad);
            PersistFavoritesOrder();
            UpdatePadState();

            if (_activePadPage != null && pageId == _activePadPage.Id)
            {
                ReloadFavoritesPanel();
            }
        }

        private void PersistFavoritesOrder()
        {
            var ids = FavoritesPanel.Children.OfType<RecordingPadButton>()
                .Where(b => b.Entry != null)
                .Select(b => b.Entry!.RecordingId)
                .ToList();
            _recordingStore.SetSortOrders(ids);
            for (int i = 0; i < FavoritesPanel.Children.Count; i++)
                if (FavoritesPanel.Children[i] is RecordingPadButton b && b.Entry != null)
                    b.Entry.SortOrder = i;
        }

        private void PersistRecordingsOrder()
        {
            var ids = PadPanel.Children.OfType<RecordingPadButton>()
                .Where(b => b.Entry != null)
                .Select(b => b.Entry!.RecordingId)
                .ToList();
            _recordingStore.SetSortOrders(ids);
            for (int i = 0; i < PadPanel.Children.Count; i++)
                if (PadPanel.Children[i] is RecordingPadButton b && b.Entry != null)
                    b.Entry.SortOrder = i;
        }

        private void SwitchToCustomSort()
        {
            int customIndex = SortOrderLabels.Length - 1;
            if (_settings.PadSortOrder == customIndex) return;
            _settings.PadSortOrder = customIndex;
            _settings.Save();
            _suppressSelectionEvents = true;
            SortOrderCombo.SelectedIndex = customIndex;
            _suppressSelectionEvents = false;
        }

        private void PopulateInputDevices()
        {
            var devices = AudioCaptureService.GetInputDevices();
            InputDeviceCombo.Items.Clear();
            if (devices.Count == 0)
            {
                InputDeviceCombo.Items.Add("No microphones found");
                InputDeviceCombo.SelectedIndex = 0;
                return;
            }
            foreach (var d in devices)
                InputDeviceCombo.Items.Add(d.Name);

            InputDeviceCombo.SelectedIndex =
                Math.Clamp(_settings.InputDeviceIndex, 0, devices.Count - 1);
        }

        private void PopulateOutputDevices()
        {
            OutputDeviceCombo.Items.Clear();
            OutputDeviceCombo.Items.Add("Default Output");

            using (var enumerator = new MMDeviceEnumerator())
            {
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                    OutputDeviceCombo.Items.Add(device.FriendlyName);
            }

            int clampedOut = Math.Clamp(_settings.OutputDeviceIndex, 0,
                OutputDeviceCombo.Items.Count - 1);
            OutputDeviceCombo.SelectedIndex = clampedOut;
            _outputDeviceIndex = clampedOut - 1;
        }

        private void PopulateListenOutputDevices()
        {
            ListenOutputDeviceCombo.Items.Clear();
            ListenOutputDeviceCombo.Items.Add("Default Output");

            using (var enumerator = new MMDeviceEnumerator())
            {
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                    ListenOutputDeviceCombo.Items.Add(device.FriendlyName);
            }

            int clamped = Math.Clamp(_settings.ListenOutputDeviceIndex, 0,
                ListenOutputDeviceCombo.Items.Count - 1);
            ListenOutputDeviceCombo.SelectedIndex = clamped;
            ListenOutputDeviceCombo.IsEnabled = _settings.ListenOutputEnabled;
            ListenOutputDeviceCombo.Opacity = _settings.ListenOutputEnabled ? 1.0 : 0.4;
        }

        private void PopulateLoopbackDevices()
        {
            _loopbackDevices = AudioCaptureService.GetLoopbackDevices();
            LoopbackDeviceCombo.Items.Clear();

            foreach (var d in _loopbackDevices)
                LoopbackDeviceCombo.Items.Add(d.Name);

            if (_loopbackDevices.Count > 0)
            {
                int idx = _loopbackDevices.FindIndex(d => d.Id == _settings.LoopbackDeviceId);
                LoopbackDeviceCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
            else
            {
                LoopbackDeviceCombo.Items.Add("No output devices found");
                LoopbackDeviceCombo.SelectedIndex = 0;
            }
        }

        private void PopulateAppLoopbackProcesses()
        {
            _appLoopbackProcesses = AudioSessionHelper.GetAudioProcesses();
            AppLoopbackCombo.Items.Clear();

            if (_appLoopbackProcesses.Count == 0)
            {
                AppLoopbackCombo.Items.Add("No apps producing audio");
                AppLoopbackCombo.SelectedIndex = 0;
                return;
            }

            foreach (var p in _appLoopbackProcesses)
                AppLoopbackCombo.Items.Add(p.ProcessName);

            int idx = _appLoopbackProcesses.FindIndex(p => p.ProcessId == _settings.AppLoopbackProcessId);
            AppLoopbackCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void ApplySettings()
        {
            var requestedMode = (CaptureSourceMode)Math.Clamp(_settings.CaptureSourceMode, 0, 2);
            if (!_captureSourceModes.Contains(requestedMode))
                requestedMode = _captureSourceModes.Contains(CaptureSourceMode.OutputLoopback)
                    ? CaptureSourceMode.OutputLoopback
                    : CaptureSourceMode.Microphone;

            CaptureSourceCombo.SelectedIndex = _captureSourceModes.IndexOf(requestedMode);
            if (_settings.CaptureSourceMode != (int)requestedMode)
            {
                _settings.CaptureSourceMode = (int)requestedMode;
                _settings.Save();
            }

            ListenOutputEnabledCheck.IsChecked = _settings.ListenOutputEnabled;

            SensitivitySlider.Value = _settings.Sensitivity;
            SilenceSlider.Value = _settings.SilenceTimeoutMs;

            // Recording mode combo
            int modeIdx = ModeToComboIndex(_settings.RecordingMode, _settings.DetectionAlgorithm);
            RecordingModeCombo.SelectedIndex = modeIdx;
            ApplyModeComboIndex(modeIdx);
            KeyBufferHint.Visibility = modeIdx == ModeComboKeyBufferIndex ? Visibility.Visible : Visibility.Collapsed;
            UpdateVadSettingsVisibility(modeIdx);
            UpdateHotkeyLabel();

            // Format settings
            _captureService.RecordSampleRate = _settings.RecordSampleRate;
            _captureService.RecordBitDepth = _settings.RecordBitDepth;
            _captureService.RecordChannels = _settings.RecordChannels;
            _captureService.RecordCodec = _settings.RecordCodec;
            _captureService.PastBufferDurationMs = _settings.PastBufferDurationMs;

            _captureService.SaveFolder = RecordingStore.InternalTempRecDir;

            _captureService.Sensitivity = _settings.Sensitivity;
            _captureService.SilenceTimeoutMs = _settings.SilenceTimeoutMs;
            _captureService.DetectionAlgorithm = _settings.DetectionAlgorithm;

            _performanceMode = _settings.PerformanceMode;
            _pauseAnimationsWhenUnfocused = _settings.PauseAnimationsWhenUnfocused;

            // Apply saved global effect config to the live chain and assign to capture service
            EffectSettingsManager.ApplyConfig(_globalCaptureChain, _effectSettings.GlobalChain);
            _captureService.CaptureEffectChain = _globalCaptureChain;

            // Volume settings
            InputVolumeSlider.Value = _settings.InputVolume;
            OutputVolumeSlider.Value = _settings.OutputVolume;
            PadListenVolumeSlider.Value = _settings.PadListenVolume;
            _captureService.InputGain = (float)(_settings.InputVolume / 100.0);
            _outputVolume = (float)(_settings.OutputVolume / 100.0);
            _padListenVolume = (float)(_settings.PadListenVolume / 100.0);

            UpdateInputControlsForSource();
            ListenOutputDeviceCombo.IsEnabled = _settings.ListenOutputEnabled;
            ListenOutputDeviceCombo.Opacity = _settings.ListenOutputEnabled ? 1.0 : 0.4;

            OverlayEnabledCheck.IsChecked = _settings.OverlayEnabled;
            OverlayOpacitySlider.Value = Math.Clamp(_settings.OverlayOpacity * 100.0, 20, 100);
            OverlayFpsSlider.Value = Math.Clamp(_settings.OverlayFrameRateCap, 30, 240);

            UpdatePadMonitorMeterAvailability();
            RefreshPadOutputRouting();
            RefreshOutputFormatInfo();
            RefreshInputFormatInfo();

            ApplyOverlayOptionsFromSettings();

            // Initialize Discord Service
            DiscordService.Instance.Initialize(_settings.DiscordRichPresenceEnabled, _settings.DiscordClientId);
        }

        private OverlayOptions BuildOverlayOptions()
        {
            return new OverlayOptions
            {
                Enabled = _settings.OverlayEnabled,
                FrameRateCap = Math.Clamp(_settings.OverlayFrameRateCap, 30, 240),
                VisualStyle = new OverlayVisualStyle
                {
                    Opacity = Math.Clamp(_settings.OverlayOpacity, 0.2, 1.0),
                    AccentColorHex = "#FF4CAF50",
                    PrimaryColorHex = "#FFFFFFFF",
                    FontFamily = "Segoe UI",
                    FontSize = 18f
                }
            };
        }

        private void ApplyOverlayOptionsFromSettings()
        {
            if (_settings.OverlayEnabled && _overlayEngine.State == OverlayEngineState.Created)
            {
                _overlayEngine.Initialize(BuildOverlayOptions());
            }

            if (_overlayEngine.State == OverlayEngineState.Created || _overlayEngine.State == OverlayEngineState.Disposed)
            {
                return;
            }

            _overlayEngine.UpdateOptions(BuildOverlayOptions());
            if (!_settings.OverlayEnabled)
            {
                _overlayEngine.Hide();
                return;
            }

            if (_settings.AppLoopbackProcessId != 0)
            {
                UpdateOverlayTarget(_settings.AppLoopbackProcessId);
            }
        }

        private void OverlayEngine_DiagnosticEvent(object? sender, OverlayDiagnosticEvent e)
        {
            Debug.WriteLine($"[Overlay:{e.Level}] {e.Category}: {e.Message}");
            if (e.Exception != null)
            {
                Debug.WriteLine(e.Exception);
            }
        }

        private void UpdateHotkeyLabel()
        {
            HotkeyLabel.Text = KeyHelper.FormatHotkey(_settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
        }

        private int GetCurrentListenDeviceIndex()
        {
            if (ListenOutputEnabledCheck.IsChecked == true)
                return ListenOutputDeviceCombo.SelectedIndex - 1;
            return -2;
        }

        private void RefreshPadOutputRouting()
        {
            int listenDevice = GetCurrentListenDeviceIndex();
            foreach (var panel in new[] { PadPanel, FavoritesPanel })
            {
                foreach (var child in panel.Children)
                {
                    if (child is RecordingPadButton pad)
                    {
                        pad.OutputDeviceIndex = _outputDeviceIndex;
                        pad.ListenDeviceIndex = listenDevice;
                        pad.TrimEditorOutputDeviceIndex = _settings.TrimEditorOutputDeviceIndex;
                        pad.OutputVolume = _outputVolume;
                        pad.ListenVolume = _padListenVolume;
                        pad.RefreshLiveVolumes();
                    }
                }
            }
        }

        private CaptureSourceMode GetSelectedCaptureMode()
        {
            int index = CaptureSourceCombo.SelectedIndex;
            if (index >= 0 && index < _captureSourceModes.Count)
                return _captureSourceModes[index];

            return CaptureSourceMode.Microphone;
        }

        private string? GetSelectedLoopbackDeviceId()
        {
            int index = LoopbackDeviceCombo.SelectedIndex;
            if (index < 0 || index >= _loopbackDevices.Count) return null;
            return _loopbackDevices[index].Id;
        }

        private void UpdateInputControlsForSource()
        {
            var mode = GetSelectedCaptureMode();
            bool useMic = mode == CaptureSourceMode.Microphone;
            bool useLoopback = mode == CaptureSourceMode.OutputLoopback;
            bool useApp = mode == CaptureSourceMode.AppLoopback;

            InputDeviceLabel.Visibility = useMic ? Visibility.Visible : Visibility.Collapsed;
            InputDeviceCombo.Visibility = useMic ? Visibility.Visible : Visibility.Collapsed;
            LoopbackDeviceLabel.Visibility = useLoopback ? Visibility.Visible : Visibility.Collapsed;
            LoopbackDeviceCombo.Visibility = useLoopback ? Visibility.Visible : Visibility.Collapsed;
            AppLoopbackLabel.Visibility = useApp ? Visibility.Visible : Visibility.Collapsed;
            AppLoopbackCombo.Visibility = useApp ? Visibility.Visible : Visibility.Collapsed;
            RefreshAppLoopbackBtn.Visibility = useApp ? Visibility.Visible : Visibility.Collapsed;

            if (useApp)
                PopulateAppLoopbackProcesses();

            RefreshInputFormatInfo();
        }

        private void RestartMonitoringIfActive()
        {
            if (MonitorToggle.IsChecked != true) return;
            _inputMeterUpdatesEnabled = false;
            _captureService.Stop();
            StartMonitoringWithCurrentSelection();
            _inputMeterUpdatesEnabled = true;
        }

        private void StartMonitoringWithCurrentSelection()
        {
            var mode = GetSelectedCaptureMode();
            if (mode == CaptureSourceMode.Microphone)
            {
                _captureService.Start(Math.Max(0, InputDeviceCombo.SelectedIndex), mode, null);
                SetStatus("Listening…", "#FF4CAF50");
                RefreshInputFormatInfo();
                return;
            }

            if (mode == CaptureSourceMode.AppLoopback)
            {
                if (_appLoopbackProcesses.Count == 0)
                    throw new InvalidOperationException("No apps producing audio to capture.");

                int idx = AppLoopbackCombo.SelectedIndex;
                if (idx < 0 || idx >= _appLoopbackProcesses.Count)
                    throw new InvalidOperationException("No app selected for loopback capture.");

                _captureService.AppLoopbackProcessId = _appLoopbackProcesses[idx].ProcessId;
                UpdateOverlayTarget(_appLoopbackProcesses[idx].ProcessId);
                _captureService.Start(0, mode, null);
                SetStatus($"Monitoring app: {_appLoopbackProcesses[idx].ProcessName}…", "#FF4CAF50");
                RefreshInputFormatInfo();
                return;
            }

            if (_loopbackDevices.Count == 0)
                throw new InvalidOperationException("No active output devices available for loopback capture.");

            _captureService.Start(0, mode, GetSelectedLoopbackDeviceId());
            SetStatus("Monitoring output loopback…", "#FF4CAF50");
            RefreshInputFormatInfo();
        }

        // ── Device selection ───────────────────────────────────────────────────
        private void InputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _settings.InputDeviceIndex = InputDeviceCombo.SelectedIndex;
            _settings.Save();
            RefreshInputFormatInfo();
            RestartMonitoringIfActive();
        }

        private void CaptureSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;

            _settings.CaptureSourceMode = CaptureSourceCombo.SelectedIndex;
            _settings.Save();

            UpdateInputControlsForSource();
            RestartMonitoringIfActive();
        }

        private void LoopbackDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;

            _settings.LoopbackDeviceId = GetSelectedLoopbackDeviceId() ?? string.Empty;
            _settings.Save();
            RefreshInputFormatInfo();
            RestartMonitoringIfActive();
        }

        private void AppLoopbackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;

            int idx = AppLoopbackCombo.SelectedIndex;
            if (idx >= 0 && idx < _appLoopbackProcesses.Count)
            {
                _settings.AppLoopbackProcessId = _appLoopbackProcesses[idx].ProcessId;
                _settings.Save();
                UpdateOverlayTarget(_settings.AppLoopbackProcessId);
            }
            RefreshInputFormatInfo();
            RestartMonitoringIfActive();
        }

        private void UpdateOverlayTarget(uint processId)
        {
            if (!_settings.OverlayEnabled)
            {
                if (_overlayEngine.State != OverlayEngineState.Created && _overlayEngine.State != OverlayEngineState.Disposed)
                {
                    _overlayEngine.Hide();
                    _overlayEngine.Detach();
                }
                return;
            }

            if (processId == 0)
            {
                if (_overlayEngine.State != OverlayEngineState.Created && _overlayEngine.State != OverlayEngineState.Disposed)
                {
                    _overlayEngine.Hide();
                    _overlayEngine.Detach();
                }
                return;
            }

            if (_overlayEngine.State == OverlayEngineState.Created)
            {
                _overlayEngine.Initialize(BuildOverlayOptions());
            }

            if (_overlayEngine.AttachToProcess(processId))
            {
                string processName = _appLoopbackProcesses.FirstOrDefault(p => p.ProcessId == processId).ProcessName;
                if (string.IsNullOrWhiteSpace(processName))
                {
                    processName = $"PID {processId}";
                }

                _overlayEngine.UpdateFrame(new OverlayFrame
                {
                    Title = "PaDDY",
                    Lines = new[]
                    {
                        $"Tracking: {processName}",
                        "Press monitor hotkey to capture"
                    }
                });
                _overlayEngine.Show();
            }
            else
            {
                _overlayEngine.Hide();
            }
        }

        private void OverlayEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (OverlayEnabledCheck == null)
            {
                return;
            }

            _settings.OverlayEnabled = OverlayEnabledCheck.IsChecked == true;
            _settings.Save();
            ApplyOverlayOptionsFromSettings();
        }

        private void OverlayOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OverlayOpacityValueLabel == null)
            {
                return;
            }

            int pct = (int)Math.Round(e.NewValue);
            OverlayOpacityValueLabel.Text = $"{pct}%";
            _settings.OverlayOpacity = pct / 100.0;
            _settings.Save();
            ApplyOverlayOptionsFromSettings();
        }

        private void OverlayFpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (OverlayFpsValueLabel == null)
            {
                return;
            }

            int fps = (int)Math.Round(e.NewValue);
            OverlayFpsValueLabel.Text = fps.ToString();
            _settings.OverlayFrameRateCap = fps;
            _settings.Save();
            ApplyOverlayOptionsFromSettings();
        }

        private void RefreshAppLoopback_Click(object sender, RoutedEventArgs e)
        {
            PopulateAppLoopbackProcesses();
            RefreshInputFormatInfo();
        }

        private void OutputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _outputDeviceIndex = OutputDeviceCombo.SelectedIndex - 1;
            _settings.OutputDeviceIndex = OutputDeviceCombo.SelectedIndex;
            _settings.Save();
            RefreshPadOutputRouting();
        }

        private void ListenOutputEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _settings.ListenOutputEnabled = ListenOutputEnabledCheck.IsChecked == true;
            _settings.Save();
            ListenOutputDeviceCombo.IsEnabled = _settings.ListenOutputEnabled;
            ListenOutputDeviceCombo.Opacity = _settings.ListenOutputEnabled ? 1.0 : 0.4;
            UpdatePadMonitorMeterAvailability();
            RefreshPadOutputRouting();
        }

        private void UpdatePadMonitorMeterAvailability()
        {
            bool enabled = _settings.ListenOutputEnabled;
            PadMonitorMeterPanel.Opacity = enabled ? 1.0 : 0.35;
            if (!enabled)
                ResetPadMonitorMeter();
        }

        private void ResetPadMonitorMeter()
        {
            PadMonitorMeterOverlayL.Width = 10000;
            PadMonitorMeterOverlayR.Width = 10000;
            MonitorRmsValueLabel.Text = "-∞";
            MonitorRmsValueLabelR.Text = "-∞";
            MonitorPeakIndicatorL.Background = PeakColdBrush;
            MonitorPeakIndicatorR.Background = PeakColdBrush;
            _monitorPeakHoldTimeL = DateTime.MinValue;
            _monitorPeakHoldTimeR = DateTime.MinValue;
        }

        private void ListenOutputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _settings.ListenOutputDeviceIndex = ListenOutputDeviceCombo.SelectedIndex;
            _settings.Save();
            RefreshPadOutputRouting();
        }

        private void RecordingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            int idx = RecordingModeCombo.SelectedIndex;
            ApplyModeComboIndex(idx);
            _settings.Save();
            KeyBufferHint.Visibility = idx == ModeComboKeyBufferIndex ? Visibility.Visible : Visibility.Collapsed;
            UpdateVadSettingsVisibility(idx);
            RefreshOutputFormatInfo();
        }

        // Sensitivity and Silence only apply to AutoVAD/Adaptive VAD detection.
        private void UpdateVadSettingsVisibility(int modeIdx)
        {
            var vadVisibility = modeIdx == ModeComboKeyBufferIndex ? Visibility.Collapsed : Visibility.Visible;
            SensitivityRow.Visibility = vadVisibility;
            SilenceRow.Visibility = vadVisibility;
        }

        // ── Monitoring toggle ──────────────────────────────────────────────────
        private void MonitorToggle_Checked(object sender, RoutedEventArgs e)
        {
            _inputMeterUpdatesEnabled = false;

            if (GetSelectedCaptureMode() == CaptureSourceMode.Microphone && WaveInEvent.DeviceCount == 0)
            {
                System.Windows.MessageBox.Show("No microphone detected.", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
                return;
            }

            if (GetSelectedCaptureMode() == CaptureSourceMode.OutputLoopback && _loopbackDevices.Count == 0)
            {
                System.Windows.MessageBox.Show("No active output device found for loopback capture.", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
                return;
            }

            if (GetSelectedCaptureMode() == CaptureSourceMode.AppLoopback && _appLoopbackProcesses.Count == 0)
            {
                System.Windows.MessageBox.Show("No apps currently producing audio.\nStart playback in an app first, then click the refresh button.", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
                return;
            }

            try
            {
                StartMonitoringWithCurrentSelection();
                _inputMeterUpdatesEnabled = true;
            }
            catch (Exception ex)
            {
                _inputMeterUpdatesEnabled = false;
                System.Windows.MessageBox.Show($"Unable to start monitoring:\n{ex.Message}", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
            }
            BroadcastIpcState();
        }

        private void MonitorToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _inputMeterUpdatesEnabled = false;
            _captureService.Stop();
            SetStatus("Idle — press Start to begin", "#FF757575");
            RefreshInputFormatInfo();
            ForceResetInputMeter();
            StartMeterDecay();

            _inputMeterResetTimer ??= new System.Windows.Threading.DispatcherTimer();
            _inputMeterResetTimer.Stop();
            _inputMeterResetTimer.Interval = TimeSpan.FromMilliseconds(380);
            _inputMeterResetTimer.Tick -= InputMeterResetTimerTick;
            _inputMeterResetTimer.Tick += InputMeterResetTimerTick;
            _inputMeterResetTimer.Start();
            BroadcastIpcState();
        }

        private void InputMeterResetTimerTick(object? sender, EventArgs e)
        {
            _inputMeterResetTimer?.Stop();
            if (_inputMeterUpdatesEnabled || MonitorToggle.IsChecked == true)
                return;

            ForceResetInputMeter();
        }

        private void ForceResetInputMeter()
        {
            RmsValueLabel.Text = "-∞";
            RmsValueLabelR.Text = "-∞";
            PeakIndicatorL.Background = PeakColdBrush;
            PeakIndicatorR.Background = PeakColdBrush;
            MeterOverlayL.Width = 10000;
            MeterOverlayR.Width = 10000;
        }

        private void StartMeterDecay()
        {
            double meterWidth = ThresholdCanvas.ActualWidth;
            if (meterWidth <= 0) { MeterOverlayL.Width = 10000; MeterOverlayR.Width = 10000; return; }

            _decayCurrentL = MeterOverlayL.Width;
            _decayCurrentR = MeterOverlayR.Width;
            _decayTargetL = meterWidth;
            _decayTargetR = meterWidth;
            _decayStep = 0;

            if (_meterDecayTimer == null)
            {
                _meterDecayTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _meterDecayTimer.Tick += MeterDecayTick;
            }
            _meterDecayTimer.Start();
        }

        private void MeterDecayTick(object? sender, EventArgs e)
        {
            _decayStep++;
            double t = Math.Min(1.0, (double)_decayStep / DecaySteps);
            // Ease-out quad
            double ease = 1.0 - (1.0 - t) * (1.0 - t);

            MeterOverlayL.Width = _decayCurrentL + (_decayTargetL - _decayCurrentL) * ease;
            MeterOverlayR.Width = _decayCurrentR + (_decayTargetR - _decayCurrentR) * ease;

            if (t >= 1.0)
            {
                _meterDecayTimer!.Stop();
                MeterOverlayL.Width = 10000;
                MeterOverlayR.Width = 10000;
            }
        }

        // ── Sensitivity / Silence sliders ──────────────────────────────────────
        private void SensitivitySlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (SensitivityValueLabel == null) return;
            double v = Math.Round(e.NewValue);
            SensitivityValueLabel.Text = v.ToString("0");
            _captureService.Sensitivity = v;
            _settings.Sensitivity = v;
            _settings.Save();
            UpdateThresholdMarker();
        }

        private void SilenceSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (SilenceValueLabel == null) return;
            double v = e.NewValue;
            SilenceValueLabel.Text = $"{v / 1000:0.00}s";
            _captureService.SilenceTimeoutMs = v;
            _settings.SilenceTimeoutMs = v;
            _settings.Save();
        }

        private void InputVolumeSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (InputVolumeValueLabel == null) return;
            double v = Math.Round(e.NewValue);
            InputVolumeValueLabel.Text = v.ToString("0");
            _captureService.InputGain = (float)(v / 100.0);
            _settings.InputVolume = v;
            _settings.Save();
        }

        private void OutputVolumeSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (OutputVolumeValueLabel == null) return;
            double v = Math.Round(e.NewValue);
            OutputVolumeValueLabel.Text = v.ToString("0");
            _outputVolume = (float)(v / 100.0);
            _settings.OutputVolume = v;
            _settings.Save();
            RefreshPadOutputRouting();
        }

        private void PadListenVolumeSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (PadListenVolumeValueLabel == null) return;
            double v = Math.Round(e.NewValue);
            PadListenVolumeValueLabel.Text = v.ToString("0");
            _padListenVolume = (float)(v / 100.0);
            _settings.PadListenVolume = v;
            _settings.Save();
            RefreshPadOutputRouting();
        }


        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string dataDir = Path.GetDirectoryName(RecordingStore.StorePath) ?? AppDataPaths.AppDataRoot;
            if (Directory.Exists(dataDir))
                System.Diagnostics.Process.Start("explorer.exe", dataDir);
        }

        // ── Settings / About buttons ───────────────────────────────────────────
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings)
            {
                Owner = this
            };
            if (win.ShowDialog() != true) return;

            // Apply changes
            _settings.RecordCodec = win.SelectedCodec;
            _settings.PastBufferDurationMs = win.SelectedBufferDurationMs;
            _settings.BufferHotKeyModifiers = win.SelectedHotKeyModifiers;
            _settings.BufferHotKeyVk = win.SelectedHotKeyVk;
            _settings.MaxRecords = win.SelectedMaxRecords;
            _settings.AppFontVariant = win.SelectedFontVariant;
            _settings.DefaultPadTitleTemplate = win.SelectedDefaultPadTitleTemplate;
            _settings.UseFocusedAppForPadTitle = win.SelectedUseFocusedAppForPadTitle;
            _settings.TrimEditorOutputDeviceIndex = win.SelectedTrimEditorOutputDeviceIndex;

            bool wasND = _settings.NewRecordingsNonDestructive;
            _settings.NewRecordingsNonDestructive = win.SelectedNewRecordingsNonDestructive;
            if (wasND && !_settings.NewRecordingsNonDestructive)
            {
                foreach (var pad in _padCache.Values)
                {
                    if (pad.Entry != null && pad.Entry.IsNonDestructive)
                    {
                        pad.Entry.IsNonDestructive = false;
                        pad.Entry.TrimStartMs = 0;
                        pad.Entry.TrimEndMs = 0;
                        pad.Entry.GainDb = 0.0;
                        
                        try
                        {
                            using var reader = AudioReaderFactory.Open(pad.Entry.FilePath);
                            pad.Entry.Duration = reader.TotalTime;
                        }
                        catch { }

                        pad.SetEntry(pad.Entry);

                        _recordingStore.UpdateNonDestructiveSettings(
                            pad.Entry.RecordingId,
                            false,
                            0,
                            0,
                            0.0,
                            (long)pad.Entry.Duration.TotalMilliseconds
                        );
                    }
                }
                UpdatePadState();
            }

            // Appearance
            _settings.Theme = win.SelectedTheme;
            _settings.MeterSkin = win.SelectedMeterSkin;
            _settings.PerformanceMode = win.SelectedPerformanceMode;
            _settings.PauseAnimationsWhenUnfocused = win.SelectedPauseAnimationsWhenUnfocused;

            // System tray / startup
            _settings.MinimizeToTray = win.SelectedMinimizeToTray;
            _settings.CloseToTray = win.SelectedCloseToTray;
            _settings.StartMinimizedInTray = win.SelectedStartMinimizedInTray;
            _settings.RunOnWindowsStartup = win.SelectedRunOnWindowsStartup;

            // Detection / speech
            _settings.DetectionAlgorithm = win.SelectedDetectionAlgorithm;
            _settings.AutoRenameWithSpeech = win.SelectedAutoRenameWithSpeech;
            _settings.SpeechModel = win.SelectedSpeechModel;
            _settings.SpeechLanguage = win.SelectedSpeechLanguage;
            _settings.UseCudaForSpeech = win.SelectedUseCudaForSpeech;
            _settings.DiscordRichPresenceEnabled = win.SelectedDiscordRichPresenceEnabled;
            _settings.DiscordClientId = win.SelectedDiscordClientId;
            _settings.AutoInstallUpdates = win.SelectedAutoInstallUpdates;
            _settings.DownloadBetaUpdates = win.SelectedDownloadBetaUpdates;
            _settings.Save();

            DiscordService.Instance.Initialize(_settings.DiscordRichPresenceEnabled, _settings.DiscordClientId);

            App.ApplyFont(win.SelectedFontVariant);
            Helpers.ThemeManager.ApplyTheme(_settings.Theme);
            UpdateLoadingOverlayTheme();
            Helpers.ThemeManager.ApplyMeterSkin(_settings.MeterSkin, _settings.MeterDigitalDots);
            Helpers.ThemeManager.ApplyPerformanceMode(_settings.PerformanceMode);
            bool startupApplied = Helpers.StartupRegistration.SetRunOnStartup(_settings.RunOnWindowsStartup);
            bool startupEnabled = Helpers.StartupRegistration.IsRunOnStartupEnabled();
            if (!startupApplied || startupEnabled != _settings.RunOnWindowsStartup)
            {
                _settings.RunOnWindowsStartup = startupEnabled;
                _settings.Save();
                System.Windows.MessageBox.Show(
                    "PaDDY could not fully apply the Windows startup setting. The toggle was synced to the current registry state.",
                    "Startup registration",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            _captureService.DetectionAlgorithm = _settings.DetectionAlgorithm;
            _performanceMode = _settings.PerformanceMode;
            _pauseAnimationsWhenUnfocused = _settings.PauseAnimationsWhenUnfocused;

            _captureService.RecordCodec = win.SelectedCodec;
            _captureService.PastBufferDurationMs = win.SelectedBufferDurationMs;

            // Re-register hotkey with new key
            _hotkeyService.Reregister(this, _settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
            UpdateHotkeyLabel();

            // Restart monitoring to apply new format settings
            RestartMonitoringIfActive();
            RefreshOutputFormatInfo();
            RefreshInputFormatInfo();
            RefreshPadOutputRouting();
            WhisperARTTStatus();
            Forget(RefreshStorageInfoAsync());
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            new AboutWindow { Owner = this }.ShowDialog();
        }

        // ── Global hotkey → buffer capture ────────────────────────────────────
        private void OnBufferHotkeyPressed()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (MonitorToggle.IsChecked == true && _captureService.RecordingMode == AudioRecordingMode.KeyBuffer)
                    _captureService.TriggerBufferCapture();
            });
        }

        // ── Audio events (cross-thread) ────────────────────────────────────────
        private static double LinearToDb(double linear)
        {
            if (linear <= 0) return -100.0;
            return 20.0 * Math.Log10(linear / 100.0);
        }

        private static double DbToMeterFraction(double db)
        {
            if (db <= MeterMinDb) return 0.0;
            if (db >= 0.0) return 1.0;
            return (db - MeterMinDb) / (0.0 - MeterMinDb);
        }

        private void OnRmsChanged(double left, double right)
        {
            _lastRmsL = left;
            _lastRmsR = right;

            // Throttle OUTSIDE the Dispatcher call to avoid flooding the UI message queue
            var now = DateTime.UtcNow;
            if ((now - _lastInputMeterTick).TotalMilliseconds < 30)
                return;
            _lastInputMeterTick = now;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_inputMeterUpdatesEnabled || MonitorToggle.IsChecked != true) return;

                // Cancel any running decay animation — we have live data
                _meterDecayTimer?.Stop();

                double dbL = LinearToDb(left);
                double dbR = LinearToDb(right);

                // Update meter overlays (cover the unfilled portion from the right)
                double meterWidth = ThresholdCanvas.ActualWidth;
                if (meterWidth > 0)
                {
                    double filledL = DbToMeterFraction(dbL) * meterWidth;
                    double filledR = DbToMeterFraction(dbR) * meterWidth;
                    MeterOverlayL.Width = Math.Max(0, meterWidth - filledL);
                    MeterOverlayR.Width = Math.Max(0, meterWidth - filledR);
                }

                // Update dB labels
                RmsValueLabel.Text = left > 0 ? $"{dbL:0}" : "-∞";
                RmsValueLabelR.Text = right > 0 ? $"{dbR:0}" : "-∞";

                // Peak hold logic
                if (dbL >= PeakThresholdDb)
                    _peakHoldTimeL = now;
                if (dbR >= PeakThresholdDb)
                    _peakHoldTimeR = now;

                PeakIndicatorL.Background = (now - _peakHoldTimeL).TotalSeconds < PeakHoldSeconds
                    ? PeakHotBrush : PeakColdBrush;
                PeakIndicatorR.Background = (now - _peakHoldTimeR).TotalSeconds < PeakHoldSeconds
                    ? PeakHotBrush : PeakColdBrush;
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void UpdateInputMeterOverlaysLayout()
        {
            double meterWidth = ThresholdCanvas.ActualWidth;
            if (meterWidth <= 0) return;

            if (_meterDecayTimer != null && _meterDecayTimer.IsEnabled)
            {
                _decayTargetL = meterWidth;
                _decayTargetR = meterWidth;
                return;
            }

            if (_lastRmsL <= 0 || MonitorToggle.IsChecked != true || !_inputMeterUpdatesEnabled)
            {
                MeterOverlayL.Width = 10000;
            }
            else
            {
                double dbL = LinearToDb(_lastRmsL);
                double filledL = DbToMeterFraction(dbL) * meterWidth;
                MeterOverlayL.Width = Math.Max(0, meterWidth - filledL);
            }

            if (_lastRmsR <= 0 || MonitorToggle.IsChecked != true || !_inputMeterUpdatesEnabled)
            {
                MeterOverlayR.Width = 10000;
            }
            else
            {
                double dbR = LinearToDb(_lastRmsR);
                double filledR = DbToMeterFraction(dbR) * meterWidth;
                MeterOverlayR.Width = Math.Max(0, meterWidth - filledR);
            }
        }

        private void UpdateOutputMeter(double left, double right)
        {
            _lastOutputRmsL = left;
            _lastOutputRmsR = right;

            var now = DateTime.UtcNow;
            if ((now - _lastOutputMeterTick).TotalMilliseconds < 30)
                return;
            _lastOutputMeterTick = now;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _outputMeterDecayTimer?.Stop();

                double dbL = LinearToDb(left);
                double dbR = LinearToDb(right);

                double meterWidth = ThresholdCanvas.ActualWidth;
                if (meterWidth > 0)
                {
                    double filledL = DbToMeterFraction(dbL) * meterWidth;
                    double filledR = DbToMeterFraction(dbR) * meterWidth;
                    OutputMeterOverlayL.Width = Math.Max(0, meterWidth - filledL);
                    OutputMeterOverlayR.Width = Math.Max(0, meterWidth - filledR);
                }

                OutputRmsValueLabel.Text = left > 0 ? $"{dbL:0}" : "-∞";
                OutputRmsValueLabelR.Text = right > 0 ? $"{dbR:0}" : "-∞";

                if (dbL >= PeakThresholdDb)
                    _outputPeakHoldTimeL = now;
                if (dbR >= PeakThresholdDb)
                    _outputPeakHoldTimeR = now;

                OutputPeakIndicatorL.Background = (now - _outputPeakHoldTimeL).TotalSeconds < PeakHoldSeconds ? PeakHotBrush : PeakColdBrush;
                OutputPeakIndicatorR.Background = (now - _outputPeakHoldTimeR).TotalSeconds < PeakHoldSeconds ? PeakHotBrush : PeakColdBrush;

                // If both L and R are zero (playback stopped), start decay animation
                if (left <= 0 && right <= 0)
                    StartOutputMeterDecay();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void UpdateOutputMeterOverlaysLayout()
        {
            double meterWidth = ThresholdCanvas.ActualWidth;
            if (meterWidth <= 0) return;

            if (_outputMeterDecayTimer != null && _outputMeterDecayTimer.IsEnabled)
            {
                _outputDecayTargetL = meterWidth;
                _outputDecayTargetR = meterWidth;
                return;
            }

            if (_lastOutputRmsL <= 0)
            {
                OutputMeterOverlayL.Width = 10000;
            }
            else
            {
                double dbL = LinearToDb(_lastOutputRmsL);
                double filledL = DbToMeterFraction(dbL) * meterWidth;
                OutputMeterOverlayL.Width = Math.Max(0, meterWidth - filledL);
            }

            if (_lastOutputRmsR <= 0)
            {
                OutputMeterOverlayR.Width = 10000;
            }
            else
            {
                double dbR = LinearToDb(_lastOutputRmsR);
                double filledR = DbToMeterFraction(dbR) * meterWidth;
                OutputMeterOverlayR.Width = Math.Max(0, meterWidth - filledR);
            }
        }

        private void UpdatePadMonitorMeter(double left, double right)
        {
            _lastMonitorRmsL = left;
            _lastMonitorRmsR = right;

            var now = DateTime.UtcNow;
            if ((now - _lastMonitorMeterTick).TotalMilliseconds < 30)
                return;
            _lastMonitorMeterTick = now;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_settings.ListenOutputEnabled) { ResetPadMonitorMeter(); return; }

                double dbL = LinearToDb(left);
                double dbR = LinearToDb(right);

                double meterWidthL = PadMonitorMeterHostL.ActualWidth;
                if (meterWidthL > 0)
                {
                    double filledL = DbToMeterFraction(dbL) * meterWidthL;
                    PadMonitorMeterOverlayL.Width = Math.Max(0, meterWidthL - filledL);
                }

                double meterWidthR = PadMonitorMeterHostR.ActualWidth;
                if (meterWidthR > 0)
                {
                    double filledR = DbToMeterFraction(dbR) * meterWidthR;
                    PadMonitorMeterOverlayR.Width = Math.Max(0, meterWidthR - filledR);
                }

                MonitorRmsValueLabel.Text = left > 0 ? $"{dbL:0}" : "-∞";
                MonitorRmsValueLabelR.Text = right > 0 ? $"{dbR:0}" : "-∞";

                if (dbL >= PeakThresholdDb)
                    _monitorPeakHoldTimeL = now;
                if (dbR >= PeakThresholdDb)
                    _monitorPeakHoldTimeR = now;

                MonitorPeakIndicatorL.Background = (now - _monitorPeakHoldTimeL).TotalSeconds < PeakHoldSeconds ? PeakHotBrush : PeakColdBrush;
                MonitorPeakIndicatorR.Background = (now - _monitorPeakHoldTimeR).TotalSeconds < PeakHoldSeconds ? PeakHotBrush : PeakColdBrush;
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void UpdateMonitorMeterOverlaysLayout()
        {
            if (!_settings.ListenOutputEnabled)
            {
                ResetPadMonitorMeter();
                return;
            }

            double meterWidthL = PadMonitorMeterHostL.ActualWidth;
            if (meterWidthL > 0)
            {
                if (_lastMonitorRmsL <= 0)
                {
                    PadMonitorMeterOverlayL.Width = 10000;
                }
                else
                {
                    double dbL = LinearToDb(_lastMonitorRmsL);
                    double filledL = DbToMeterFraction(dbL) * meterWidthL;
                    PadMonitorMeterOverlayL.Width = Math.Max(0, meterWidthL - filledL);
                }
            }

            double meterWidthR = PadMonitorMeterHostR.ActualWidth;
            if (meterWidthR > 0)
            {
                if (_lastMonitorRmsR <= 0)
                {
                    PadMonitorMeterOverlayR.Width = 10000;
                }
                else
                {
                    double dbR = LinearToDb(_lastMonitorRmsR);
                    double filledR = DbToMeterFraction(dbR) * meterWidthR;
                    PadMonitorMeterOverlayR.Width = Math.Max(0, meterWidthR - filledR);
                }
            }
        }

        private void StartOutputMeterDecay()
        {
            double meterWidth = ThresholdCanvas.ActualWidth;
            if (meterWidth <= 0) { OutputMeterOverlayL.Width = 10000; OutputMeterOverlayR.Width = 10000; return; }

            _outputDecayCurrentL = OutputMeterOverlayL.Width;
            _outputDecayCurrentR = OutputMeterOverlayR.Width;
            _outputDecayTargetL = meterWidth;
            _outputDecayTargetR = meterWidth;
            _outputDecayStep = 0;

            if (_outputMeterDecayTimer == null)
            {
                _outputMeterDecayTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _outputMeterDecayTimer.Tick += OutputMeterDecayTick;
            }
            _outputMeterDecayTimer.Start();
        }

        private void OutputMeterDecayTick(object? sender, EventArgs e)
        {
            _outputDecayStep++;
            double t = Math.Min(1.0, (double)_outputDecayStep / DecaySteps);
            double ease = 1.0 - (1.0 - t) * (1.0 - t);

            OutputMeterOverlayL.Width = _outputDecayCurrentL + (_outputDecayTargetL - _outputDecayCurrentL) * ease;
            OutputMeterOverlayR.Width = _outputDecayCurrentR + (_outputDecayTargetR - _outputDecayCurrentR) * ease;

            if (t >= 1.0)
            {
                _outputMeterDecayTimer!.Stop();
                OutputMeterOverlayL.Width = 10000;
                OutputMeterOverlayR.Width = 10000;
                OutputRmsValueLabel.Text = "-∞";
                OutputRmsValueLabelR.Text = "-∞";
            }
        }

        private void OnRecordingStateChanged(bool isRecording)
        {
            _isRecording = isRecording;
            Dispatcher.InvokeAsync(() =>
            {
                if (isRecording)
                    SetStatus("Recording…", "#FFEF5350");
                else
                    SetStatus("Listening…", "#FF4CAF50");

                BroadcastIpcState();
            });
        }

        private void OnRecordingCompleted(RecordingEntry entry)
        {
            Task.Run(() =>
            {
                try
                {
                    byte[] audioBytes = File.ReadAllBytes(entry.FilePath);
                    string codec = Path.GetExtension(entry.FilePath).TrimStart('.');

                    string displayName;
                    string id;
                    string materializedPath;

                    lock (_recordingStore)
                    {
                        displayName = RecordingNameGenerator.BuildDisplayName(_settings, entry.CreatedAt, codec);
                        id = _recordingStore.Add(displayName, codec, entry.Duration, entry.CreatedAt, audioBytes, _settings.NewRecordingsNonDestructive);
                        materializedPath = _recordingStore.MaterializeToTemp(id, codec);
                    }

                    try { File.Delete(entry.FilePath); } catch { }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        entry.RecordingId = id;
                        entry.DisplayName = displayName;
                        entry.FilePath = materializedPath;
                        entry.IsNonDestructive = _settings.NewRecordingsNonDestructive;
                        AddPadButton(entry, toFavorites: false);
                        Forget(RefreshStorageInfoAsync());
                        if (_settings.AutoRenameWithSpeech) Forget(AutoRenameFromSpeechAsync(entry));
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                catch { /* Ignore unreadable or short recordings */ }
            });
        }

        private async Task AutoRenameFromSpeechAsync(RecordingEntry entry)
        {
            if (string.IsNullOrEmpty(entry.RecordingId) || string.IsNullOrEmpty(entry.FilePath))
                return;

            string recordingId = entry.RecordingId;
            string filePath = entry.FilePath;

            try
            {
                _speechService ??= new Services.SpeechRecognitionService();

                string text = await Task.Run(() =>
                    _speechService.TranscribeAsync(filePath, _settings.SpeechModel, _settings.SpeechLanguage, _settings.UseCudaForSpeech))
                    .ConfigureAwait(true);

                text = SanitizeSpeechName(text);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                _recordingStore.SetDisplayName(recordingId, text);
                entry.DisplayName = text;

                foreach (var btn in FindPadButtons(recordingId))
                    btn.SetEntry(entry);

                Forget(RefreshStorageInfoAsync());
            }
            catch { /* STT unavailable or failed; keep generated name */ }
        }

        private static string SanitizeSpeechName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                text = text.Replace(c, ' ');
            text = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > 60)
                text = text.Substring(0, 60).Trim();
            return text;
        }

        private IEnumerable<RecordingPadButton> FindPadButtons(string recordingId)
        {
            foreach (var panel in new[] { FavoritesPanel.Children, PadPanel.Children })
                foreach (var child in panel)
                    if (child is RecordingPadButton b && b.Entry?.RecordingId == recordingId)
                        yield return b;
        }

        private void OnCodecCompatibilityWarning(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _settings.RecordCodec = "wav";
                _settings.Save();
                _captureService.RecordCodec = "wav";
                RefreshOutputFormatInfo();

                System.Windows.MessageBox.Show(this,
                    message,
                    "Codec Disabled",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
        }

        // ── Pad panel ──────────────────────────────────────────────────────────
        private RecordingPadButton CreatePadButton(RecordingEntry entry)
        {
            var btn = new RecordingPadButton
            {
                Margin = new Thickness(6),
                OutputDeviceIndex = _outputDeviceIndex,
                ListenDeviceIndex = GetCurrentListenDeviceIndex(),
                TrimEditorOutputDeviceIndex = _settings.TrimEditorOutputDeviceIndex,
                OutputVolume = _outputVolume,
                ListenVolume = _padListenVolume
            };
            btn.SetEntry(entry);
            btn.IsFavorite = entry.IsFavorite;

            btn.PlaybackRmsChanged += UpdateOutputMeter;
            btn.ListenPlaybackRmsChanged += UpdatePadMonitorMeter;

            btn.DeleteRequested += (s, e) =>
            {
                if (s is RecordingPadButton b)
                {
                    // Remove from DB + both panels
                    if (b.Entry != null && !string.IsNullOrEmpty(b.Entry.RecordingId))
                    {
                        _recordingStore.Delete(b.Entry.RecordingId);
                        _padCache.Remove(b.Entry.RecordingId);
                    }
                    PadPanel.Children.Remove(b);
                    FavoritesPanel.Children.Remove(b);
                    UpdatePadState();
                }
            };

            btn.RecordingRenamed += (entry, newDisplayName) =>
            {
                if (string.IsNullOrEmpty(entry.RecordingId)) return;
                _recordingStore.SetDisplayName(entry.RecordingId, newDisplayName);
                Forget(RefreshStorageInfoAsync());
            };

            btn.PadColorChanged += (entry, newHexColor) =>
            {
                if (string.IsNullOrEmpty(entry.RecordingId)) return;
                _recordingStore.SetPadColor(entry.RecordingId, newHexColor);
            };

            btn.FavoriteToggled += (s, _) =>
            {
                if (s is not RecordingPadButton b || b.Entry == null) return;
                if (b.IsFavorite)
                {
                    // Move from PadPanel to FavoritesPanel, pinning to the active page.
                    PadPanel.Children.Remove(b);
                    FavoritesPanel.Children.Insert(0, b);
                    if (!string.IsNullOrEmpty(b.Entry.RecordingId))
                    {
                        _recordingStore.SetFavorite(b.Entry.RecordingId, true);
                        string pageId = (_activePadPage != null && !_activePadPage.IsFavorites)
                            ? _activePadPage.Id
                            : string.Empty;
                        b.Entry.PadPage = pageId;
                        _recordingStore.SetPadPage(b.Entry.RecordingId, pageId);
                    }
                }
                else
                {
                    // Move from FavoritesPanel to PadPanel, clearing its page.
                    FavoritesPanel.Children.Remove(b);
                    PadPanel.Children.Insert(0, b);
                    if (!string.IsNullOrEmpty(b.Entry.RecordingId))
                    {
                        _recordingStore.SetFavorite(b.Entry.RecordingId, false);
                        b.Entry.PadPage = string.Empty;
                        _recordingStore.SetPadPage(b.Entry.RecordingId, string.Empty);
                    }
                    EnforceMaxRecords();
                }
                UpdatePadState();
                Forget(RefreshStorageInfoAsync());
            };

            btn.RecordingEdited += (entry) =>
            {
                // In-place editor save: update the stored audio bytes in DB
                if (string.IsNullOrEmpty(entry.RecordingId) || !File.Exists(entry.FilePath)) return;
                try
                {
                    if (entry.IsNonDestructive)
                    {
                        _recordingStore.UpdateNonDestructiveSettings(
                            entry.RecordingId,
                            true,
                            entry.TrimStartMs,
                            entry.TrimEndMs,
                            entry.GainDb,
                            (long)entry.Duration.TotalMilliseconds
                        );
                    }
                    else
                    {
                        byte[] updated = File.ReadAllBytes(entry.FilePath);
                        _recordingStore.UpdateAudioData(entry.RecordingId, updated);
                        _recordingStore.UpdateNonDestructiveSettings(
                            entry.RecordingId,
                            false,
                            0,
                            0,
                            0.0,
                            (long)entry.Duration.TotalMilliseconds
                        );
                    }
                }
                catch { }
                Forget(RefreshStorageInfoAsync());
            };

            btn.RecordingCopied += (copyPath, asFav) =>
            {
                if (!File.Exists(copyPath)) return;
                try
                {
                    // Read duration and bytes from the copy file, then store in DB.
                    TimeSpan duration;
                    using (var reader = AudioReaderFactory.Open(copyPath))
                        duration = reader.TotalTime;

                    byte[] audioBytes = File.ReadAllBytes(copyPath);
                    try { File.Delete(copyPath); } catch { }

                    string codec = Path.GetExtension(copyPath).TrimStart('.');
                    string displayName = RecordingNameGenerator.BuildDisplayName(_settings, DateTime.Now, codec);
                    var newEntry = new RecordingEntry
                    {
                        DisplayName = displayName,
                        Duration = duration,
                        CreatedAt = DateTime.Now,
                        IsFavorite = asFav
                    };
                    string id = _recordingStore.Add(displayName, codec, newEntry.Duration, newEntry.CreatedAt, audioBytes);
                    newEntry.RecordingId = id;
                    newEntry.FilePath = _recordingStore.MaterializeToTemp(id, codec);

                    AddPadButton(newEntry, asFav);
                    Forget(RefreshStorageInfoAsync());
                }
                catch { }
            };

            btn.MouseEnter += (s, _) => _hoveredPad = s as RecordingPadButton;
            btn.MouseLeave += (_, _) => { if (_hoveredPad == btn) _hoveredPad = null; };

            btn.DragStarting += BeginPadDragVisual;
            btn.DragFinished += FinalizePadDrop;

            return btn;
        }

        // ── Effect chain management ───────────────────────────────────────────────
        private void OpenGlobalEffectsWindow()
        {
            var win = new EffectsWindow(_globalCaptureChain, isPerClip: false) { Owner = this };
            if (win.ShowDialog() == true)
            {
                _effectSettings.GlobalChain = EffectSettingsManager.ToConfig(_globalCaptureChain);
                EffectSettingsManager.Save(_effectSettings);
                // Live chain is already updated by CommitValues() inside the window
            }
        }

        private void AddPadButton(RecordingEntry entry, bool toFavorites)
        {
            entry.IsFavorite = entry.IsFavorite || toFavorites;

            var btn = CreatePadButton(entry);
            _padCache[entry.RecordingId] = btn;

            if (entry.IsFavorite)
                FavoritesPanel.Children.Insert(0, btn);
            else
            {
                PadPanel.Children.Insert(0, btn);
                SortPadPanel();
            }

            EnforceMaxRecords();
            UpdatePadState();
            SetStatus($"Saved: {entry.FileName}", "#FF4CAF50");
        }
        // ── Pad pages (tabs) ───────────────────────────────────────────────────
        private void InitializePadPages()
        {
            _activePadPage = _settings.EnsurePadPages();
            _settings.Save();
            BuildPadPageTabs();
        }

        private void BuildPadPageTabs()
        {
            PadPageTabBar.Children.Clear();
            var pages = _settings.PadPages
                .OrderBy(p => p.IsFavorites ? 0 : 1)
                .ThenBy(p => p.Order)
                .ToList();
            foreach (var page in pages)
            {
                bool isActive = _activePadPage != null && page.Id == _activePadPage.Id;
                var tab = new System.Windows.Controls.Button
                {
                    Content = page.IsFavorites ? "★ " + page.Name : page.Name,
                    Tag = page.Id,
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(2, 0, 0, 0),
                    FontSize = 10,
                    FontWeight = isActive ? FontWeights.Bold : FontWeights.Normal,
                    Background = isActive
                        ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xC1, 0x07))
                        : System.Windows.Media.Brushes.Transparent,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xC1, 0x07)),
                    BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, 0xFF, 0xC1, 0x07)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                string pageId = page.Id;
                tab.Click += (_, _) => SwitchToPadPage(pageId);
                tab.AllowDrop = true;
                tab.DragOver += (_, ev) =>
                {
                    ev.Effects = GetDraggedPad(ev) != null ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
                    ev.Handled = true;
                    UpdateDragAdorner(ev);
                };
                tab.Drop += (_, ev) =>
                {
                    var pad = GetDraggedPad(ev);
                    if (pad?.Entry != null) MovePadToPage(pad, pageId);
                    ev.Handled = true;
                };
                PadPageTabBar.Children.Add(tab);
            }

            bool canEdit = _activePadPage != null && !_activePadPage.IsFavorites;
            RenamePadPageButton.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
            DeletePadPageButton.Visibility = canEdit ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SwitchToPadPage(string pageId)
        {
            var page = _settings.PadPages.FirstOrDefault(p => p.Id == pageId);
            if (page == null) return;
            _activePadPage = page;
            _settings.ActivePadPageId = pageId;
            _settings.Save();
            BuildPadPageTabs();
            ReloadFavoritesPanel();
        }

        private void ReloadFavoritesPanel()
        {
            FavoritesPanel.Children.Clear();
            RecordingPadButton.SuppressEntranceAnimation++;
            LoadFavoritesFromStore();
            RecordingPadButton.SuppressEntranceAnimation--;
        }

        public void PrepareRecordingDataRestore()
        {
            try
            {
                // Stop monitoring to release audio engine handles and prevent data conflicts
                _inputMeterUpdatesEnabled = false;
                _captureService.Stop();
                ForceResetInputMeter();

                _recordingStore.Dispose();
            }
            catch
            {
                // Ignore dispose failures when preparing for restore.
            }

            _recordingStore.CleanupAllTempFiles();
        }

        public async Task ReloadRecordingDataFromDiskAsync()
        {
            try
            {
                _recordingStore.Dispose();
                // Force SQLite to release all pooled connection handles to the database files.
                SqliteConnection.ClearAllPools();
            }
            catch
            {
                // Ignore dispose failures when reloading.
            }

            _recordingStore.CleanupAllTempFiles();
            _recordingStore = new RecordingStore();
            _settings = AppSettings.Load();
            _effectSettings = EffectSettingsManager.Load();

            PadPanel.Children.Clear();
            FavoritesPanel.Children.Clear();

            // Re-apply all application settings to UI and services
            _suppressSelectionEvents = true;
            ApplySettings();
            ThemeManager.ApplyTheme(_settings.Theme);
            UpdateLoadingOverlayTheme();
            ThemeManager.ApplyMeterSkin(_settings.MeterSkin, _settings.MeterDigitalDots);
            ThemeManager.ApplyPerformanceMode(_settings.PerformanceMode);
            App.ApplyFont(_settings.AppFontVariant);
            _suppressSelectionEvents = false;

            InitializePadPages();
            await PreloadAllPadsAsync();
            RecordingPadButton.SuppressEntranceAnimation++;
            LoadFavoritesFromStore();
            LoadNonFavoritesFromStore();
            RecordingPadButton.SuppressEntranceAnimation--;

            // Refresh status labels for storage and speech-to-text
            WhisperARTTStatus();
            Forget(RefreshStorageInfoAsync());

            // If monitoring was requested, restart it with the new device settings
            if (MonitorToggle.IsChecked == true)
            {
                try { StartMonitoringWithCurrentSelection(); _inputMeterUpdatesEnabled = true; }
                catch { MonitorToggle.IsChecked = false; }
            }

            SetStatus("Restored backup and reloaded recordings.", "#FF4CAF50");
        }

        private void AddPadPageButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Controls.RenameDialog("New Page") { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string name = dlg.NewName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            var page = new PadPage
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Order = _settings.PadPages.Count,
                IsFavorites = false
            };
            _settings.PadPages.Add(page);
            _settings.Save();
            SwitchToPadPage(page.Id);
        }

        private void RenamePadPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activePadPage == null || _activePadPage.IsFavorites) return;
            var dlg = new Controls.RenameDialog(_activePadPage.Name) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string name = dlg.NewName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            _activePadPage.Name = name;
            _settings.Save();
            BuildPadPageTabs();
        }

        private void DeletePadPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activePadPage == null || _activePadPage.IsFavorites) return;

            var confirm = System.Windows.MessageBox.Show(
                $"Delete page \"{_activePadPage.Name}\"? Its pads will move back to Favorites.",
                "Delete Pad Page", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            string deletedId = _activePadPage.Id;
            _recordingStore.ClearPadPage(deletedId);

            foreach (var btn in _padCache.Values)
            {
                if (btn.Entry != null && btn.Entry.PadPage == deletedId)
                {
                    btn.Entry.PadPage = string.Empty;
                }
            }

            _settings.PadPages.RemoveAll(p => p.Id == deletedId);
            var favorites = _settings.EnsurePadPages();
            _settings.ActivePadPageId = favorites.Id;
            _settings.Save();
            SwitchToPadPage(favorites.Id);
        }

        private async Task PreloadAllPadsAsync()
        {
            _padCache.Clear();
            var records = _recordingStore.GetAll();
            int total = records.Count;
            int count = 0;
            foreach (var rec in records)
            {
                count++;
                if (count % 5 == 0 || count == 1 || count == total)
                {
                    ShowLoadingOverlay($"Loading recordings ({count} / {total})...");
                    await Task.Delay(10);
                }

                try
                {
                    string tempPath = _recordingStore.MaterializeToTemp(rec.Id, rec.Codec);
                    var entry = new RecordingEntry
                    {
                        RecordingId = rec.Id,
                        FilePath = tempPath,
                        DisplayName = rec.DisplayName,
                        Duration = TimeSpan.FromMilliseconds(rec.DurationMs),
                        CreatedAt = rec.CreatedAt,
                        IsFavorite = rec.IsFavorite,
                        PadPage = rec.PadPage,
                        SortOrder = rec.SortOrder,
                        IsNonDestructive = rec.IsNonDestructive,
                        TrimStartMs = rec.TrimStartMs,
                        TrimEndMs = rec.TrimEndMs,
                        GainDb = rec.GainDb,
                        PadColor = rec.PadColor
                    };
                    var btn = CreatePadButton(entry);
                    _padCache[rec.Id] = btn;
                }
                catch { /* skip unreadable records */ }
            }
        }

        private void LoadFavoritesFromStore()
        {
            FavoritesPanel.Children.Clear();
            var favs = _padCache.Values
                .Where(btn => btn.Entry != null && btn.Entry.IsFavorite && BelongsToActivePage(btn.Entry))
                .OrderBy(btn => btn.Entry!.SortOrder)
                .ThenByDescending(btn => btn.Entry!.CreatedAt)
                .ToList();

            foreach (var btn in favs)
            {
                if (System.Windows.Media.VisualTreeHelper.GetParent(btn) is System.Windows.Controls.Panel p)
                {
                    p.Children.Remove(btn);
                }
                FavoritesPanel.Children.Add(btn);
            }
            UpdatePadState();
        }

        /// <summary>True when a favourite recording should appear on the active pad page.</summary>
        private bool BelongsToActivePage(RecordingEntry rec)
        {
            if (!rec.IsFavorite) return false;
            string pp = rec.PadPage ?? string.Empty;
            if (_activePadPage == null || _activePadPage.IsFavorites)
                return pp.Length == 0 || _activePadPage == null || pp == _activePadPage.Id;
            return pp == _activePadPage.Id;
        }

        private void LoadNonFavoritesFromStore()
        {
            PadPanel.Children.Clear();
            int max = _settings.MaxRecords;
            int count = 0;

            var nonFavs = _padCache.Values
                .Where(btn => btn.Entry != null && !btn.Entry.IsFavorite)
                .OrderByDescending(btn => btn.Entry!.CreatedAt)
                .ToList();

            foreach (var btn in nonFavs)
            {
                if (max > 0 && count >= max) break;
                if (System.Windows.Media.VisualTreeHelper.GetParent(btn) is System.Windows.Controls.Panel p)
                {
                    p.Children.Remove(btn);
                }
                PadPanel.Children.Add(btn);
                count++;
            }

            SortPadPanel();
            UpdatePadState();
        }

        private void UpdatePadState()
        {
            int count = PadPanel.Children.Count;
            RecordingCountLabel.Text = count == 1 ? "1 clip" : $"{count} clips";
            EmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int favCount = FavoritesPanel.Children.Count;
            FavoriteCountLabel.Text = $" — {favCount}";
            bool hasFavorites = favCount > 0;
            bool hasExtraPages = _settings.PadPages != null && _settings.PadPages.Count > 1;
            
            bool isFavCollapsed = _settings.FavoritesPanelCollapsed;
            bool isRecCollapsed = _settings.RecordingsPanelCollapsed;

            FavoritesHeader.Visibility = (hasFavorites || hasExtraPages || !isFavCollapsed) ? Visibility.Visible : Visibility.Collapsed;

            FavoritesPanelBorder.Visibility = isFavCollapsed ? Visibility.Collapsed : Visibility.Visible;
            FavoritesCollapseIcon.Text = isFavCollapsed ? "►" : "▼";
            FavoritesCollapseButton.ToolTip = isFavCollapsed ? "Expand favorites" : "Collapse favorites";

            RecordingsScrollViewer.Visibility = isRecCollapsed ? Visibility.Collapsed : Visibility.Visible;
            RecordingsCollapseIcon.Text = isRecCollapsed ? "►" : "▼";
            RecordingsCollapseButton.ToolTip = isRecCollapsed ? "Expand recordings" : "Collapse recordings";

            if (PadsContainerGrid != null && PadsContainerGrid.RowDefinitions.Count >= 4)
            {
                if (isRecCollapsed)
                {
                    // If recordings are hidden, Favorite panel takes all the window space
                    PadsContainerGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                    PadsContainerGrid.RowDefinitions[3].Height = new GridLength(0, GridUnitType.Auto);
                    FavoritesPanelBorder.MaxHeight = double.PositiveInfinity;
                }
                else
                {
                    // If recordings are visible, Favorites gets Auto (up to ~3 rows) and Recordings takes the rest
                    PadsContainerGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Auto);
                    PadsContainerGrid.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
                    FavoritesPanelBorder.MaxHeight = 318;
                }
            }
        }

        private void FavoritesCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.FavoritesPanelCollapsed = !_settings.FavoritesPanelCollapsed;
            _settings.Save();
            UpdatePadState();
        }

        private void RecordingsCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.RecordingsPanelCollapsed = !_settings.RecordingsPanelCollapsed;
            _settings.Save();
            UpdatePadState();
        }

        /// <summary>
        /// Removes the oldest non-favorite recordings from PadPanel when MaxRecords is exceeded.
        /// </summary>
        private void EnforceMaxRecords()
        {
            int max = _settings.MaxRecords;
            if (max <= 0 || PadPanel.Children.Count <= max) return;

            // Batch the removals and search to avoid O(N^2) complexity and redundant layout passes
            var toRemove = PadPanel.Children.OfType<RecordingPadButton>()
                .Where(b => b.Entry != null)
                .OrderBy(b => b.Entry!.CreatedAt)
                .Take(PadPanel.Children.Count - max)
                .ToList();

            if (toRemove.Count == 0) return;

            foreach (var pad in toRemove)
            {
                pad.StopPlayback();
                if (pad.Entry != null && !string.IsNullOrEmpty(pad.Entry.RecordingId))
                {
                    _recordingStore.Delete(pad.Entry.RecordingId);
                    _padCache.Remove(pad.Entry.RecordingId);
                }
                PadPanel.Children.Remove(pad);
            }

            Forget(RefreshStorageInfoAsync());
        }

        // ── Clear / Delete All ─────────────────────────────────────────────────
        private void ClearPadsButton_Click(object sender, RoutedEventArgs e)
        {
            var buttons = PadPanel.Children.OfType<RecordingPadButton>().ToList();
            if (buttons.Count == 0) return;

            var idsToDelete = buttons
                .Where(b => b.Entry != null && !string.IsNullOrEmpty(b.Entry.RecordingId))
                .Select(b => b.Entry!.RecordingId)
                .ToList();

            foreach (var btn in buttons)
                btn.StopPlayback();

            PadPanel.Children.Clear();
            _recordingStore.DeleteAll(idsToDelete);
            foreach (var id in idsToDelete) _padCache.Remove(id);

            UpdatePadState();
            Forget(CompactAndRefreshAsync());
        }

        private void DeleteAllFilesButton_Click(object sender, RoutedEventArgs e)
        {
            int total = PadPanel.Children.Count + FavoritesPanel.Children.Count;
            if (total == 0) return;

            var dlg = new DeleteAllDialog { Owner = this, Icon = Icon };
            if (dlg.ShowDialog() != true) return;

            var toDelete = new List<RecordingPadButton>();

            // Always delete from PadPanel
            foreach (var child in PadPanel.Children.OfType<RecordingPadButton>())
                toDelete.Add(child);

            // Apply to FavoritesPanel only if NOT keeping favorites
            if (!dlg.KeepFavorites)
            {
                foreach (var child in FavoritesPanel.Children.OfType<RecordingPadButton>())
                    toDelete.Add(child);
            }

            var idsToDelete = toDelete
                .Where(b => b.Entry != null && !string.IsNullOrEmpty(b.Entry.RecordingId))
                .Select(b => b.Entry!.RecordingId)
                .ToList();

            foreach (var btn in toDelete)
            {
                btn.StopPlayback();
                PadPanel.Children.Remove(btn);
                if (!dlg.KeepFavorites)
                    FavoritesPanel.Children.Remove(btn);
            }

            _recordingStore.DeleteAll(idsToDelete);
            foreach (var id in idsToDelete) _padCache.Remove(id);

            UpdatePadState();
            Forget(CompactAndRefreshAsync());
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static readonly HashSet<string> _audioExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".opus", ".ogg" };

        private static bool IsAudioFile(string path) =>
            _audioExtensions.Contains(Path.GetExtension(path));

        private void RefreshInputFormatInfo()
        {
            if (MonitorToggle.IsChecked != true)
            {
                SetInfoLabel(InputFormatInfoLabel, "Input format: ", "waiting for monitoring");
                return;
            }

            var format = _captureService.CurrentCaptureFormat;
            if (format == null)
            {
                SetInfoLabel(InputFormatInfoLabel, "Input format: ", "detecting...");
                return;
            }

            SetInfoLabel(InputFormatInfoLabel, "Input format: ", FormatPcmDetails(format.SampleRate, format.BitsPerSample, format.Channels));
        }

        private void RefreshOutputFormatInfo()
        {
            string codec = (_captureService.RecordCodec ?? "wav").Trim().ToUpperInvariant();
            int sampleRate = _captureService.RecordSampleRate;
            int bitDepth = _captureService.RecordBitDepth;
            int channels = _captureService.RecordChannels;

            string suffix = codec is "WAV" or "FLAC"
                ? $"{FormatPcmDetails(sampleRate, bitDepth, channels)}"
                : $"{FormatSampleRate(sampleRate)} | {FormatChannels(channels)}";

            SetInfoLabel(OutputFormatInfoLabel, "Recording format: ", $"{codec} | {suffix}");
        }

        private async Task CompactAndRefreshAsync()
        {
            // Run WAL checkpoint + VACUUM off the UI thread so the app stays responsive,
            // then refresh the displayed storage size once the .dat file has shrunk.
            await Task.Run(() => _recordingStore.Compact());
            await RefreshStorageInfoAsync();
        }

        private async Task RefreshStorageInfoAsync()
        {
            try
            {
                (long dbBytes, int count) = await Task.Run(() =>
                    (_recordingStore.GetStoreSizeBytes(), _recordingStore.GetCount())
                );
                string value = $"{count} files | {FormatByteSize(dbBytes)}";
                SetInfoLabel(StorageInfoLabel, "Storage data: ", value);
            }
            catch
            {
                SetInfoLabel(StorageInfoLabel, "Storage data: ", "Unable to read storage data");
            }
        }
        private void WhisperARTTStatus()
        {
            string nm = "AR-STT: ";
            try
            {
                bool? arttsvalue = _settings.AutoRenameWithSpeech;
                if (arttsvalue == null)
                {
                    SetInfoLabel(WhisperStatusLabel, nm, "unavailable");
                    return;
                }
                else if (arttsvalue == true)
                {
                    string suffix = _settings.UseCudaForSpeech ? " (CUDA)" : "";
                    SetInfoLabel(WhisperStatusLabel, nm, "enabled" + suffix);
                }
                else
                {
                    SetInfoLabel(WhisperStatusLabel, nm, "disabled");
                    return;
                }
            }
            catch
            {
                SetInfoLabel(WhisperStatusLabel, nm, "unavailable");
                return;
            }
        }


        private async Task CheckForUpdateAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PaDDY-UpdateCheck/1.0");
                using var response = await http.GetAsync(ReleasesApiEndpoint);
                if (!response.IsSuccessStatusCode)
                {
                    UpdateNoticeText.Visibility = Visibility.Collapsed;
                    return;
                }

                await using var stream = await response.Content.ReadAsStreamAsync();
                using var document = await JsonDocument.ParseAsync(stream);
                if (!document.RootElement.TryGetProperty("tag_name", out var tagNameProperty))
                {
                    UpdateNoticeText.Visibility = Visibility.Collapsed;
                    return;
                }

                string tagName = tagNameProperty.GetString() ?? string.Empty;
                if (!TryParseTagVersion(tagName, out var latestVersion))
                {
                    UpdateNoticeText.Visibility = Visibility.Collapsed;
                    return;
                }

                var currentVersion = GetCurrentAppVersion();
                if (latestVersion <= currentVersion)
                {
                    UpdateNoticeText.Visibility = Visibility.Collapsed;
                    return;
                }

                UpdateNoticeLink.NavigateUri = ReleasesPageUri;
                UpdateNoticeText.Visibility = Visibility.Visible;
            }
            catch
            {
                UpdateNoticeText.Visibility = Visibility.Collapsed;
            }
        }

        private static Version GetCurrentAppVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        }

        private static bool TryParseTagVersion(string tagName, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(tagName)) return false;

            string normalized = tagName.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[1..];

            if (!Version.TryParse(normalized, out var parsed))
                return false;

            version = parsed;
            return true;
        }

        private static string FormatPcmDetails(int sampleRate, int bitDepth, int channels)
        {
            return $"{FormatSampleRate(sampleRate)} | {bitDepth}-bit | {FormatChannels(channels)}";
        }

        private static void Forget(Task task)
        {
            // Intentionally ignored background refresh task.
        }

        private static string FormatSampleRate(int sampleRate)
        {
            return $"{sampleRate / 1000.0:0.0} kHz";
        }

        private static string FormatChannels(int channels)
        {
            return channels switch
            {
                1 => "mono",
                2 => "stereo",
                _ => $"{channels}ch"
            };
        }

        private static string FormatByteSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int index = 0;

            while (value >= 1024 && index < units.Length - 1)
            {
                value /= 1024;
                index++;
            }

            return $"{value:0.0} {units[index]}";
        }

        private void UpdateNoticeLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Ignore browser launch failures to keep UX non-invasive.
            }
        }

        private void SetStatus(string text, string hexColor)
        {
            StatusLabel.Text = text;
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
            StatusDot.Fill = new SolidColorBrush(color);
            if (StatusDotGlow != null)
            {
                StatusDotGlow.Color = color;
            }

            // Update Discord Rich Presence
            string details = "Idle";
            string state = text;
            bool isRecordingOrMonitoring = false;

            if (text.Contains("Recording"))
            {
                details = "Recording audio clip";
                isRecordingOrMonitoring = true;
            }
            else if (text.Contains("Listening") || text.Contains("Monitoring"))
            {
                details = "Monitoring audio";
                isRecordingOrMonitoring = true;
            }
            else
            {
                details = "Idle";
            }

            DiscordService.Instance.UpdateActivity(details, state, isRecordingOrMonitoring);
        }

        private void UpdateThresholdMarker()
        {
            // Map slider value (0-100) directly to meter fraction so the marker
            // moves linearly across the dB-scaled bar.
            double frac = _captureService.Sensitivity / 100.0;

            if (ThresholdCanvas != null && ThresholdLine != null)
            {
                double widthL = ThresholdCanvas.ActualWidth;
                if (widthL > 0)
                    Canvas.SetLeft(ThresholdLine, frac * widthL - 1);
            }

            if (ThresholdCanvasR != null && ThresholdLineR != null)
            {
                double widthR = ThresholdCanvasR.ActualWidth;
                if (widthR > 0)
                    Canvas.SetLeft(ThresholdLineR, frac * widthR - 1);
            }
        }

        // ── IPC ────────────────────────────────────────────────────────────────
        private void BroadcastIpcState()
        {
            if (_ipcServer == null) return;
            var state = new
            {
                isRecording = _isRecording,
                isMonitoring = MonitorToggle.IsChecked == true,
                mode = _settings.RecordingMode
            };
            string json = JsonSerializer.Serialize(state);
            _ = _ipcServer.BroadcastAsync(json);
        }

        private void IpcServer_MessageReceived(object? sender, string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    using var doc = JsonDocument.Parse(message);
                    if (doc.RootElement.TryGetProperty("command", out var cmdEl))
                    {
                        var command = cmdEl.GetString();
                        if (command == "ToggleRecord")
                        {
                            MonitorToggle.IsChecked = MonitorToggle.IsChecked != true;
                        }
                        else if (command == "TriggerKeyBuffer")
                        {
                            if (MonitorToggle.IsChecked == true && _captureService.RecordingMode == AudioRecordingMode.KeyBuffer)
                                _captureService.TriggerBufferCapture();
                        }
                        else if (command == "PlayPad")
                        {
                            if (doc.RootElement.TryGetProperty("padId", out var padIdEl))
                            {
                                string padId = padIdEl.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(padId))
                                {
                                    if (_padCache.TryGetValue(padId, out var pad))
                                    {
                                        pad.TogglePlay();
                                    }
                                }
                            }
                        }
                        else if (command == "GetPads")
                        {
                            var pads = _recordingStore.GetAll()
                                .Select(p => new { id = p.Id, title = !string.IsNullOrWhiteSpace(p.DisplayName) ? p.DisplayName : "Unnamed Pad" })
                                .ToList();

                            var response = new { type = "padsList", pads = pads };
                            _ = _ipcServer?.BroadcastAsync(System.Text.Json.JsonSerializer.Serialize(response));
                        }
                    }
                }
                catch { }
            });
        }

        // ── Shutdown ───────────────────────────────────────────────────────────
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_settings.CloseToTray && !_forceExit)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            _trayIcon?.Dispose();
            _speechService?.Dispose();
            _hotkeyService.Dispose();
            _captureService.Dispose();
            _ipcServer?.Dispose();
            _overlayEngine.DiagnosticEvent -= OverlayEngine_DiagnosticEvent;
            _overlayEngine.Dispose();
            _recordingStore.CleanupAllTempFiles();
            _recordingStore.CleanupInternalTempRecordings();
            _recordingStore.Dispose();
            DiscordService.Instance.Dispose();
        }

        // ── Audio Import & Drag/Drop ───────────────────────────────────────────
        private async void ImportAudioButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import Audio File(s)",
                Filter = "Supported Audio Files (*.wav;*.mp3;*.ogg;*.flac;*.aiff;*.aif;*.wma;*.m4a;*.aac)|*.wav;*.mp3;*.ogg;*.flac;*.aiff;*.aif;*.wma;*.m4a;*.aac|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) == true && dlg.FileNames.Length > 0)
            {
                await ProcessAudioImportsAsync(dlg.FileNames);
            }
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
                {
                    if (files.Any(f => AudioImportService.IsSupportedExtension(f)))
                    {
                        e.Effects = System.Windows.DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var supportedFiles = files.Where(f => AudioImportService.IsSupportedExtension(f)).ToArray();
                    if (supportedFiles.Length > 0)
                    {
                        e.Handled = true;
                        await ProcessAudioImportsAsync(supportedFiles);
                    }
                }
            }
        }

        private async Task ProcessAudioImportsAsync(IEnumerable<string> filePaths)
        {
            var filesList = filePaths.ToList();
            if (filesList.Count == 0) return;

            ShowLoadingOverlay($"Importing {filesList.Count} audio file(s)...");
            await Task.Delay(50); // Yield UI thread to ensure LoadingOverlay renders

            int importedCount = 0;
            int failedCount = 0;

            foreach (var file in filesList)
            {
                ShowLoadingOverlay($"Verifying & converting: {System.IO.Path.GetFileName(file)}");
                var result = await AudioImportService.ImportFileAsync(file);

                if (result.Success && result.AudioData.Length > 0)
                {
                    try
                    {
                        var entry = new RecordingEntry
                        {
                            DisplayName = result.DisplayName,
                            Duration = result.Duration,
                            CreatedAt = DateTime.Now,
                            IsFavorite = false
                        };

                        string id = _recordingStore.Add(result.DisplayName, result.Codec, entry.Duration, entry.CreatedAt, result.AudioData);
                        entry.RecordingId = id;
                        entry.FilePath = _recordingStore.MaterializeToTemp(id, result.Codec);

                        if (_settings.AutoNormalizeOnCapture && File.Exists(entry.FilePath))
                        {
                            LoudnessNormalizer.NormalizeWavFile(entry.FilePath, entry.FilePath, _settings.TargetLoudnessLufs);
                            double newLufs = LoudnessNormalizer.MeasureIntegratedLoudness(entry.FilePath);
                            entry.LufsValue = newLufs;
                            _recordingStore.UpdateLufs(id, newLufs);
                        }

                        AddPadButton(entry, toFavorites: false);
                        importedCount++;

                        if (_settings.AutoSpeechIndexingEnabled && File.Exists(entry.FilePath))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    using var speech = new SpeechRecognitionService();
                                    string text = await speech.TranscribeAsync(entry.FilePath, _settings.SpeechModel, _settings.SpeechLanguage, _settings.UseCudaForSpeech);
                                    if (!string.IsNullOrWhiteSpace(text))
                                    {
                                        string tags = SpeechRecognitionService.ExtractTags(text);
                                        entry.Transcription = text;
                                        entry.Tags = tags;
                                        _recordingStore.UpdateTranscription(id, text, tags);
                                    }
                                }
                                catch { }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        System.Diagnostics.Debug.WriteLine($"Failed to save imported file: {ex.Message}");
                    }
                }
                else
                {
                    failedCount++;
                    System.Diagnostics.Debug.WriteLine($"Audio import error: {result.ErrorMessage}");
                }
            }

            Forget(RefreshStorageInfoAsync());
            HideLoadingOverlay();

            if (importedCount > 0)
            {
                SetStatus($"Successfully imported {importedCount} audio clip(s)", "#FF4CAF50");
            }
            if (failedCount > 0)
            {
                System.Windows.MessageBox.Show(this, 
                    $"{failedCount} file(s) could not be imported due to unsupported audio encoding.", 
                    "Import Warning", 
                    System.Windows.MessageBoxButton.OK, 
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private void LiveMicBtn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (LiveMicBtn.IsChecked == true)
            {
                _liveMicModulator.Start(_settings.InputDeviceIndex, _settings.OutputDeviceIndex, _settings.SecondaryOutputDeviceIndex, _settings.DualOutputEnabled);
                _liveMicModulator.IsFxEnabled = _settings.LiveMicFxEnabled;
                LiveMicBtn.Content = "🎙️ Live Mic ON";
                SetStatus("Live Mic Modulator active", "#FF4CAF50");
            }
            else
            {
                _liveMicModulator.Stop();
                LiveMicBtn.Content = "🎙️ Live Mic";
                SetStatus("Live Mic Modulator stopped", "#FF9090A0");
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchTextBox.Text?.Trim() ?? string.Empty;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;
            FilterPads(query);
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
        }

        private void FilterPads(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                foreach (var pad in _padCache.Values)
                {
                    pad.Visibility = Visibility.Visible;
                }
                return;
            }

            string q = query.ToLowerInvariant();
            foreach (var pad in _padCache.Values)
            {
                if (pad.Entry == null) continue;
                bool match = (pad.Entry.FileName != null && pad.Entry.FileName.ToLowerInvariant().Contains(q)) ||
                             (pad.Entry.DisplayName != null && pad.Entry.DisplayName.ToLowerInvariant().Contains(q)) ||
                             (pad.Entry.Tags != null && pad.Entry.Tags.ToLowerInvariant().Contains(q)) ||
                             (pad.Entry.Transcription != null && pad.Entry.Transcription.ToLowerInvariant().Contains(q));

                pad.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
