using System;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using NAudio.Wave;
using Paddy.Controls;
using Paddy.Models;
using Paddy.Services;

namespace Paddy
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindow : Window
    {
        private readonly AudioCaptureService _captureService = new();
        private AppSettings _settings = AppSettings.Load();
        private List<(string Id, string Name)> _loopbackDevices = new();
        private int _outputDeviceIndex = 0;
        private bool _suppressSelectionEvents = true;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            ThresholdCanvas.SizeChanged += (_, _) => UpdateThresholdMarker();
        }

        // ── Startup ────────────────────────────────────────────────────────────
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateCaptureSourceModes();
            PopulateInputDevices();
            PopulateLoopbackDevices();
            PopulateOutputDevices();
            PopulateListenOutputDevices();
            ApplySettings();
            _suppressSelectionEvents = false;

            _captureService.RmsLevelChanged += OnRmsChanged;
            _captureService.RecordingCompleted += OnRecordingCompleted;
            _captureService.RecordingStateChanged += OnRecordingStateChanged;
        }

        private void PopulateCaptureSourceModes()
        {
            CaptureSourceCombo.Items.Clear();
            CaptureSourceCombo.Items.Add("Microphone");
            CaptureSourceCombo.Items.Add("Output loopback");
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
            _outputDeviceIndex = clampedOut - 1; // -1 = default device in WaveOutEvent
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

        private int GetCurrentListenDeviceIndex()
        {
            if (ListenOutputEnabledCheck.IsChecked == true)
                return ListenOutputDeviceCombo.SelectedIndex - 1; // -1 = default
            return -2; // disabled
        }

        private void RefreshPadOutputRouting()
        {
            int listenDevice = GetCurrentListenDeviceIndex();
            foreach (var child in PadPanel.Children)
            {
                if (child is RecordingPadButton pad)
                {
                    pad.OutputDeviceIndex = _outputDeviceIndex;
                    pad.ListenDeviceIndex = listenDevice;
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
            // ComboBox index 0 = default, 1..N = devices 0..N-1
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
            RmsMeter.Value = 0;
            RmsValueLabel.Text = "0";
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

        // ── Audio events (cross-thread) ────────────────────────────────────────
        private void OnRmsChanged(double value)
        {
            Dispatcher.InvokeAsync(() =>
            {
                RmsMeter.Value = value;
                RmsValueLabel.Text = value.ToString("0");
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
            Dispatcher.InvokeAsync(() => AddPadButton(entry));
        }

        // ── Pad panel ──────────────────────────────────────────────────────────
        private void AddPadButton(RecordingEntry entry)
        {
            var btn = new RecordingPadButton
            {
                Margin = new Thickness(6),
                OutputDeviceIndex = _outputDeviceIndex,
                ListenDeviceIndex = GetCurrentListenDeviceIndex()
            };
            btn.SetEntry(entry);
            btn.DeleteRequested += (s, _) =>
            {
                if (s is RecordingPadButton b) PadPanel.Children.Remove(b);
                UpdatePadState();
            };

            PadPanel.Children.Insert(0, btn); // newest first
            UpdatePadState();

            SetStatus($"Saved: {Path.GetFileName(entry.FilePath)}", "#FF4CAF50");
        }

        private void UpdatePadState()
        {
            int count = PadPanel.Children.Count;
            RecordingCountLabel.Text = count == 1 ? "1 clip" : $"{count} clips";
            EmptyHint.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (PadPanel.Children.Count == 0) return;
            var result = System.Windows.MessageBox.Show(
                "Remove all pad buttons? (Files on disk are NOT deleted.)",
                "Paddy", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            PadPanel.Children.Clear();
            UpdatePadState();
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private void SetStatus(string text, string hexColor)
        {
            StatusLabel.Text = text;
            StatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor));
        }

        private void UpdateThresholdMarker()
        {
            if (ThresholdCanvas == null || ThresholdLine == null) return;
            double width = ThresholdCanvas.ActualWidth;
            if (width <= 0) return;
            double pct = _captureService.Sensitivity / 100.0;
            Canvas.SetLeft(ThresholdLine, pct * width - 1);
        }

        // ── Shutdown ───────────────────────────────────────────────────────────
        private void MainWindow_Closing(object? sender,
            System.ComponentModel.CancelEventArgs e)
        {
            _captureService.Dispose();
        }
    }
}
