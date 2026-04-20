using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Controls;
using PaDDY.Helpers;
using PaDDY.Models;
using PaDDY.Services;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private readonly AudioCaptureService _captureService = new();
        private readonly GlobalHotkeyService _hotkeyService = new();
        private AppSettings _settings = AppSettings.Load();
        private readonly List<CaptureSourceMode> _captureSourceModes = new();
        private List<(string Id, string Name)> _loopbackDevices = new();
        private List<(uint ProcessId, string ProcessName)> _appLoopbackProcesses = new();
        private int _outputDeviceIndex = 0;
        private bool _suppressSelectionEvents = true;
        private bool _developerModeEnabled;
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

        // Meter decay animation (input)
        private System.Windows.Threading.DispatcherTimer? _meterDecayTimer;
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            ThresholdCanvas.SizeChanged += (_, _) => UpdateThresholdMarker();
            ThresholdCanvasR.SizeChanged += (_, _) => UpdateThresholdMarker();
            this.PreviewKeyDown += OnPadHotKey;
        }

        private void OnPadHotKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == (ModifierKeys.Control | ModifierKeys.Shift)
                && e.Key == Key.D)
            {
                e.Handled = true;
                ToggleDeveloperMode();
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
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateCaptureSourceModes();
            PopulateInputDevices();
            PopulateLoopbackDevices();
            PopulateAppLoopbackProcesses();
            PopulateOutputDevices();
            PopulateListenOutputDevices();
            PopulateRecordingModes();
            PopulateSortOrderCombo();
            ApplySettings();
            LoadFavoritesFromSettings();
            LoadNonFavoriteRecordingsFromDisk();
            _suppressSelectionEvents = false;

            _captureService.RmsLevelChanged += OnRmsChanged;
            _captureService.RecordingCompleted += OnRecordingCompleted;
            _captureService.RecordingStateChanged += OnRecordingStateChanged;
            _captureService.CodecCompatibilityWarning += OnCodecCompatibilityWarning;

            // Register global hotkey
            _hotkeyService.Register(this, _settings.BufferHotKeyModifiers, _settings.BufferHotKeyVk);
            _hotkeyService.HotkeyPressed += OnBufferHotkeyPressed;
        }

        private void PopulateCaptureSourceModes()
        {
            CaptureSourceCombo.Items.Clear();
            _captureSourceModes.Clear();

            AddCaptureSourceMode(CaptureSourceMode.Microphone, "Mic/Line input");
            AddCaptureSourceMode(CaptureSourceMode.OutputLoopback, "Output loopback");

            if (_developerModeEnabled)
                AddCaptureSourceMode(CaptureSourceMode.AppLoopback, "App loopback");
        }

        private void AddCaptureSourceMode(CaptureSourceMode mode, string label)
        {
            _captureSourceModes.Add(mode);
            CaptureSourceCombo.Items.Add(label);
        }

        private void ToggleDeveloperMode()
        {
            _developerModeEnabled = !_developerModeEnabled;

            var previousMode = GetSelectedCaptureMode();
            RefreshCaptureSourceModes(previousMode, persistSelection: true);

            string status = _developerModeEnabled
                ? "Developer mode enabled. App loopback is now visible."
                : "Developer mode disabled. App loopback is now hidden.";
            SetStatus(status, _developerModeEnabled ? "#FFFFB300" : "#FF757575");
        }

        private void RefreshCaptureSourceModes(CaptureSourceMode preferredMode, bool persistSelection)
        {
            bool wasSuppressing = _suppressSelectionEvents;
            _suppressSelectionEvents = true;

            PopulateCaptureSourceModes();

            CaptureSourceMode nextMode = preferredMode;
            if (!_captureSourceModes.Contains(nextMode))
                nextMode = _captureSourceModes.Contains(CaptureSourceMode.OutputLoopback)
                    ? CaptureSourceMode.OutputLoopback
                    : CaptureSourceMode.Microphone;

            CaptureSourceCombo.SelectedIndex = _captureSourceModes.IndexOf(nextMode);
            _suppressSelectionEvents = wasSuppressing;

            if (persistSelection)
            {
                _settings.CaptureSourceMode = (int)nextMode;
                _settings.Save();
                UpdateInputControlsForSource();
                RestartMonitoringIfActive();
            }
        }

        private void PopulateRecordingModes()
        {
            RecordingModeCombo.Items.Clear();
            RecordingModeCombo.Items.Add("Auto VAD");
            RecordingModeCombo.Items.Add("Key Buffer");
        }

        private static readonly string[] SortOrderLabels =
        {
            "Newest first",
            "Oldest first",
            "Name A\u2192Z",
            "Name Z\u2192A",
            "Longest",
            "Shortest"
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
                _ => buttons.OrderByDescending(b => b.Entry?.CreatedAt ?? DateTime.MinValue) // 0 = Newest first
            };

            PadPanel.Children.Clear();
            foreach (var btn in sorted)
                PadPanel.Children.Add(btn);
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
            int modeIdx = Math.Clamp(_settings.RecordingMode, 0, RecordingModeCombo.Items.Count - 1);
            RecordingModeCombo.SelectedIndex = modeIdx;
            _captureService.RecordingMode = (AudioRecordingMode)modeIdx;
            KeyBufferHint.Visibility = modeIdx == 1 ? Visibility.Visible : Visibility.Collapsed;
            UpdateHotkeyLabel();

            // Format settings
            _captureService.RecordSampleRate = _settings.RecordSampleRate;
            _captureService.RecordBitDepth = _settings.RecordBitDepth;
            _captureService.RecordChannels = _settings.RecordChannels;
            _captureService.RecordCodec = _settings.RecordCodec;
            _captureService.PastBufferDurationMs = _settings.PastBufferDurationMs;

            string folder = string.IsNullOrWhiteSpace(_settings.SaveFolder)
                ? Path.Combine(AppContext.BaseDirectory, "recordings")
                : _settings.SaveFolder;
            _captureService.SaveFolder = folder;

            _captureService.Sensitivity = _settings.Sensitivity;
            _captureService.SilenceTimeoutMs = _settings.SilenceTimeoutMs;

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
                        pad.OutputVolume = _outputVolume;
                        pad.ListenVolume = _padListenVolume;
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
                return;
            }

            if (_loopbackDevices.Count == 0)
                throw new InvalidOperationException("No active output devices available for loopback capture.");

            _captureService.Start(0, mode, GetSelectedLoopbackDeviceId());
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

        private void AppLoopbackCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvents) return;

            int idx = AppLoopbackCombo.SelectedIndex;
            if (idx >= 0 && idx < _appLoopbackProcesses.Count)
            {
                _settings.AppLoopbackProcessId = _appLoopbackProcesses[idx].ProcessId;
                _settings.Save();
            }
            RestartMonitoringIfActive();
        }

        private void RefreshAppLoopback_Click(object sender, RoutedEventArgs e)
        {
            PopulateAppLoopbackProcesses();
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
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Unable to start monitoring:\n{ex.Message}", "PaDDY",
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
            string folder = _captureService.SaveFolder;
            if (Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", folder);
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
            _settings.SaveFolder = win.SelectedSaveFolder;
            _settings.PastBufferDurationMs = win.SelectedBufferDurationMs;
            _settings.BufferHotKeyModifiers = win.SelectedHotKeyModifiers;
            _settings.BufferHotKeyVk = win.SelectedHotKeyVk;
            _settings.MaxRecords = win.SelectedMaxRecords;
            _settings.Save();

            _captureService.RecordCodec = win.SelectedCodec;
            _captureService.SaveFolder = win.SelectedSaveFolder;
            _captureService.PastBufferDurationMs = win.SelectedBufferDurationMs;

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

        private void UpdateOutputMeter(double left, double right)
        {
            Dispatcher.InvokeAsync(() =>
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

                var now = DateTime.UtcNow;
                if (dbL >= PeakThresholdDb)
                    _outputPeakHoldTimeL = now;
                if (dbR >= PeakThresholdDb)
                    _outputPeakHoldTimeR = now;

                OutputPeakIndicatorL.Background = (now - _outputPeakHoldTimeL).TotalSeconds < PeakHoldSeconds
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));
                OutputPeakIndicatorR.Background = (now - _outputPeakHoldTimeR).TotalSeconds < PeakHoldSeconds
                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
                    : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x44, 0x44, 0x44));

                // If both L and R are zero (playback stopped), start decay animation
                if (left <= 0 && right <= 0)
                    StartOutputMeterDecay();
            });
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

        private void OnCodecCompatibilityWarning(string message)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _settings.RecordCodec = "wav";
                _settings.Save();
                _captureService.RecordCodec = "wav";

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
                OutputVolume = _outputVolume,
                ListenVolume = _padListenVolume
            };
            btn.SetEntry(entry);
            btn.IsFavorite = entry.IsFavorite;

            btn.PlaybackRmsChanged += UpdateOutputMeter;

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

            btn.FileRenamed += (oldPath, newPath) =>
            {
                UpdateFavoritePathAfterRename(oldPath, newPath);
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
                    // Re-enforce limit: un-favoriting adds to PadPanel and may exceed MaxRecords
                    EnforceMaxRecords();
                }
                UpdatePadState();
            };

            btn.RecordingCopied += (copyPath, asFav) =>
            {
                if (!File.Exists(copyPath)) return;
                try
                {
                    using var reader = AudioReaderFactory.Open(copyPath);
                    var newEntry = new RecordingEntry
                    {
                        FilePath = copyPath,
                        Duration = reader.TotalTime,
                        CreatedAt = File.GetCreationTime(copyPath),
                        IsFavorite = asFav
                    };
                    AddPadButton(newEntry, asFav);
                }
                catch { }
            };

            btn.MouseEnter += (s, _) => _hoveredPad = s as RecordingPadButton;
            btn.MouseLeave += (_, _) => { if (_hoveredPad == btn) _hoveredPad = null; };

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
            {
                PadPanel.Children.Insert(0, btn);
                SortPadPanel();
            }

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
                    using var reader = AudioReaderFactory.Open(path);
                    var entry = new RecordingEntry
                    {
                        FilePath = path,
                        Duration = reader.TotalTime,
                        CreatedAt = File.GetCreationTime(path),
                        IsFavorite = true
                    };
                    var btn = CreatePadButton(entry);
                    FavoritesPanel.Children.Add(btn);
                }
                catch { /* skip unreadable files — do NOT remove from favorites */ }
            }

            // Clean up only files confirmed missing from disk
            foreach (var p in toRemove)
                _settings.FavoriteFilePaths.Remove(p);
            if (toRemove.Count > 0) _settings.Save();

            UpdatePadState();
        }

        private void LoadNonFavoriteRecordingsFromDisk()
        {
            string folder = _captureService.SaveFolder;
            if (!Directory.Exists(folder)) return;

            var favSet = new HashSet<string>(_settings.FavoriteFilePaths, StringComparer.OrdinalIgnoreCase);

            IEnumerable<FileInfo> files = new DirectoryInfo(folder)
                .EnumerateFiles("*.*")
                .Where(f => IsAudioFile(f.Name) && !favSet.Contains(f.FullName))
                .OrderByDescending(f => f.CreationTime); // initial load always newest-first; SortPadPanel applied after

            int max = _settings.MaxRecords;
            if (max > 0)
                files = files.Take(max);

            foreach (var fi in files)
            {
                try
                {
                    using var reader = AudioReaderFactory.Open(fi.FullName);
                    var entry = new RecordingEntry
                    {
                        FilePath = fi.FullName,
                        Duration = reader.TotalTime,
                        CreatedAt = fi.CreationTime,
                        IsFavorite = false
                    };
                    var btn = CreatePadButton(entry);
                    PadPanel.Children.Add(btn);
                }
                catch { /* skip unreadable files */ }
            }

            SortPadPanel();
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

        private void UpdateFavoritePathAfterRename(string oldPath, string newPath)
        {
            int idx = _settings.FavoriteFilePaths
                .FindIndex(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _settings.FavoriteFilePaths[idx] = newPath;
                _settings.Save();
            }
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

            // Sweep the save folder for any orphaned .wav files not represented as pads
            string saveFolder = _captureService.SaveFolder;
            if (Directory.Exists(saveFolder))
            {
                var protectedPaths = dlg.KeepFavorites
                    ? new HashSet<string>(_settings.FavoriteFilePaths, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var wav in Directory.EnumerateFiles(saveFolder, "*.*").Where(f => IsAudioFile(f)))
                {
                    if (protectedPaths.Contains(wav)) continue;
                    try { File.Delete(wav); } catch { }
                }
            }

            UpdatePadState();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static readonly HashSet<string> _audioExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".wav", ".mp3", ".opus", ".ogg" };

        private static bool IsAudioFile(string path) =>
            _audioExtensions.Contains(Path.GetExtension(path));

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

