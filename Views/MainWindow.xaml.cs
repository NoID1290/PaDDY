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
        private readonly Dictionary<string, RecordingPadButton> _hiddenPads = new();
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
        private bool _overlayDevUnlocked = false;
        private TcpIpcServer? _ipcServer;
        private bool _isRecording;

        public void ShowLoadingOverlay(string message = "Processing...")
        {
            Dispatcher.Invoke(() => MainLoadingOverlay.Show(message));
        }

        public void HideLoadingOverlay()
        {
            Dispatcher.Invoke(() => MainLoadingOverlay.Hide());
        }
        private bool _performanceMode;
        private DateTime _lastInputMeterTick;
        private DateTime _lastOutputMeterTick;
        private DateTime _lastMonitorMeterTick;
        private static readonly SolidColorBrush PeakHotBrush = new(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36));
        private static readonly SolidColorBrush PeakColdBrush = new(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
        private static readonly SolidColorBrush InfoLabelPrefixBrush = new(System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x88));
        private static readonly SolidColorBrush InfoLabelValueBrush = new(System.Windows.Media.Color.FromRgb(0x90, 0x90, 0xB8));

        static MainWindow() 
        { 
            PeakHotBrush.Freeze(); 
            PeakColdBrush.Freeze(); 
            InfoLabelPrefixBrush.Freeze(); 
            InfoLabelValueBrush.Freeze();
        }

        private void SetInfoLabel(TextBlock label, string prefix, string value)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.InvokeAsync(() => SetInfoLabel(label, prefix, value));
                return;
            }
            label.Inlines.Clear();
            label.Inlines.Add(new System.Windows.Documents.Run(prefix) { Foreground = InfoLabelPrefixBrush });
            label.Inlines.Add(new System.Windows.Documents.Run(value) { Foreground = InfoLabelValueBrush });
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
            if (_startHiddenInTray)
            {
                // Open minimized (and without stealing focus) so no black/unpainted
                // window flashes on screen, but keep the taskbar entry so the user can
                // still find and restore the app from the taskbar.
                ShowActivated = false;
                WindowState = WindowState.Minimized;
                _initialTrayMinimize = true;
            }

            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            StateChanged += MainWindow_StateChanged;
            ThresholdCanvas.SizeChanged += (_, _) =>
            {
                UpdateThresholdMarker();
                Helpers.ThemeManager.UpdateMeterSkinSize(ThresholdCanvas.ActualWidth);
            };
            ThresholdCanvasR.SizeChanged += (_, _) => UpdateThresholdMarker();
            this.PreviewKeyDown += OnPadHotKey;
            PadMonitorMeterHostL.SizeChanged += (_, _) => UpdatePadMonitorMeter(0, 0);
            PadMonitorMeterHostR.SizeChanged += (_, _) => UpdatePadMonitorMeter(0, 0);
        }

        // ── Custom Window Chrome ───────────────────────────────────────────────
        private void ChromeMinimize_Click(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void ChromeMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                SystemCommands.RestoreWindow(this);
                ChromeMaxIcon.Text = "\u2610"; // □
                ChromeMaxRestoreBtn.ToolTip = "Maximize";
            }
            else
            {
                SystemCommands.MaximizeWindow(this);
                ChromeMaxIcon.Text = "\u2750"; // ❐ (restore icon)
                ChromeMaxRestoreBtn.ToolTip = "Restore";
            }
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e)
            => SystemCommands.CloseWindow(this);

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
            var isD = e.Key == Key.D || (e.Key == Key.System && e.SystemKey == Key.D);
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt) && isD)
            {
                e.Handled = true;
                _overlayDevUnlocked = !_overlayDevUnlocked;
                OverlayConfigPanel.Visibility = _overlayDevUnlocked ? Visibility.Visible : Visibility.Collapsed;
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
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ShowLoadingOverlay("Starting PaDDY...");
            // Yield to the UI thread so the loading overlay can actually render before we block it
            await Task.Delay(50);

            PopulateCaptureSourceModes();
            PopulateInputDevices();
            PopulateLoopbackDevices();
            PopulateAppLoopbackProcesses();
            PopulateOutputDevices();
            PopulateListenOutputDevices();
            PopulateRecordingModes();
            PopulateSortOrderCombo();
            ApplySettings();
            InitializePadPages();
            RecordingPadButton.SuppressEntranceAnimation++;
            LoadFavoritesFromStore();
            LoadNonFavoritesFromStore();
            RecordingPadButton.SuppressEntranceAnimation--;
            _suppressSelectionEvents = false;

            _captureService.RmsLevelChanged += OnRmsChanged;
            _captureService.RecordingCompleted += OnRecordingCompleted;
            _captureService.RecordingStateChanged += OnRecordingStateChanged;
            _captureService.CodecCompatibilityWarning += OnCodecCompatibilityWarning;

            //_overlayEngine.DiagnosticEvent += OverlayEngine_DiagnosticEvent;  // NOT READY YET! CAN BE CALL WITH DEV KEY BUT NEED TO BE UNCOMMENT
            /*
            _overlayEngine.Initialize(BuildOverlayOptions());
            if (_settings.OverlayEnabled && _settings.AppLoopbackProcessId != 0)
            {
                _overlayEngine.AttachToProcess(_settings.AppLoopbackProcessId);
                _overlayEngine.Show();
            }
            */


            RefreshOutputFormatInfo();
            RefreshInputFormatInfo();
            WhisperARTTStatus();
            Forget(RefreshStorageInfoAsync());
            _ = CheckForUpdateAsync();

            // Register global hotkey
            _hotkeyService.Register(this, _settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
            _hotkeyService.HotkeyPressed += OnBufferHotkeyPressed;

            InitializeTrayIcon();

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
                    ReloadRecordingDataFromDisk();
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
            // The pad has already been live-moved into place; commit happens in FinalizePadDrop.
            e.Handled = true;
        }

        private void PadPanel_Drop(object sender, System.Windows.DragEventArgs e)
        {
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

        /// <summary>Moves a pad to a specific pad page (folder tab) target.</summary>
        private void MovePadToPage(RecordingPadButton pad, string pageId)
        {
            if (pad.Entry == null) return;

            var page = _settings.PadPages.FirstOrDefault(p => p.Id == pageId);
            bool toFavoritesPage = page != null && page.IsFavorites;

            pad.IsFavorite = true;
            pad.Entry.IsFavorite = true;
            _recordingStore.SetFavorite(pad.Entry.RecordingId, true);
            _recordingStore.SetPadPage(pad.Entry.RecordingId, toFavoritesPage ? string.Empty : pageId);

            // The pad now belongs to another page; remove it from the current view.
            (pad.Parent as System.Windows.Controls.Panel)?.Children.Remove(pad);
            PersistFavoritesOrder();
            UpdatePadState();
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
                _overlayEngine.Hide();
                _overlayEngine.Detach();
                return;
            }

            if (processId == 0)
            {
                _overlayEngine.Hide();
                _overlayEngine.Detach();
                return;
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

            // Appearance
            _settings.Theme = win.SelectedTheme;
            _settings.MeterSkin = win.SelectedMeterSkin;
            _settings.PerformanceMode = win.SelectedPerformanceMode;

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
            _settings.Save();

            App.ApplyFont(win.SelectedFontVariant);
            Helpers.ThemeManager.ApplyTheme(_settings.Theme);
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

        private void UpdateOutputMeter(double left, double right)
        {
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

        private void UpdatePadMonitorMeter(double left, double right)
        {
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
            });
            BroadcastIpcState();
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
                        id = _recordingStore.Add(displayName, codec, entry.Duration, entry.CreatedAt, audioBytes);
                        materializedPath = _recordingStore.MaterializeToTemp(id, codec);
                    }

                    try { File.Delete(entry.FilePath); } catch { }

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        entry.RecordingId = id;
                        entry.DisplayName = displayName;
                        entry.FilePath = materializedPath;
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
                        _recordingStore.Delete(b.Entry.RecordingId);
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
                    byte[] updated = File.ReadAllBytes(entry.FilePath);
                    _recordingStore.UpdateAudioData(entry.RecordingId, updated);
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

        public void ReloadRecordingDataFromDisk()
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
            ThemeManager.ApplyMeterSkin(_settings.MeterSkin, _settings.MeterDigitalDots);
            ThemeManager.ApplyPerformanceMode(_settings.PerformanceMode);
            App.ApplyFont(_settings.AppFontVariant);
            _suppressSelectionEvents = false;

            InitializePadPages();
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
            _settings.PadPages.RemoveAll(p => p.Id == deletedId);
            var favorites = _settings.EnsurePadPages();
            _settings.ActivePadPageId = favorites.Id;
            _settings.Save();
            SwitchToPadPage(favorites.Id);
        }

        private void LoadFavoritesFromStore()
        {
            var records = _recordingStore.GetAll();
            var favs = records
                .Where(r => r.IsFavorite && BelongsToActivePage(r))
                .OrderBy(r => r.SortOrder)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();
            foreach (var rec in favs)
            {
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
                        IsFavorite = true,
                        SortOrder = rec.SortOrder
                    };
                    var btn = CreatePadButton(entry);
                    FavoritesPanel.Children.Add(btn);
                }
                catch { /* skip unreadable records */ }
            }
            UpdatePadState();
        }

        /// <summary>True when a favourite recording should appear on the active pad page.</summary>
        private bool BelongsToActivePage(RecordingRecord rec)
        {
            if (!rec.IsFavorite) return false;
            string pp = rec.PadPage ?? string.Empty;
            if (_activePadPage == null || _activePadPage.IsFavorites)
                return pp.Length == 0 || _activePadPage == null || pp == _activePadPage.Id;
            return pp == _activePadPage.Id;
        }

        private void LoadNonFavoritesFromStore()
        {
            var records = _recordingStore.GetAll();
            int max = _settings.MaxRecords;
            int count = 0;

            // GetAll() returns newest-first from DB
            foreach (var rec in records)
            {
                if (rec.IsFavorite) continue;
                if (max > 0 && count >= max) break;
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
                        IsFavorite = false,
                        SortOrder = rec.SortOrder
                    };
                    var btn = CreatePadButton(entry);
                    PadPanel.Children.Add(btn);
                    count++;
                }
                catch { /* skip unreadable records */ }
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
            FavoritesHeader.Visibility = (hasFavorites || hasExtraPages) ? Visibility.Visible : Visibility.Collapsed;

            if (!hasFavorites)
            {
                FavoritesPanelBorder.Visibility = Visibility.Collapsed;
                FavoritesCollapseIcon.Text = "▼";
                FavoritesCollapseButton.ToolTip = "Expand favorites";
                return;
            }

            bool isCollapsed = _settings.FavoritesPanelCollapsed;
            FavoritesPanelBorder.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            FavoritesCollapseIcon.Text = isCollapsed ? "►" : "▼";
            FavoritesCollapseButton.ToolTip = isCollapsed ? "Expand favorites" : "Collapse favorites";
        }

        private void FavoritesCollapseButton_Click(object sender, RoutedEventArgs e)
        {
            if (FavoritesPanel.Children.Count == 0) return;

            _settings.FavoritesPanelCollapsed = !_settings.FavoritesPanelCollapsed;
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
                    _recordingStore.Delete(pad.Entry.RecordingId);
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
                long dbBytes = await Task.Run(() => _recordingStore.GetStoreSizeBytes());
                string root = Path.GetPathRoot(RecordingStore.StorePath) ?? string.Empty;
                string value;
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var drive = new DriveInfo(root);
                    value = $"{FormatByteSize(dbBytes)} | {FormatByteSize(drive.AvailableFreeSpace)} free";
                }
                else
                {
                    value = $"{FormatByteSize(dbBytes)}";
                }
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
            StatusDot.Fill = new SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
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
                                    var pad = PadPanel.Children.OfType<RecordingPadButton>().FirstOrDefault(p => p.Entry?.RecordingId == padId) ??
                                              FavoritesPanel.Children.OfType<RecordingPadButton>().FirstOrDefault(p => p.Entry?.RecordingId == padId);
                                    if (pad != null)
                                    {
                                        pad.TogglePlay();
                                    }
                                    else
                                    {
                                        if (!_hiddenPads.TryGetValue(padId, out var hiddenPad))
                                        {
                                            var record = _recordingStore.GetAll().FirstOrDefault(r => r.Id == padId);
                                            if (record != null)
                                            {
                                                string tempPath = _recordingStore.MaterializeToTemp(record.Id, record.Codec);
                                                var entry = new RecordingEntry
                                                {
                                                    RecordingId = record.Id,
                                                    FilePath = tempPath
                                                };
                                                hiddenPad = CreatePadButton(entry);
                                                _hiddenPads[padId] = hiddenPad;
                                            }
                                        }
                                        hiddenPad?.TogglePlay();
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
        }
    }
}
