using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using NAudio.Wave;
using Paddy.Controls;
using Paddy.Helpers;
using Paddy.Models;
using Paddy.Services;

namespace Paddy
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private readonly AudioCaptureService _captureService = new();
        private readonly GlobalHotkeyService _hotkeyService = new();
        private AppSettings _settings = AppSettings.Load();
        private List<(string Id, string Name)> _loopbackDevices = new();
        private int _outputDeviceIndex = 0;
        private bool _suppressSelectionEvents = true;

        // Peak hold state
        private const double PeakThresholdDb = -1.0;
        private const double PeakHoldSeconds = 1.5;
        private const double MeterMinDb = -60.0;
        private DateTime _peakHoldTimeL = DateTime.MinValue;
        private DateTime _peakHoldTimeR = DateTime.MinValue;

        // Meter decay animation
        private System.Windows.Threading.DispatcherTimer? _meterDecayTimer;
        private double _decayTargetL;
        private double _decayTargetR;
        private double _decayCurrentL;
        private double _decayCurrentR;
        private const int DecaySteps = 18; // ~288ms at 16ms/tick
        private int _decayStep;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            ThresholdCanvas.SizeChanged += (_, _) => UpdateThresholdMarker();
            ThresholdCanvasR.SizeChanged += (_, _) => UpdateThresholdMarker();
        }

        // ── Startup ────────────────────────────────────────────────────────────
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateCaptureSourceModes();
            PopulateInputDevices();
            PopulateLoopbackDevices();
            PopulateOutputDevices();
            PopulateListenOutputDevices();
            PopulateRecordingModes();
            ApplySettings();
            LoadFavoritesFromSettings();
            _suppressSelectionEvents = false;

            _captureService.RmsLevelChanged += OnRmsChanged;
            _captureService.RecordingCompleted += OnRecordingCompleted;
            _captureService.RecordingStateChanged += OnRecordingStateChanged;

            // Register global hotkey
            _hotkeyService.Register(this, _settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
            _hotkeyService.HotkeyPressed += OnBufferHotkeyPressed;
        }

        private void PopulateCaptureSourceModes()
        {
            CaptureSourceCombo.Items.Clear();
            CaptureSourceCombo.Items.Add("Microphone");
            CaptureSourceCombo.Items.Add("Output loopback");
        }

        private void PopulateRecordingModes()
        {
            RecordingModeCombo.Items.Clear();
            RecordingModeCombo.Items.Add("Auto VAD");
            RecordingModeCombo.Items.Add("Key Buffer");
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

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                OutputDeviceCombo.Items.Add(caps.ProductName);
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

            for (int i = 0; i < WaveOut.DeviceCount; i++)
            {
                var caps = WaveOut.GetCapabilities(i);
                ListenOutputDeviceCombo.Items.Add(caps.ProductName);
            }

            int clamped = Math.Clamp(_settings.ListenOutputDeviceIndex, 0,
                ListenOutputDeviceCombo.Items.Count - 1);
            ListenOutputDeviceCombo.SelectedIndex = clamped;
            ListenOutputDeviceCombo.IsEnabled = _settings.ListenOutputEnabled;
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

        private void ApplySettings()
        {
            int sourceMode = Math.Clamp(_settings.CaptureSourceMode, 0, CaptureSourceCombo.Items.Count - 1);
            CaptureSourceCombo.SelectedIndex = sourceMode;
            ListenOutputEnabledCheck.IsChecked = _settings.ListenOutputEnabled;

            SensitivitySlider.Value = _settings.Sensitivity;
            SilenceSlider.Value = _settings.SilenceTimeoutMs;

            // Recording mode combo
            int modeIdx = Math.Clamp(_settings.RecordingMode, 0, RecordingModeCombo.Items.Count - 1);
            RecordingModeCombo.SelectedIndex = modeIdx;
            _captureService.RecordingMode = (AudioRecordingMode)modeIdx;
            KeyBufferHint.Visibility = modeIdx == 1 ? Visibility.Visible : Visibility.Collapsed;
            UpdateHotkeyLabel();

            // Format settings
            _captureService.RecordSampleRate = _settings.RecordSampleRate;
            _captureService.RecordBitDepth = _settings.RecordBitDepth;
            _captureService.RecordChannels = _settings.RecordChannels;
            _captureService.PastBufferDurationMs = _settings.PastBufferDurationMs;

            string folder = string.IsNullOrWhiteSpace(_settings.SaveFolder)
                ? Path.Combine(AppContext.BaseDirectory, "recordings")
                : _settings.SaveFolder;
            _captureService.SaveFolder = folder;
            FolderLabel.Text = folder;

            _captureService.Sensitivity = _settings.Sensitivity;
            _captureService.SilenceTimeoutMs = _settings.SilenceTimeoutMs;

            UpdateInputControlsForSource();
            ListenOutputDeviceCombo.IsEnabled = _settings.ListenOutputEnabled;
            RefreshPadOutputRouting();
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
                    }
                }
            }
        }

        private CaptureSourceMode GetSelectedCaptureMode()
        {
            return CaptureSourceCombo.SelectedIndex == 1
                ? CaptureSourceMode.OutputLoopback
                : CaptureSourceMode.Microphone;
        }

        private string? GetSelectedLoopbackDeviceId()
        {
            int index = LoopbackDeviceCombo.SelectedIndex;
            if (index < 0 || index >= _loopbackDevices.Count) return null;
            return _loopbackDevices[index].Id;
        }

        private void UpdateInputControlsForSource()
        {
            bool useLoopback = GetSelectedCaptureMode() == CaptureSourceMode.OutputLoopback;

            InputDeviceLabel.Visibility = useLoopback ? Visibility.Collapsed : Visibility.Visible;
            InputDeviceCombo.Visibility = useLoopback ? Visibility.Collapsed : Visibility.Visible;
            LoopbackDeviceLabel.Visibility = useLoopback ? Visibility.Visible : Visibility.Collapsed;
            LoopbackDeviceCombo.Visibility = useLoopback ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RestartMonitoringIfActive()
        {
            if (MonitorToggle.IsChecked != true) return;
            _captureService.Stop();
            StartMonitoringWithCurrentSelection();
        }

        private void StartMonitoringWithCurrentSelection()
        {
            var mode = GetSelectedCaptureMode();
            if (mode == CaptureSourceMode.Microphone)
            {
                _captureService.Start(Math.Max(0, InputDeviceCombo.SelectedIndex), mode, null);
                SetStatus("Listening…", "#FF4CAF50");
                return;
            }

            if (_loopbackDevices.Count == 0)
                throw new InvalidOperationException("No active output devices available for loopback capture.");

            _captureService.Start(0, mode, GetSelectedLoopbackDeviceId());
            // Loopback format detected lazily from first audio frame
            SetStatus("Monitoring output loopback…", "#FF4CAF50");
        }

        // ── Device selection ───────────────────────────────────────────────────
        private void InputDeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;
            _settings.InputDeviceIndex = InputDeviceCombo.SelectedIndex;
            _settings.Save();
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
            RestartMonitoringIfActive();
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
            RefreshPadOutputRouting();
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
            _captureService.RecordingMode = (AudioRecordingMode)idx;
            _settings.RecordingMode = idx;
            _settings.Save();
            KeyBufferHint.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Monitoring toggle ──────────────────────────────────────────────────
        private void MonitorToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (GetSelectedCaptureMode() == CaptureSourceMode.Microphone && WaveInEvent.DeviceCount == 0)
            {
                System.Windows.MessageBox.Show("No microphone detected.", "Paddy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
                return;
            }

            if (GetSelectedCaptureMode() == CaptureSourceMode.OutputLoopback && _loopbackDevices.Count == 0)
            {
                System.Windows.MessageBox.Show("No active output device found for loopback capture.", "Paddy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
                return;
            }

            try
            {
                StartMonitoringWithCurrentSelection();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Unable to start monitoring:\n{ex.Message}", "Paddy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                MonitorToggle.IsChecked = false;
            }
        }

        private void MonitorToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            _captureService.Stop();
            SetStatus("Idle — press Start to begin", "#FF757575");
            RmsValueLabel.Text = "-∞";
            RmsValueLabelR.Text = "-∞";
            PeakIndicatorL.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
            PeakIndicatorR.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
            StartMeterDecay();
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

        // ── Folder selection ───────────────────────────────────────────────────
        [SupportedOSPlatform("windows")]
        private void ChangeFolderButton_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select folder to save recordings",
                UseDescriptionForTitle = true,
                SelectedPath = _captureService.SaveFolder
            };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _captureService.SaveFolder = dlg.SelectedPath;
            FolderLabel.Text = dlg.SelectedPath;
            _settings.SaveFolder = dlg.SelectedPath;
            _settings.Save();
        }

        // ── Settings / About buttons ───────────────────────────────────────────
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings) { Owner = this };
            if (win.ShowDialog() != true) return;

            // Apply changes
            _settings.RecordSampleRate = win.SelectedSampleRate;
            _settings.RecordBitDepth = win.SelectedBitDepth;
            _settings.RecordChannels = win.SelectedChannels;
            _settings.SaveFolder = win.SelectedSaveFolder;
            _settings.PastBufferDurationMs = win.SelectedBufferDurationMs;
            _settings.BufferHotKeyModifiers = win.SelectedHotKeyModifiers;
            _settings.BufferHotKeyVk = win.SelectedHotKeyVk;
            _settings.MaxRecords = win.SelectedMaxRecords;
            _settings.Save();

            _captureService.RecordSampleRate = win.SelectedSampleRate;
            _captureService.RecordBitDepth = win.SelectedBitDepth;
            _captureService.RecordChannels = win.SelectedChannels;
            _captureService.SaveFolder = win.SelectedSaveFolder;
            _captureService.PastBufferDurationMs = win.SelectedBufferDurationMs;
            FolderLabel.Text = win.SelectedSaveFolder;

            // Re-register hotkey with new key
            _hotkeyService.Reregister(this, _settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
            UpdateHotkeyLabel();

            // Restart monitoring to apply new format settings
            RestartMonitoringIfActive();
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
            Dispatcher.InvokeAsync(() =>
            {
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
                var now = DateTime.UtcNow;
                if (dbL >= PeakThresholdDb)
                    _peakHoldTimeL = now;
                if (dbR >= PeakThresholdDb)
                    _peakHoldTimeR = now;

                PeakIndicatorL.Background = (now - _peakHoldTimeL).TotalSeconds < PeakHoldSeconds
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
                PeakIndicatorR.Background = (now - _peakHoldTimeR).TotalSeconds < PeakHoldSeconds
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
            });
        }

        private void OnRecordingStateChanged(bool isRecording)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (isRecording)
                    SetStatus("Recording…", "#FFEF5350");
                else
                    SetStatus("Listening…", "#FF4CAF50");
            });
        }

        private void OnRecordingCompleted(RecordingEntry entry)
        {
            Dispatcher.InvokeAsync(() => AddPadButton(entry, toFavorites: false));
        }

        // ── Pad panel ──────────────────────────────────────────────────────────
        private RecordingPadButton CreatePadButton(RecordingEntry entry)
        {
            var btn = new RecordingPadButton
            {
                Margin = new Thickness(6),
                OutputDeviceIndex = _outputDeviceIndex,
                ListenDeviceIndex = GetCurrentListenDeviceIndex()
            };
            btn.SetEntry(entry);
            btn.IsFavorite = entry.IsFavorite;

            btn.DeleteRequested += (s, _) =>
            {
                if (s is RecordingPadButton b)
                {
                    // Remove from whichever panel it's in
                    PadPanel.Children.Remove(b);
                    FavoritesPanel.Children.Remove(b);
                    if (b.IsFavorite && b.Entry != null)
                        RemoveFavoriteFromSettings(b.Entry.FilePath);
                    UpdatePadState();
                }
            };

            btn.FavoriteToggled += (s, _) =>
            {
                if (s is not RecordingPadButton b || b.Entry == null) return;
                if (b.IsFavorite)
                {
                    // Move from PadPanel to FavoritesPanel
                    PadPanel.Children.Remove(b);
                    FavoritesPanel.Children.Insert(0, b);
                    AddFavoriteToSettings(b.Entry.FilePath);
                }
                else
                {
                    // Move from FavoritesPanel to PadPanel
                    FavoritesPanel.Children.Remove(b);
                    PadPanel.Children.Insert(0, b);
                    RemoveFavoriteFromSettings(b.Entry.FilePath);
                }
                UpdatePadState();
            };

            return btn;
        }

        private void AddPadButton(RecordingEntry entry, bool toFavorites)
        {
            bool isFav = _settings.FavoriteFilePaths.Contains(entry.FilePath);
            entry.IsFavorite = isFav || toFavorites;

            var btn = CreatePadButton(entry);

            if (entry.IsFavorite)
                FavoritesPanel.Children.Insert(0, btn);
            else
                PadPanel.Children.Insert(0, btn);

            EnforceMaxRecords();
            UpdatePadState();
            SetStatus($"Saved: {Path.GetFileName(entry.FilePath)}", "#FF4CAF50");
        }

        private void LoadFavoritesFromSettings()
        {
            var toRemove = new List<string>();
            foreach (var path in _settings.FavoriteFilePaths)
            {
                if (!File.Exists(path))
                {
                    toRemove.Add(path);
                    continue;
                }
                try
                {
                    using var reader = new NAudio.Wave.AudioFileReader(path);
                    var entry = new RecordingEntry
                    {
                        FilePath = path,
                        Duration = reader.TotalTime,
                        IsFavorite = true
                    };
                    var btn = CreatePadButton(entry);
                    FavoritesPanel.Children.Add(btn);
                }
                catch { toRemove.Add(path); }
            }

            // Clean up missing files
            foreach (var p in toRemove)
                _settings.FavoriteFilePaths.Remove(p);
            if (toRemove.Count > 0) _settings.Save();

            UpdatePadState();
        }

        private void AddFavoriteToSettings(string filePath)
        {
            if (!_settings.FavoriteFilePaths.Contains(filePath))
            {
                _settings.FavoriteFilePaths.Add(filePath);
                _settings.Save();
            }
        }

        private void RemoveFavoriteFromSettings(string filePath)
        {
            _settings.FavoriteFilePaths.Remove(filePath);
            _settings.Save();
        }

        private void UpdatePadState()
        {
            int count = PadPanel.Children.Count;
            RecordingCountLabel.Text = count == 1 ? "1 clip" : $"{count} clips";
            EmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int favCount = FavoritesPanel.Children.Count;
            FavoriteCountLabel.Text = $" — {favCount}";
            var favVis = favCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            FavoritesHeader.Visibility = favVis;
            FavoritesPanelBorder.Visibility = favVis;
        }

        /// <summary>
        /// Removes the oldest non-favorite recordings from PadPanel when MaxRecords is exceeded.
        /// </summary>
        private void EnforceMaxRecords()
        {
            int max = _settings.MaxRecords;
            if (max <= 0) return; // unlimited

            while (PadPanel.Children.Count > max)
            {
                // Find oldest by CreatedAt among PadPanel children
                RecordingPadButton? oldest = null;
                foreach (var child in PadPanel.Children.OfType<RecordingPadButton>())
                {
                    if (child.Entry == null) continue;
                    if (oldest == null || child.Entry.CreatedAt < oldest.Entry!.CreatedAt)
                        oldest = child;
                }
                if (oldest == null) break;

                oldest.StopPlayback();
                if (oldest.Entry?.FilePath is string fp)
                    try { File.Delete(fp); } catch { }
                PadPanel.Children.Remove(oldest);
            }
        }

        // ── Clear / Delete All ─────────────────────────────────────────────────
        private void ClearPadsButton_Click(object sender, RoutedEventArgs e)
        {
            if (PadPanel.Children.Count == 0) return;
            PadPanel.Children.Clear();
            UpdatePadState();
        }

        private void DeleteAllFilesButton_Click(object sender, RoutedEventArgs e)
        {
            int total = PadPanel.Children.Count + FavoritesPanel.Children.Count;
            if (total == 0) return;

            var dlg = new DeleteAllDialog { Owner = this };
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

            foreach (var btn in toDelete)
            {
                btn.StopPlayback();
                if (btn.Entry?.FilePath is string fp)
                    try { File.Delete(fp); } catch { }

                PadPanel.Children.Remove(btn);
                if (!dlg.KeepFavorites)
                {
                    FavoritesPanel.Children.Remove(btn);
                    if (btn.Entry != null)
                        _settings.FavoriteFilePaths.Remove(btn.Entry.FilePath);
                }
            }

            if (!dlg.KeepFavorites)
            {
                _settings.FavoriteFilePaths.Clear();
                _settings.Save();
            }

            UpdatePadState();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
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

        // ── Shutdown ───────────────────────────────────────────────────────────
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _hotkeyService.Dispose();
            _captureService.Dispose();
        }
    }
}

