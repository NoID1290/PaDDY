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
using NoIDSoftwork.EffectProcessor;
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
                if (_splashWindow != null)
                {
                    _splashWindow.UpdateMessage(message);
                }
                else
                {
                    MainLoadingOverlay.Show(message);
                }
            });
        }

        public void HideLoadingOverlay(bool instantly = false)
        {
            Dispatcher.Invoke(() =>
            {
                if (_splashWindow != null)
                {
                    MainLoadingOverlay.Hide(instantly: true);
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
                        this.Show();
                        this.WindowState = WindowState.Normal;
                        this.Activate();
                    }
                }
                else
                {
                    MainLoadingOverlay.Hide(instantly);
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

        public static MainWindow? Instance { get; private set; }
        private bool _configPanelVisible = false;

        public MainWindow()
        {
            Instance = this;
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
                ShowActivated = false;
                WindowState = WindowState.Minimized;
                _initialTrayMinimize = true;
                this.Opacity = 1;
                this.IsHitTestVisible = false;
            }
            else
            {
                this.ShowActivated = false;
                this.Visibility = Visibility.Hidden;
                this.Opacity = 1;
                this.IsHitTestVisible = false;
            }

            InitializeComponent();
            LiveMicBtn.Visibility = Visibility.Visible;
            UpdateLoadingOverlayTheme();
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            Activated += OnWindowActivated;
            Deactivated += OnWindowDeactivated;

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await StartStartupSequenceAsync();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
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

            RecordingPadButton.GlobalPlaybackRmsChanged += UpdateOutputMeter;
            RecordingPadButton.GlobalListenPlaybackRmsChanged += UpdatePadMonitorMeter;
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
            _settings.AudioPanelVisible = _configPanelVisible;
            _settings.Save();
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


            if (_hoveredPad == null) return;
            // Don't intercept when a text-entry control has keyboard focus
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox ||
                Keyboard.FocusedElement is System.Windows.Controls.ComboBox) return;
            if (e.Key == Key.E) { e.Handled = true; _hoveredPad.OpenAudioEditor(); }
            else if (e.Key == Key.R) { e.Handled = true; _hoveredPad.OpenRename(); }
        }

        // ── Startup ────────────────────────────────────────────────────────────
        private bool _startupSequenceStarted;
        private async Task StartStartupSequenceAsync()
        {
            if (_startupSequenceStarted) return;
            _startupSequenceStarted = true;

            ShowLoadingOverlay("Core starting up");
            await Task.Yield();

            // Run device enumeration & VST pre-warming in parallel
            PopulateCaptureSourceModes();
            var audioDevicesTask = PopulateAudioDevicesAsync();
            var vstCleanupAndPrewarmTask = Task.Run(() => CleanStaleVstPathsAndPrewarm());

            PopulateRecordingModes();
            PopulateSortOrderCombo();

            await audioDevicesTask;
            ApplySettings();

            _configPanelVisible = _settings.AudioPanelVisible;
            ConfigPanelBorder.MaxHeight = _configPanelVisible ? 290.0 : 0.0;
            ConfigToggleText.Text = _configPanelVisible ? "▲" : "▼";

            _recordingStore.CleanupInternalTempRecordings();
            _recordingStore.CleanupOrphanedTempFiles();

            InitializePadPages();
            await PreloadAllPadsAsync();
            RecordingPadButton.SuppressEntranceAnimation++;
            LoadFavoritesFromStore();
            LoadNonFavoritesFromStore();
            RecordingPadButton.SuppressEntranceAnimation--;
            _suppressSelectionEvents = false;

            _globalCaptureChain?.Reset();

            // Await background VST checks before hooking events
            await vstCleanupAndPrewarmTask;

            _captureService.RmsLevelChanged += OnRmsChanged;
            _captureService.RecordingCompleted += OnRecordingCompleted;
            _captureService.RecordingStateChanged += OnRecordingStateChanged;
            _captureService.CodecCompatibilityWarning += OnCodecCompatibilityWarning;
            Helpers.ZoomManager.ScaleChanged += OnZoomScaleChanged;

            RefreshOutputFormatInfo();
            RefreshInputFormatInfo();
            WhisperARTTStatus();
            Forget(RefreshStorageInfoAsync());

            // ── Auto-update / update check ──────────────────────────────────
            if (Services.UpdateService.HasPendingRestore())
            {
                ShowLoadingOverlay("Restoring your data...");
                var restoreService = new Services.UpdateService();
                restoreService.StatusChanged += msg => ShowLoadingOverlay(msg);
                restoreService.RestorePostUpdateBackup();
            }
            else if (_settings.AutoInstallUpdates)
            {
                ShowLoadingOverlay("Checking for updates...");
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
                        Dispatcher.Invoke(() =>
                        {
                            _splashWindow?.HideProgress();
                            MainLoadingOverlay.HideProgress();
                        });

                        updateService.UninstallPluginAndCloseStreamDeck();

                        ShowLoadingOverlay("Backing up your data...");
                        bool backupOk = updateService.CreatePreUpdateBackup();

                        if (backupOk)
                        {
                            updateService.LaunchInstallerAndExit(installerPath);
                            return;
                        }
                        else
                        {
                            ShowLoadingOverlay("Backup failed — skipping update");
                        }
                    }
                    else
                    {
                        ShowLoadingOverlay("Download failed — skipping update");
                        Dispatcher.Invoke(() =>
                        {
                            _splashWindow?.HideProgress();
                            MainLoadingOverlay.HideProgress();
                        });
                    }
                }
            }
            else
            {
                _ = CheckForUpdateAsync();
            }

            if (_settings.PreloadAudioCache && (_settings.AutoRenameWithSpeech || _settings.AutoSpeechIndexingEnabled))
            {
                ShowLoadingOverlay("Loading AR-STT model");
                try
                {
                    _speechService = new Services.SpeechRecognitionService();
                    await _speechService.PreloadModelAsync(_settings.SpeechModel, _settings.UseCudaForSpeech);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to preload Whisper model: {ex.Message}");
                }
            }

            InitializeTrayIcon();

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

            // Pre-warm EffectsWindow XAML & styles in background on idle so opening is instantaneous
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var dummyChain = EffectChainFactory.CreateGlobal();
                    var warmupWin = new EffectsWindow(dummyChain, isPerClip: false);
                    warmupWin.Opacity = 0;
                    warmupWin.ShowInTaskbar = false;
                    warmupWin.WindowStartupLocation = WindowStartupLocation.Manual;
                    warmupWin.Left = -10000;
                    warmupWin.Top = -10000;
                    warmupWin.Width = 740;
                    warmupWin.Height = 700;
                    warmupWin.Show();
                    warmupWin.Close();
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.SystemIdle);

            // Reclaim startup temporary memory and compact SQLite DB WAL in background
            _ = Task.Run(() =>
            {
                try
                {
                    _recordingStore.Compact();
                    GC.Collect(2, GCCollectionMode.Aggressive, false, false);
                }
                catch { }
            });

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
                    string detail = !string.IsNullOrEmpty(backupService.LastError) ? $"\n\nDetails: {backupService.LastError}" : "";
                    System.Windows.MessageBox.Show(
                        this,
                        $"Failed to restore backup.\nPlease ensure the file is a valid PaDDY backup.{detail}",
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
            _trayIcon.ToggleMonitoringRequested += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    MonitorToggle.IsChecked = !(MonitorToggle.IsChecked == true);
                });
            };
            _trayIcon.TogglePadMonitoringRequested += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    ListenOutputEnabledCheck.IsChecked = !(ListenOutputEnabledCheck.IsChecked == true);
                });
            };
            _trayIcon.IsMonitoringActiveFunc = () => MonitorToggle.IsChecked == true;
            _trayIcon.IsPadMonitoringActiveFunc = () => ListenOutputEnabledCheck.IsChecked == true;

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
        /// Pauses or resumes the owned decorative meter timers.
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
            => RecordingPadButton.GetDraggedPad(e);

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
                string pageId = _activePadPage != null && !_activePadPage.IsFavorites ? _activePadPage.Id : string.Empty;
                pad.IsFavorite = true;
                pad.Entry.IsFavorite = true;
                pad.Entry.PadPage = pageId;
                _recordingStore.SetFavorite(pad.Entry.RecordingId, true);
                _recordingStore.SetPadPage(pad.Entry.RecordingId, pageId);
                PersistFavoritesOrder();
            }
            else if (ReferenceEquals(parent, PadPanel))
            {
                pad.IsFavorite = false;
                pad.Entry.IsFavorite = false;
                pad.Entry.PadPage = string.Empty;
                _recordingStore.SetFavorite(pad.Entry.RecordingId, false);
                _recordingStore.SetPadPage(pad.Entry.RecordingId, string.Empty);
                SwitchToCustomSort();
                PersistRecordingsOrder();
                EnforceMaxRecords();
            }
            // Otherwise the pad was moved to another page (detached) and already committed.

            UpdatePadState();
            RefreshSecondaryFolderWindows();
        }

        private void FavoritesPanel_DragOver(object sender, System.Windows.DragEventArgs e)
            => HandlePanelDragOver(FavoritesPanel, e);

        private void PadPanel_DragOver(object sender, System.Windows.DragEventArgs e)
            => HandlePanelDragOver(PadPanel, e);

        /// <summary>
        /// Updates only the lightweight drag ghost. Reordering the visual tree here causes a
        /// full panel layout on every drag event, so the actual move is deferred until drop.
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
        }

        private void FavoritesPanel_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                Window_Drop(sender, e);
                return;
            }
            CommitPanelDrop(FavoritesPanel, e);
            e.Handled = true;
        }

        private void PadPanel_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                Window_Drop(sender, e);
                return;
            }
            CommitPanelDrop(PadPanel, e);
            e.Handled = true;
        }

        private void CommitPanelDrop(System.Windows.Controls.Panel panel, System.Windows.DragEventArgs e)
        {
            var pad = GetDraggedPad(e);
            if (pad == null || pad.Entry == null) return;

            // Resolve to the main window's cached pad button instance if it exists
            if (_padCache.TryGetValue(pad.Entry.RecordingId, out var mainBtn) && mainBtn != null && mainBtn.Entry != null)
            {
                if (!ReferenceEquals(pad, mainBtn))
                {
                    (pad.Parent as System.Windows.Controls.Panel)?.Children.Remove(pad);
                }
                pad = mainBtn;
            }

            int index = ComputeDropIndex(panel, e, pad);
            MovePadOnce(panel, pad, index);

            if (ReferenceEquals(panel, FavoritesPanel))
            {
                string pageId = _activePadPage != null && !_activePadPage.IsFavorites ? _activePadPage.Id : string.Empty;
                pad.IsFavorite = true;
                pad.Entry.IsFavorite = true;
                pad.Entry.PadPage = pageId;
                _recordingStore.SetFavorite(pad.Entry.RecordingId, true);
                _recordingStore.SetPadPage(pad.Entry.RecordingId, pageId);
                PersistFavoritesOrder();
            }
            else if (ReferenceEquals(panel, PadPanel))
            {
                pad.IsFavorite = false;
                pad.Entry.IsFavorite = false;
                pad.Entry.PadPage = string.Empty;
                _recordingStore.SetFavorite(pad.Entry.RecordingId, false);
                _recordingStore.SetPadPage(pad.Entry.RecordingId, string.Empty);
                SwitchToCustomSort();
                PersistRecordingsOrder();
                EnforceMaxRecords();
            }

            UpdatePadState();
            RefreshSecondaryFolderWindows();
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

        /// <summary>Commits one visual-tree move at drop time (cross-panel aware).</summary>
        private static void MovePadOnce(System.Windows.Controls.Panel target, RecordingPadButton pad, int targetIndex)
        {
            var current = pad.Parent as System.Windows.Controls.Panel;
            if (ReferenceEquals(current, target))
            {
                int cur = target.Children.IndexOf(pad);
                if (cur < 0) return;
                int insert = Math.Clamp(targetIndex, 0, target.Children.Count - 1);
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

            // Resolve to the main window's cached pad button instance if it exists
            if (_padCache.TryGetValue(pad.Entry.RecordingId, out var mainBtn) && mainBtn != null && mainBtn.Entry != null)
            {
                if (!ReferenceEquals(pad, mainBtn))
                {
                    (pad.Parent as System.Windows.Controls.Panel)?.Children.Remove(pad);
                }
                pad = mainBtn;
            }

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
            RefreshSecondaryFolderWindows();
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

        private void CleanStaleVstPathsAndPrewarm()
        {
            try
            {
                NoIDSoftwork.EffectProcessor.Effects.VstPluginManager.PrewarmEmbeddedPlugins();

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

                if (currentSettings.PendingDeletedVstPluginPaths is { Count: > 0 } pendingPaths)
                {
                    foreach (string path in pendingPaths.ToList())
                    {
                        try
                        {
                            if (File.Exists(path))
                                File.Delete(path);
                            else if (Directory.Exists(path))
                                Directory.Delete(path, recursive: true);
                        }
                        catch
                        {
                            continue;
                        }

                        pendingPaths.Remove(path);
                    }

                    currentSettings.UserVstPluginPaths.RemoveAll(
                        p => !string.IsNullOrWhiteSpace(p) && !File.Exists(p) && !Directory.Exists(p));
                    vstSettingsChanged = true;
                }

                if (vstSettingsChanged) currentSettings.Save();
            }
            catch { }
        }

        private async Task PopulateAudioDevicesAsync()
        {
            var inputTask = Task.Run(() => AudioCaptureService.GetInputDevices());
            var loopbackTask = Task.Run(() => AudioCaptureService.GetLoopbackDevices());
            var appProcTask = Task.Run(() => AudioSessionHelper.GetAudioProcesses());
            var renderEndpointsTask = Task.Run(() =>
            {
                var names = new List<string>();
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    foreach (var ep in endpoints)
                        names.Add(ep.FriendlyName);
                }
                catch { }
                return names;
            });

            await Task.WhenAll(inputTask, loopbackTask, appProcTask, renderEndpointsTask);

            var inputDevices = await inputTask;
            var loopbackDevices = await loopbackTask;
            var appProcesses = await appProcTask;
            var renderEndpoints = await renderEndpointsTask;

            // Apply Input Devices
            InputDeviceCombo.Items.Clear();
            if (inputDevices.Count == 0)
            {
                InputDeviceCombo.Items.Add("No microphones found");
                InputDeviceCombo.SelectedIndex = 0;
            }
            else
            {
                foreach (var d in inputDevices)
                    InputDeviceCombo.Items.Add(d.Name);
                InputDeviceCombo.SelectedIndex = Math.Clamp(_settings.InputDeviceIndex, 0, inputDevices.Count - 1);
            }

            // Apply Output Devices
            OutputDeviceCombo.Items.Clear();
            OutputDeviceCombo.Items.Add("Default Output");
            foreach (var name in renderEndpoints)
                OutputDeviceCombo.Items.Add(name);
            int clampedOut = Math.Clamp(_settings.OutputDeviceIndex, 0, OutputDeviceCombo.Items.Count - 1);
            OutputDeviceCombo.SelectedIndex = clampedOut;
            _outputDeviceIndex = clampedOut - 1;

            // Apply Listen Output Devices
            ListenOutputDeviceCombo.Items.Clear();
            ListenOutputDeviceCombo.Items.Add("Default Output");
            foreach (var name in renderEndpoints)
                ListenOutputDeviceCombo.Items.Add(name);
            int clampedListen = Math.Clamp(_settings.ListenOutputDeviceIndex, 0, ListenOutputDeviceCombo.Items.Count - 1);
            ListenOutputDeviceCombo.SelectedIndex = clampedListen;
            ListenOutputDeviceCombo.IsEnabled = _settings.ListenOutputEnabled;
            ListenOutputDeviceCombo.Opacity = _settings.ListenOutputEnabled ? 1.0 : 0.4;

            // Apply Loopback Devices
            _loopbackDevices = loopbackDevices;
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

            // Apply App Loopback Processes
            _appLoopbackProcesses = appProcesses;
            AppLoopbackCombo.Items.Clear();
            if (_appLoopbackProcesses.Count == 0)
            {
                AppLoopbackCombo.Items.Add("No apps producing audio");
                AppLoopbackCombo.SelectedIndex = 0;
            }
            else
            {
                foreach (var p in _appLoopbackProcesses)
                    AppLoopbackCombo.Items.Add(p.ProcessName);
                int idx = _appLoopbackProcesses.FindIndex(p => p.ProcessId == _settings.AppLoopbackProcessId);
                AppLoopbackCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
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
            RecordingPadButton.AllowMultiPadPlayback = _settings.AllowMultiPadPlayback;
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
            AutoNormalizeCheck.IsChecked = _settings.AutoNormalizeOnCapture;

            SensitivitySlider.Value = _settings.Sensitivity;
            SilenceSlider.Value = _settings.SilenceTimeoutMs;
            BufferDurationSlider.Value = _settings.PastBufferDurationMs / 1000.0;
            BufferDurationValueLabel.Text = $"{_settings.PastBufferDurationMs / 1000}s";

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

            UpdatePadMonitorMeterAvailability();
            RefreshPadOutputRouting();
            RefreshOutputFormatInfo();
            RefreshInputFormatInfo();

            // Initialize Discord Service
            DiscordService.Instance.Initialize(_settings.DiscordRichPresenceEnabled, _settings.DiscordClientId);
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
            RecordingPadButton.AllowMultiPadPlayback = _settings.AllowMultiPadPlayback;
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
                        pad.GlobalFadeEnabled = _settings.GlobalFadeEnabled;
                        pad.GlobalFadeInDurationMs = _settings.GlobalFadeInDurationMs;
                        pad.GlobalFadeOutDurationMs = _settings.GlobalFadeOutDurationMs;
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
            }
            RefreshInputFormatInfo();
            RestartMonitoringIfActive();
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

        // Sensitivity and meter threshold marker only apply to threshold-based Auto VAD.
        // Silence timeout applies to both Auto VAD and Adaptive VAD.
        private void UpdateVadSettingsVisibility(int modeIdx)
        {
            var isAutoVad = modeIdx == 0;
            var isKeyBuffer = modeIdx == ModeComboKeyBufferIndex;
            var silenceVisibility = isKeyBuffer ? Visibility.Collapsed : Visibility.Visible;
            var bufferVisibility = isKeyBuffer ? Visibility.Visible : Visibility.Collapsed;
            var sensitivityVisibility = isAutoVad ? Visibility.Visible : Visibility.Collapsed;

            SensitivityRow.Visibility = sensitivityVisibility;
            SilenceRow.Visibility = silenceVisibility;
            if (BufferDurationRow != null)
            {
                BufferDurationRow.Visibility = bufferVisibility;
            }

            var markerVisibility = isAutoVad ? Visibility.Visible : Visibility.Collapsed;
            if (ThresholdLine != null) ThresholdLine.Visibility = markerVisibility;
            if (ThresholdLineR != null) ThresholdLineR.Visibility = markerVisibility;
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

        private void BufferDurationSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (BufferDurationValueLabel == null) return;
            double v = Math.Round(e.NewValue);
            BufferDurationValueLabel.Text = $"{v:0}s";
            _settings.PastBufferDurationMs = (int)(v * 1000);
            if (_captureService != null)
            {
                _captureService.PastBufferDurationMs = _settings.PastBufferDurationMs;
            }
            _settings.Save();
        }

        private void AutoNormalizeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _settings.AutoNormalizeOnCapture = AutoNormalizeCheck.IsChecked == true;
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
        private SettingsWindow? _activeSettingsWindow;
        private AboutWindow? _activeAboutWindow;

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded)
            {
                if (_activeSettingsWindow.WindowState == WindowState.Minimized)
                    _activeSettingsWindow.WindowState = WindowState.Normal;
                _activeSettingsWindow.Activate();
                _activeSettingsWindow.Focus();
                return;
            }

            var win = new SettingsWindow(_settings);
            _activeSettingsWindow = win;
            win.Closed += (s, args) =>
            {
                _activeSettingsWindow = null;
                if (!win.Confirmed) return;

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
                _settings.LiveMicOutputDeviceIndex = win.SelectedLiveMicOutputDeviceIndex;

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
                }
                UpdatePadState();

                // Appearance
                _settings.UiScale = win.SelectedUiScale;
                _settings.Theme = win.SelectedTheme;
                _settings.MeterSkin = win.SelectedMeterSkin;
                _settings.PerformanceMode = win.SelectedPerformanceMode;
                _settings.PauseAnimationsWhenUnfocused = win.SelectedPauseAnimationsWhenUnfocused;
                _settings.PreloadAudioCache = win.SelectedPreloadAudioCache;

                // System tray / startup
                _settings.MinimizeToTray = win.SelectedMinimizeToTray;
                _settings.CloseToTray = win.SelectedCloseToTray;
                _settings.StartMinimizedInTray = win.SelectedStartMinimizedInTray;
                _settings.RunOnWindowsStartup = win.SelectedRunOnWindowsStartup;

                // Detection / speech
                _settings.DetectionAlgorithm = win.SelectedDetectionAlgorithm;
                _settings.AutoRenameWithSpeech = win.SelectedAutoRenameWithSpeech;
                _settings.CancelRecordingIfNoVoice = win.SelectedCancelRecordingIfNoVoice;
                _settings.SpeechModel = win.SelectedSpeechModel;
                _settings.SpeechLanguage = win.SelectedSpeechLanguage;
                _settings.UseCudaForSpeech = win.SelectedUseCudaForSpeech;
                _settings.DiscordRichPresenceEnabled = win.SelectedDiscordRichPresenceEnabled;
                _settings.DiscordClientId = win.SelectedDiscordClientId;
                _settings.AutoInstallUpdates = win.SelectedAutoInstallUpdates;
                _settings.DownloadBetaUpdates = win.SelectedDownloadBetaUpdates;

                // Global Effects & Playback
                _settings.GlobalFadeEnabled = win.SelectedGlobalFadeEnabled;
                _settings.GlobalFadeInDurationMs = win.SelectedGlobalFadeInDurationMs;
                _settings.GlobalFadeOutDurationMs = win.SelectedGlobalFadeOutDurationMs;
                _settings.AllowMultiPadPlayback = win.SelectedAllowMultiPadPlayback;
                RecordingPadButton.AllowMultiPadPlayback = _settings.AllowMultiPadPlayback;
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
                if (_liveMicModulator.IsRunning)
                {
                    _liveMicModulator.Gain = (float)_settings.LiveMicGain;
                    _liveMicModulator.IsFxEnabled = _settings.LiveMicFxEnabled;
                    int liveMicIn = _settings.LiveMicDeviceIndex;
                    int liveMicOut = _settings.LiveMicOutputDeviceIndex - 1;
                    _liveMicModulator.Start(liveMicIn, liveMicOut, _settings.SecondaryOutputDeviceIndex, _settings.DualOutputEnabled);
                }
                RefreshOutputFormatInfo();
                RefreshInputFormatInfo();
                RefreshPadOutputRouting();
                WhisperARTTStatus();
                Forget(RefreshStorageInfoAsync());

                // Sync quick-config panel UI with updated settings
                _suppressSelectionEvents = true;
                try
                {
                    AutoNormalizeCheck.IsChecked = _settings.AutoNormalizeOnCapture;
                    BufferDurationSlider.Value = _settings.PastBufferDurationMs / 1000.0;
                    BufferDurationValueLabel.Text = $"{_settings.PastBufferDurationMs / 1000}s";
                }
                finally
                {
                    _suppressSelectionEvents = false;
                }
            };
            win.Owner = this;
            win.Show();

        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activeAboutWindow != null && _activeAboutWindow.IsLoaded)
            {
                if (_activeAboutWindow.WindowState == WindowState.Minimized)
                    _activeAboutWindow.WindowState = WindowState.Normal;
                _activeAboutWindow.Activate();
                _activeAboutWindow.Focus();
                return;
            }

            var win = new AboutWindow();
            _activeAboutWindow = win;
            win.Closed += (s, args) => _activeAboutWindow = null;
            win.Show();
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
            }), System.Windows.Threading.DispatcherPriority.Normal);
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
            }), System.Windows.Threading.DispatcherPriority.Normal);
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
                        if (_settings.AutoRenameWithSpeech || _settings.CancelRecordingIfNoVoice)
                            Forget(AutoRenameFromSpeechAsync(entry));
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
                {
                    if (_settings.CancelRecordingIfNoVoice)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var btn in FindPadButtons(recordingId))
                            {
                                if (btn.Entry != null && !string.IsNullOrEmpty(btn.Entry.RecordingId))
                                {
                                    _recordingStore.Delete(btn.Entry.RecordingId);
                                    _padCache.Remove(btn.Entry.RecordingId);
                                }
                                PadPanel.Children.Remove(btn);
                                FavoritesPanel.Children.Remove(btn);
                                UpdatePadState();
                            }
                            SetStatus("Recording cancelled (No voice detected)", "#FFEE534F");
                            Forget(RefreshStorageInfoAsync());
                        });
                    }
                    return;
                }

                if (_settings.AutoRenameWithSpeech)
                {
                    _recordingStore.SetDisplayName(recordingId, text);
                    entry.DisplayName = text;

                    foreach (var btn in FindPadButtons(recordingId))
                        btn.SetEntry(entry);

                    Forget(RefreshStorageInfoAsync());
                }
            }
            catch { /* STT unavailable or failed; keep generated name */ }
        }

        private static string SanitizeSpeechName(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = Services.SpeechRecognitionService.CleanTranscript(text);
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                text = text.Replace(c, ' ');
            text = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (text.Length > 60)
                text = text.Substring(0, 60).Trim();
            return text;
        }

        private List<RecordingPadButton> FindPadButtons(string recordingId)
        {
            return FavoritesPanel.Children.OfType<RecordingPadButton>()
                .Concat(PadPanel.Children.OfType<RecordingPadButton>())
                .Where(b => b.Entry?.RecordingId == recordingId)
                .ToList();
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
                Store = _recordingStore,
                OutputDeviceIndex = _outputDeviceIndex,
                ListenDeviceIndex = GetCurrentListenDeviceIndex(),
                TrimEditorOutputDeviceIndex = _settings.TrimEditorOutputDeviceIndex,
                OutputVolume = _outputVolume,
                ListenVolume = _padListenVolume,
                GlobalFadeEnabled = _settings.GlobalFadeEnabled,
                GlobalFadeInDurationMs = _settings.GlobalFadeInDurationMs,
                GlobalFadeOutDurationMs = _settings.GlobalFadeOutDurationMs
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
        private EffectsWindow? _activeGlobalEffectsWindow;

        private void OpenGlobalEffectsWindow()
        {
            if (_activeGlobalEffectsWindow != null && _activeGlobalEffectsWindow.IsLoaded)
            {
                if (_activeGlobalEffectsWindow.WindowState == WindowState.Minimized)
                    _activeGlobalEffectsWindow.WindowState = WindowState.Normal;
                _activeGlobalEffectsWindow.Activate();
                _activeGlobalEffectsWindow.Focus();
                return;
            }

            var win = new EffectsWindow(_globalCaptureChain, isPerClip: false);
            _activeGlobalEffectsWindow = win;
            win.Closed += (s, args) =>
            {
                _activeGlobalEffectsWindow = null;
                if (win.DialogResult == true)
                {
                    _effectSettings.GlobalChain = EffectSettingsManager.ToConfig(_globalCaptureChain);
                    EffectSettingsManager.Save(_effectSettings);
                    // Live chain is already updated by CommitValues() inside the window
                }
            };
            win.Show();
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
                var currentPage = page;
                tab.Click += (_, _) => SwitchToPadPage(pageId);
                tab.PreviewMouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        e.Handled = true;
                        OpenFolderInSecondaryWindow(currentPage);
                    }
                };
                tab.MouseDown += (s, e) =>
                {
                    if (e.ChangedButton == System.Windows.Input.MouseButton.Middle)
                    {
                        e.Handled = true;
                        OpenFolderInSecondaryWindow(currentPage);
                    }
                };

                var contextMenu = new System.Windows.Controls.ContextMenu();
                var openItem = new System.Windows.Controls.MenuItem { Header = "Open" };
                openItem.Click += (_, _) => OpenFolderInSecondaryWindow(currentPage);
                contextMenu.Items.Add(openItem);

                if (!page.IsFavorites)
                {
                    var renameItem = new System.Windows.Controls.MenuItem { Header = "Rename" };
                    renameItem.Click += (_, _) => RenamePadPage(currentPage);
                    contextMenu.Items.Add(renameItem);

                    var deleteItem = new System.Windows.Controls.MenuItem { Header = "Delete" };
                    deleteItem.Click += (_, _) => DeletePadPage(currentPage);
                    contextMenu.Items.Add(deleteItem);
                }

                tab.ContextMenu = contextMenu;
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

        private void EnsurePadCacheSynced()
        {
            var records = _recordingStore.GetAll();
            var recordIds = new HashSet<string>(records.Select(r => r.Id));

            foreach (var rec in records)
            {
                if (!_padCache.TryGetValue(rec.Id, out var btn) || btn == null)
                {
                    try
                    {
                        string tempPath = _settings.PreloadAudioCache
                            ? _recordingStore.MaterializeToTemp(rec.Id, rec.Codec)
                            : string.Empty;

                        var entry = new RecordingEntry
                        {
                            RecordingId = rec.Id,
                            FilePath = tempPath,
                            Codec = rec.Codec,
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
                        btn = CreatePadButton(entry);
                        _padCache[rec.Id] = btn;
                    }
                    catch { }
                }
                else if (btn.Entry != null)
                {
                    btn.Entry.IsFavorite = rec.IsFavorite;
                    btn.IsFavorite = rec.IsFavorite;
                    btn.Entry.PadPage = rec.PadPage;
                    btn.Entry.DisplayName = rec.DisplayName;
                    btn.Entry.SortOrder = rec.SortOrder;
                    btn.Entry.PadColor = rec.PadColor;
                    btn.Entry.IsNonDestructive = rec.IsNonDestructive;
                    btn.Entry.TrimStartMs = rec.TrimStartMs;
                    btn.Entry.TrimEndMs = rec.TrimEndMs;
                    btn.Entry.GainDb = rec.GainDb;
                }
            }

            var deletedIds = _padCache.Keys.Where(id => !recordIds.Contains(id)).ToList();
            foreach (var id in deletedIds)
            {
                if (_padCache.TryGetValue(id, out var btn))
                {
                    (btn.Parent as System.Windows.Controls.Panel)?.Children.Remove(btn);
                    _padCache.Remove(id);
                }
            }
        }

        private void RefreshSecondaryFolderWindows()
        {
            foreach (var win in _secondaryFolderWindows.Values.ToList())
            {
                if (win.IsLoaded) win.RefreshPads();
            }
        }

        private void ReloadFavoritesPanel()
        {
            FavoritesPanel.Children.Clear();
            RecordingPadButton.SuppressEntranceAnimation++;
            LoadFavoritesFromStore();
            RecordingPadButton.SuppressEntranceAnimation--;

            RefreshSecondaryFolderWindows();
        }

        private readonly Dictionary<string, Views.SecondaryFolderWindow> _secondaryFolderWindows = new();

        public void OpenFolderInSecondaryWindow(PadPage page)
        {
            if (page == null) return;

            if (!page.IsFavorites && !_settings.FavoritesPanelCollapsed)
            {
                _settings.FavoritesPanelCollapsed = true;
                _settings.Save();
                UpdatePadState();
            }

            if (_secondaryFolderWindows.TryGetValue(page.Id, out var existingWin) && existingWin.IsLoaded)
            {
                if (existingWin.WindowState == WindowState.Minimized)
                    existingWin.WindowState = WindowState.Normal;
                existingWin.Activate();
                existingWin.Focus();
                return;
            }

            var win = new Views.SecondaryFolderWindow(
                page,
                _recordingStore,
                _settings,
                _outputDeviceIndex,
                GetCurrentListenDeviceIndex(),
                _outputVolume,
                _padListenVolume,
                onDataChanged: () =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        EnsurePadCacheSynced();
                        ReloadFavoritesPanel();
                        LoadNonFavoritesFromStore();
                        UpdatePadState();
                        Forget(RefreshStorageInfoAsync());
                    });
                });

            _secondaryFolderWindows[page.Id] = win;
            win.Closed += (s, e) => _secondaryFolderWindows.Remove(page.Id);
            win.Show();
        }

        public void PrepareRecordingDataRestore()
        {
            try
            {
                // Stop monitoring to release audio engine handles and prevent data conflicts
                _inputMeterUpdatesEnabled = false;
                _captureService.Stop();
                ForceResetInputMeter();

                CloseOwnedSecondaryWindows();
                PadPanel.Children.Clear();
                FavoritesPanel.Children.Clear();
                _padCache.Clear();

                _recordingStore.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
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

            // Re-populate device combos from the freshly restored settings so the
            // correct audio devices are selected without requiring a restart.
            _suppressSelectionEvents = true;
            PopulateInputDevices();
            PopulateLoopbackDevices();
            PopulateAppLoopbackProcesses();
            PopulateOutputDevices();
            PopulateListenOutputDevices();

            // Sync _outputDeviceIndex from the combo that PopulateOutputDevices just set.
            _outputDeviceIndex = OutputDeviceCombo.SelectedIndex - 1;

            // Re-apply all application settings to UI and services
            ApplySettings();
            ThemeManager.ApplyTheme(_settings.Theme);
            UpdateLoadingOverlayTheme();
            ThemeManager.ApplyMeterSkin(_settings.MeterSkin, _settings.MeterDigitalDots);
            ThemeManager.ApplyPerformanceMode(_settings.PerformanceMode);
            App.ApplyFont(_settings.AppFontVariant);
            _trayIcon?.UpdateMenuFont();
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
            if (_activePadPage != null)
                RenamePadPage(_activePadPage);
        }

        private void RenamePadPage(PadPage page)
        {
            if (page.IsFavorites) return;
            var dlg = new Controls.RenameDialog(page.Name) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string name = dlg.NewName.Trim();
            if (string.IsNullOrEmpty(name)) return;

            page.Name = name;
            _settings.Save();
            BuildPadPageTabs();

            if (_secondaryFolderWindows.TryGetValue(page.Id, out var window) && window.IsLoaded)
                window.RefreshPageTitle();
        }

        private void DeletePadPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (_activePadPage != null)
                DeletePadPage(_activePadPage);
        }

        private void DeletePadPage(PadPage page)
        {
            if (page.IsFavorites) return;

            var confirm = System.Windows.MessageBox.Show(
                $"Delete page \"{page.Name}\"? Its pads will move back to Favorites.",
                "Delete Pad Page", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            string deletedId = page.Id;
            bool wasActive = _activePadPage?.Id == deletedId;
            _recordingStore.ClearPadPage(deletedId);

            foreach (var btn in _padCache.Values)
            {
                if (btn.Entry != null && btn.Entry.PadPage == deletedId)
                {
                    btn.Entry.PadPage = string.Empty;
                }
            }

            if (_secondaryFolderWindows.TryGetValue(deletedId, out var window))
                window.Close();

            _settings.PadPages.RemoveAll(p => p.Id == deletedId);
            var favorites = _settings.EnsurePadPages();
            if (wasActive)
            {
                SwitchToPadPage(favorites.Id);
            }
            else
            {
                _settings.Save();
                BuildPadPageTabs();
                ReloadFavoritesPanel();
                UpdatePadState();
            }
        }

        private async Task PreloadAllPadsAsync()
        {
            _padCache.Clear();
            var records = _recordingStore.GetAll();
            int total = records.Count;
            if (total == 0) return;

            // Phase 1: Materialize any missing audio cache files safely on a worker thread
            if (_settings.PreloadAudioCache)
            {
                await Task.Run(() =>
                {
                    _recordingStore.MaterializeAllMissingToTemp(records, (done, missingTotal) =>
                    {
                        Dispatcher.BeginInvoke(() => ShowLoadingOverlay($"Extracting audio ({done} / {missingTotal})..."), System.Windows.Threading.DispatcherPriority.Background);
                    });
                });
            }

            // Phase 2: Create UI elements on Dispatcher in smooth batches without blocking UI
            int count = 0;
            foreach (var rec in records)
            {
                count++;
                if (count % 35 == 0 || count == 1 || count == total)
                {
                    ShowLoadingOverlay($"Loading pads ({count} / {total})...");
                    await Task.Yield();
                }

                try
                {
                    string tempPath = _settings.PreloadAudioCache
                        ? Path.Combine(RecordingStore.TempDir, $"{rec.Id}.{rec.Codec}")
                        : string.Empty;

                    var entry = new RecordingEntry
                    {
                        RecordingId = rec.Id,
                        FilePath = tempPath,
                        Codec = rec.Codec,
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



        // ── Helpers ────────────────────────────────────────────────────────────
        private static readonly HashSet<string> _audioExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".opus", ".ogg" };

        private static bool IsAudioFile(string path) =>
            _audioExtensions.Contains(Path.GetExtension(path));

        private void RefreshInputFormatInfo()
        {
            string prefix = LocalizationManager.Instance["InputFormatPrefix"];
            if (MonitorToggle.IsChecked != true)
            {
                SetInfoLabel(InputFormatInfoLabel, prefix, LocalizationManager.Instance["InputFormatWaiting"]);
                return;
            }

            var format = _captureService.CurrentCaptureFormat;
            if (format == null)
            {
                SetInfoLabel(InputFormatInfoLabel, prefix, LocalizationManager.Instance["InputFormatDetecting"]);
                return;
            }

            SetInfoLabel(InputFormatInfoLabel, prefix, FormatPcmDetails(format.SampleRate, format.BitsPerSample, format.Channels));
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

            SetInfoLabel(OutputFormatInfoLabel, LocalizationManager.Instance["RecordingFormatPrefix"], $"{codec} | {suffix}");
        }

        public void PerformClearAllData()
        {
            ShowLoadingOverlay("Clearing all application data...");
            try
            {
                var buttons = PadPanel.Children.OfType<RecordingPadButton>().Concat(FavoritesPanel.Children.OfType<RecordingPadButton>()).ToList();
                foreach (var b in buttons) b.StopPlayback();

                PadPanel.Children.Clear();
                FavoritesPanel.Children.Clear();

                var allPads = _recordingStore.GetAll();
                var ids = allPads.Select(p => p.Id).ToList();
                _recordingStore.DeleteAll(ids);
                _padCache.Clear();

                _recordingStore.CleanupAllTempFiles();
                _recordingStore.CleanupInternalTempRecordings();

                _settings.ResetToDefaults();
                _settings.Save();
                ApplySettings();
                UpdatePadState();
                Forget(RefreshStorageInfoAsync());

                SetStatus("All data cleared successfully.", "#FF4CAF50");
            }
            finally
            {
                HideLoadingOverlay();
            }
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
                string value = string.Format(LocalizationManager.Instance["StorageDataFiles"], count, FormatByteSize(dbBytes));
                SetInfoLabel(StorageInfoLabel, LocalizationManager.Instance["StorageDataPrefix"], value);
            }
            catch
            {
                SetInfoLabel(StorageInfoLabel, LocalizationManager.Instance["StorageDataPrefix"], LocalizationManager.Instance["StorageDataError"]);
            }
        }
        private void WhisperARTTStatus()
        {
            string nm = LocalizationManager.Instance["STTPrefix"];
            try
            {
                bool? arttsvalue = _settings.AutoRenameWithSpeech;
                if (arttsvalue == null)
                {
                    SetInfoLabel(WhisperStatusLabel, nm, LocalizationManager.Instance["STTUnavailable"]);
                    return;
                }
                else if (arttsvalue == true)
                {
                    string suffix = _settings.UseCudaForSpeech && Helpers.GpuHelper.IsCudaRuntimeAvailable ? " (CUDA)" : "";
                    SetInfoLabel(WhisperStatusLabel, nm, LocalizationManager.Instance["STTEnabled"] + suffix);
                }
                else
                {
                    SetInfoLabel(WhisperStatusLabel, nm, LocalizationManager.Instance["STTDisabled"]);
                    return;
                }
            }
            catch
            {
                SetInfoLabel(WhisperStatusLabel, nm, LocalizationManager.Instance["STTUnavailable"]);
                return;
            }
        }


        private async Task CheckForUpdateAsync()
        {
            try
            {
                var updateService = new UpdateService(_settings.DownloadBetaUpdates);
                var updateResult = await updateService.CheckForUpdateAsync();
                if (updateResult != null)
                {
                    UpdateNoticeLink.NavigateUri = UpdateService.ReleasesPageUri;
                    UpdateNoticeText.Visibility = Visibility.Visible;
                }
                else
                {
                    UpdateNoticeText.Visibility = Visibility.Collapsed;
                }
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

            CloseOwnedSecondaryWindows();
            Helpers.ZoomManager.ScaleChanged -= OnZoomScaleChanged;
            _trayIcon?.Dispose();
            _speechService?.Dispose();
            _hotkeyService.Dispose();
            _captureService.Dispose();
            _ipcServer?.Dispose();
            _recordingStore.CleanupAllTempFiles();
            _recordingStore.CleanupInternalTempRecordings();
            _recordingStore.Dispose();
            DiscordService.Instance.Dispose();
        }

        private void CloseOwnedSecondaryWindows()
        {
            try
            {
                if (_activeSettingsWindow != null && _activeSettingsWindow.IsLoaded)
                    _activeSettingsWindow.Close();
                if (_activeAboutWindow != null && _activeAboutWindow.IsLoaded)
                    _activeAboutWindow.Close();
                if (_activeGlobalEffectsWindow != null && _activeGlobalEffectsWindow.IsLoaded)
                    _activeGlobalEffectsWindow.Close();

                foreach (var win in _secondaryFolderWindows.Values.ToList())
                {
                    if (win.IsLoaded) win.Close();
                }
                _secondaryFolderWindows.Clear();
            }
            catch { }
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
                    if (files.Any(f => AudioImportService.IsSupportedExtensionOrDirectory(f)))
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
                    var supportedFiles = AudioImportService.ExpandAudioFiles(files);
                    if (supportedFiles.Count > 0)
                    {
                        e.Handled = true;
                        await ProcessAudioImportsAsync(supportedFiles);
                    }
                }
            }
        }

        private async Task ProcessAudioImportsAsync(IEnumerable<string> filePaths)
        {
            var filesList = AudioImportService.ExpandAudioFiles(filePaths);
            if (filesList.Count == 0) return;

            var importWindow = new AudioImportWindow(filesList)
            {
                Owner = this
            };

            bool? dialogResult = importWindow.ShowDialog();
            if (dialogResult != true || importWindow.ConvertedResults.Count == 0)
                return;

            int importedCount = 0;
            int failedCount = 0;

            foreach (var result in importWindow.ConvertedResults)
            {
                if (result.Success && result.AudioData.Length > 0)
                {
                    try
                    {
                        var entry = new RecordingEntry
                        {
                            DisplayName = result.DisplayName,
                            PadColor = result.PadColor,
                            Duration = result.Duration,
                            CreatedAt = DateTime.Now,
                            IsFavorite = false
                        };

                        string id = _recordingStore.Add(result.DisplayName, result.Codec, entry.Duration, entry.CreatedAt, result.AudioData);
                        entry.RecordingId = id;
                        if (!string.IsNullOrEmpty(result.PadColor))
                        {
                            _recordingStore.SetPadColor(id, result.PadColor);
                        }
                        entry.FilePath = _recordingStore.MaterializeToTemp(id, result.Codec);

                        if (_settings.AutoNormalizeOnCapture && File.Exists(entry.FilePath))
                        {
                            try
                            {
                                if (string.Equals(entry.Codec, "wav", StringComparison.OrdinalIgnoreCase))
                                {
                                    LoudnessNormalizer.NormalizeWavFile(entry.FilePath, entry.FilePath, _settings.TargetLoudnessLufs);
                                }
                                double newLufs = LoudnessNormalizer.MeasureIntegratedLoudness(entry.FilePath);
                                entry.LufsValue = newLufs;
                                _recordingStore.UpdateLufs(id, newLufs);
                            }
                            catch (Exception normEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"LUFS normalization skipped: {normEx.Message}");
                            }
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

            if (importedCount > 0)
            {
                SetStatus($"Successfully imported {importedCount} audio clip(s)", "#FF4CAF50");
            }
            if (failedCount > 0)
            {
                System.Windows.MessageBox.Show(this,
                    $"{failedCount} file(s) could not be saved to library.",
                    "Import Warning",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
        }

        private void LiveMicBtn_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (LiveMicBtn.IsChecked == true)
            {
                int liveMicIn = _settings.LiveMicDeviceIndex;
                int liveMicOut = _settings.LiveMicOutputDeviceIndex - 1;
                _liveMicModulator.Gain = (float)_settings.LiveMicGain;
                _liveMicModulator.IsFxEnabled = _settings.LiveMicFxEnabled;
                _liveMicModulator.Start(liveMicIn, liveMicOut, _settings.SecondaryOutputDeviceIndex, _settings.DualOutputEnabled);
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

        private void OnZoomScaleChanged(double scale)
        {
            Dispatcher.InvokeAsync(() =>
            {
                SetStatus($"Zoom: {Math.Round(scale * 100)}%", "#FF00E5FF");
            });
        }
    }
}
