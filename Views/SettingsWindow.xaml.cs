using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Input;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Helpers;
using PaDDY.Services;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private List<(string Value, string Label)> _visibleCodecOptions = new();

        // Whether the user confirmed (OK) vs cancelled/closed
        public bool Confirmed { get; private set; }

        // Resolved output values
        public string SelectedCodec { get; private set; } = "wav";
        public int SelectedBufferDurationMs { get; private set; }
        public uint SelectedHotKeyModifiers { get; private set; }
        public uint SelectedHotKeyVk { get; private set; }
        public int SelectedMaxRecords { get; private set; }
        public string SelectedFontVariant { get; private set; } = "condensed";
        public string SelectedDefaultPadTitleTemplate { get; private set; } = "Recording {timestamp}";
        public bool SelectedUseFocusedAppForPadTitle { get; private set; }
        public int SelectedTrimEditorOutputDeviceIndex { get; private set; }
        public AudioEngineType SelectedAudioEngine { get; private set; } = AudioEngineType.NAudio;
        public int SelectedLiveMicOutputDeviceIndex { get; private set; }
        public bool SelectedNewRecordingsNonDestructive { get; private set; }

        // Appearance / system
        public double SelectedUiScale { get; private set; } = 1.0;
        private double _originalUiScale = 1.0;
        private bool _suppressZoomSliderEvents = true;
        public string SelectedLanguage { get; private set; } = "en";
        public string SelectedTheme { get; private set; } = "dark";
        public string SelectedMeterSkin { get; private set; } = "default";
        public bool SelectedPerformanceMode { get; private set; }
        public bool SelectedPauseAnimationsWhenUnfocused { get; private set; }
        public bool SelectedPreloadAudioCache { get; private set; }
        public bool SelectedMinimizeToTray { get; private set; }
        public bool SelectedCloseToTray { get; private set; }
        public bool SelectedStartMinimizedInTray { get; private set; }
        public bool SelectedRunOnWindowsStartup { get; private set; }
        public int SelectedDetectionAlgorithm { get; private set; }
        public bool SelectedAutoRenameWithSpeech { get; private set; }
        public bool SelectedCancelRecordingIfNoVoice { get; private set; }
        public string SelectedSpeechModel { get; private set; } = "tiny";
        public string SelectedSpeechLanguage { get; private set; } = "en";
        public bool SelectedUseCudaForSpeech { get; private set; }
        public bool SelectedDiscordRichPresenceEnabled { get; private set; }
        public long SelectedDiscordClientId { get; private set; }
        public bool SelectedAutoInstallUpdates { get; private set; }
        public bool SelectedDownloadBetaUpdates { get; private set; }

        // Global Effects
        public bool SelectedGlobalFadeEnabled { get; private set; }
        public double SelectedGlobalFadeInDurationMs { get; private set; } = 500.0;
        public double SelectedGlobalFadeOutDurationMs { get; private set; } = 500.0;
        public bool SelectedAllowMultiPadPlayback { get; private set; } = true;

        private static readonly (string Value, string Label)[] CodecOptions =
        {
            ("wav",  "WAV (LCPM FORMAT)"),
            ("mp3",  "MP3 (LAME)"),
            ("aac",  "AAC (.m4a)"),
            ("opus", "Opus (.opus)"),
            ("ogg",  "Ogg Vorbis (.ogg)"),
            ("flac", "FLAC (lossless)"),
        };

        private static readonly Dictionary<string, string> CodecDescriptions = new()
        {
            ["wav"] = "Lossless \u00b7 Raw audio LCPM format.",
            ["mp3"] = "Lossy \u00b7 Old, but still widely supported.",
            ["aac"] = "Lossy \u00b7 High quality, widely compatible (Apple, YouTube, streaming).",
            ["opus"] = "Lossy \u00b7 Optimised for voice.",
            ["ogg"] = "Lossy \u00b7 High efficiency, provides better audio quality than MP3.",
            ["flac"] = "Lossless \u00b7 Raw quality, better size than WAV.",
        };

        private uint _capturedVk;
        private bool _capturingKey;
        private bool _isChangingNonDestructiveGlobal;

        private readonly ObservableCollection<VstPluginListItem> _vst2PluginItems = new();

        private class VstPluginListItem
        {
            public string FullPath { get; set; } = string.Empty;
            public string Name => System.IO.Path.GetFileName(FullPath);
            public string SizeText { get; set; } = "--";
        }

        // Win32 ModKey flags
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            _originalUiScale = _settings.UiScale;
            SelectedUiScale = _settings.UiScale;
            _suppressZoomSliderEvents = true;
            InitializeComponent();

            Vst2PluginsListBox.ItemsSource = _vst2PluginItems;
            Vst2PluginsListBox.SelectionChanged += (_, _) => DeleteVst2Button.IsEnabled = Vst2PluginsListBox.SelectedItem != null;

            VstSettingsPanel.Visibility = Visibility.Visible;
            Vst3PluginRow.Visibility = App.IsDebugMode ? Visibility.Visible : Visibility.Collapsed;
            App.DebugModeChanged += OnDebugModeChanged;

            PaDDY.Services.SpeechRecognitionService.DownloadProgressUpdated += OnSpeechModelDownloadProgressUpdated;
            PaDDY.Services.CudaManager.DownloadProgressUpdated += OnCudaDownloadProgressUpdated;
            ZoomManager.ScaleChanged += OnGlobalZoomScaleChanged;

            Loaded += OnLoaded;
            Closed += (_, _) =>
            {
                App.DebugModeChanged -= OnDebugModeChanged;
                PaDDY.Services.SpeechRecognitionService.DownloadProgressUpdated -= OnSpeechModelDownloadProgressUpdated;
                PaDDY.Services.CudaManager.DownloadProgressUpdated -= OnCudaDownloadProgressUpdated;
                ZoomManager.ScaleChanged -= OnGlobalZoomScaleChanged;
                if (!Confirmed)
                {
                    ZoomManager.SetScale(_originalUiScale, saveSettings: false);
                }
            };
        }

        private void OnDebugModeChanged()
        {
            VstSettingsPanel.Visibility = Visibility.Visible;
            Vst3PluginRow.Visibility = App.IsDebugMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PopulateVersionAndDependenciesInfo();

            // Stream Deck Plugin check
            UpdateStreamDeckButtonState();

            // Codec
            _visibleCodecOptions = new List<(string Value, string Label)>(CodecOptions);
            CodecCombo.Items.Clear();
            foreach (var (_, label) in _visibleCodecOptions)
                CodecCombo.Items.Add(label);
            CodecCombo.SelectionChanged -= CodecCombo_SelectionChanged;
            int codecIdx = _visibleCodecOptions.FindIndex(c => c.Value == _settings.RecordCodec);
            CodecCombo.SelectedIndex = codecIdx >= 0 ? codecIdx : 0;
            CodecCombo.SelectionChanged += CodecCombo_SelectionChanged;
            UpdateCodecInfo();

            NewRecordingsNonDestructiveCheck.Checked -= NewRecordingsNonDestructiveCheck_Checked;
            NewRecordingsNonDestructiveCheck.Unchecked -= NewRecordingsNonDestructiveCheck_Unchecked;
            NewRecordingsNonDestructiveCheck.IsChecked = _settings.NewRecordingsNonDestructive;
            NewRecordingsNonDestructiveCheck.Checked += NewRecordingsNonDestructiveCheck_Checked;
            NewRecordingsNonDestructiveCheck.Unchecked += NewRecordingsNonDestructiveCheck_Unchecked;

            // VST Plugin path
            RefreshVst2PluginList();
            Vst3PluginPathTextBox.Text = _settings.Vst3PluginPath;

            // Loudness Normalization
            AutoNormalizeCheck.IsChecked = _settings.AutoNormalizeOnCapture;
            double lufsVal = Math.Clamp(_settings.TargetLoudnessLufs, -24.0, -6.0);
            TargetLufsSlider.Value = lufsVal;
            if (TargetLufsValueText != null)
                TargetLufsValueText.Text = $"{lufsVal:0.0} LUFS";

            // Buffer duration
            double bufSec = Math.Clamp(_settings.PastBufferDurationMs / 1000.0, 0.5, 60.0);
            BufferDurationSlider.Value = bufSec;
            BufferDurationLabel.Text = $"{bufSec:0.#}s";

            // Hotkey modifiers
            ModCtrl.IsChecked = (_settings.BufferHotKeyModifiers & MOD_CONTROL) != 0;
            ModAlt.IsChecked = (_settings.BufferHotKeyModifiers & MOD_ALT) != 0;
            ModShift.IsChecked = (_settings.BufferHotKeyModifiers & MOD_SHIFT) != 0;

            // Hotkey key
            _capturedVk = _settings.BufferHotKeyVk;
            HotkeyKeyBox.Text = KeyHelper.VkToLabel(_capturedVk);

            // Max records
            MaxRecordsSlider.Value = _settings.MaxRecords;
            MaxRecordsLabel.Text = _settings.MaxRecords == 0 ? "∞" : _settings.MaxRecords.ToString();

            // Font variant
            FontVariantCombo.SelectionChanged -= FontVariantCombo_SelectionChanged;
            FontVariantCombo.Items.Clear();
            int fontIdx = 0;
            for (int i = 0; i < App.FontVariants.Count; i++)
            {
                var v = App.FontVariants[i];
                FontVariantCombo.Items.Add(v.DisplayName);
                if (v.Key == _settings.AppFontVariant) fontIdx = i;
            }
            FontVariantCombo.SelectedIndex = fontIdx;
            FontVariantCombo.SelectionChanged += FontVariantCombo_SelectionChanged;

            // New pad naming
            DefaultPadTitleBox.Text = string.IsNullOrWhiteSpace(_settings.DefaultPadTitleTemplate)
                ? "Recording {timestamp}"
                : _settings.DefaultPadTitleTemplate;
            UseFocusedAppNameCheck.IsChecked = _settings.UseFocusedAppForPadTitle;

            // Audio Engine & Trim editor output & Live Mic
            PopulateAudioEngines();
            PopulateTrimOutputDevices();
            PopulateLiveMicDevices();
            RefreshVirtualDriverStatus();

            // Appearance: language + theme + meter skin
            SelectedLanguage = _settings.Language;
            LanguageCombo.SelectionChanged -= LanguageCombo_SelectionChanged;
            if (_settings.Language == "fr")
                LanguageCombo.SelectedIndex = 1;
            else
                LanguageCombo.SelectedIndex = 0;
            LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;

            ThemeCombo.SelectionChanged -= ThemeCombo_SelectionChanged;
            ThemeCombo.Items.Clear();
            int themeIdx = 0;
            for (int i = 0; i < ThemeManager.Themes.Count; i++)
            {
                ThemeCombo.Items.Add(ThemeManager.Themes[i].Label);
                if (ThemeManager.Themes[i].Key == _settings.Theme) themeIdx = i;
            }
            ThemeCombo.SelectedIndex = themeIdx;
            ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;

            MeterSkinCombo.SelectionChanged -= MeterSkinCombo_SelectionChanged;
            MeterSkinCombo.Items.Clear();
            int skinIdx = 0;
            for (int i = 0; i < ThemeManager.MeterSkins.Count; i++)
            {
                MeterSkinCombo.Items.Add(ThemeManager.MeterSkins[i].Label);
                if (ThemeManager.MeterSkins[i].Key == _settings.MeterSkin) skinIdx = i;
            }
            MeterSkinCombo.SelectedIndex = skinIdx;
            MeterSkinCombo.SelectionChanged += MeterSkinCombo_SelectionChanged;

            MeterDigitalDotsCheck.IsChecked = _settings.MeterDigitalDots;

            // UI Zoom & Scaling
            _suppressZoomSliderEvents = true;
            double zoomPct = Math.Clamp(Math.Round(ZoomManager.CurrentScale * 100.0), 50.0, 200.0);
            UiZoomSlider.Value = zoomPct;
            UiZoomValueLabel.Text = $"{(int)zoomPct}%";
            _suppressZoomSliderEvents = false;

            PerformanceModeCheck.IsChecked = _settings.PerformanceMode;
            PauseAnimationsWhenUnfocusedCheck.IsChecked = _settings.PauseAnimationsWhenUnfocused;
            PreloadAudioCacheCheck.IsChecked = _settings.PreloadAudioCache;

            // System tray / startup
            MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
            CloseToTrayCheck.IsChecked = _settings.CloseToTray;
            StartMinimizedCheck.IsChecked = _settings.StartMinimizedInTray;
            bool registeredStartup = Helpers.StartupRegistration.IsRunOnStartupEnabled();
            RunOnStartupCheck.IsChecked = registeredStartup;
            _settings.RunOnWindowsStartup = registeredStartup;

            // Detection algorithm is chosen from the main window's Mode combo;
            // preserve the current value so committing settings won't change it.
            SelectedDetectionAlgorithm = _settings.DetectionAlgorithm;

            // Speech-to-text
            AutoRenameSpeechCheck.IsChecked = _settings.AutoRenameWithSpeech;
            CancelRecordingIfNoVoiceCheck.IsChecked = _settings.CancelRecordingIfNoVoice;
            SpeechModelCombo.Items.Clear();
            string[] models = { "tiny", "base", "small", "medium", "large" };
            int modelIdx = 0;
            for (int i = 0; i < models.Length; i++)
            {
                SpeechModelCombo.Items.Add(models[i]);
                if (models[i] == _settings.SpeechModel) modelIdx = i;
            }
            SpeechModelCombo.SelectedIndex = modelIdx;
            SpeechLanguageBox.Text = string.IsNullOrWhiteSpace(_settings.SpeechLanguage) ? "en" : _settings.SpeechLanguage;
            UpdateDownloadButtonState();

            // CUDA GPU acceleration
            UpdateCudaStatusAndButtons();

            // Discord Integration
            DiscordRichPresenceCheck.IsChecked = _settings.DiscordRichPresenceEnabled;
            DiscordClientIdBox.Text = _settings.DiscordClientId.ToString();

            // Auto-update
            AutoInstallUpdatesCheck.IsChecked = _settings.AutoInstallUpdates;
            DownloadBetaUpdatesCheck.IsChecked = _settings.DownloadBetaUpdates;

            // Global Effects & Playback
            GlobalFadeCheck.IsChecked = _settings.GlobalFadeEnabled;
            double fadeInMs = Math.Clamp(_settings.GlobalFadeInDurationMs, 0.0, 5000.0);
            double fadeOutMs = Math.Clamp(_settings.GlobalFadeOutDurationMs, 0.0, 5000.0);
            GlobalFadeInSlider.Value = fadeInMs;
            GlobalFadeOutSlider.Value = fadeOutMs;
            GlobalFadeInValueText.Text = $"{fadeInMs:0} ms";
            GlobalFadeOutValueText.Text = $"{fadeOutMs:0} ms";
            AllowMultiPadPlaybackCheck.IsChecked = _settings.AllowMultiPadPlayback;
        }

        private void PopulateVersionAndDependenciesInfo()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                string fullVersion = !string.IsNullOrEmpty(infoVersion)
                    ? infoVersion
                    : (ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}.{ver.Revision}" : "1.8.4.0715");

                if (fullVersion.Contains('+'))
                {
                    var plusIdx = fullVersion.IndexOf('+');
                    fullVersion = fullVersion[..plusIdx];
                }

                string versionDisplay = fullVersion.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                    ? fullVersion
                    : $"v{fullVersion}";

                VersionInfoText.Text = $"PaDDY {versionDisplay}";

                var deps = new List<string>();

                var netVer = RuntimeInformation.FrameworkDescription;
                if (!string.IsNullOrEmpty(netVer))
                    deps.Add(netVer);

                try
                {
                    var naudioVer = typeof(NAudio.Wave.WaveStream).Assembly.GetName().Version;
                    if (naudioVer != null) deps.Add($"NAudio {naudioVer.Major}.{naudioVer.Minor}.{naudioVer.Build}");
                }
                catch { }

                try
                {
                    var vorticeVer = typeof(Vortice.Direct3D11.ID3D11Device).Assembly.GetName().Version;
                    if (vorticeVer != null) deps.Add($"Vortice {vorticeVer.Major}.{vorticeVer.Minor}.{vorticeVer.Build}");
                }
                catch { }

                try
                {
                    var whisperVer = typeof(Whisper.net.WhisperFactory).Assembly.GetName().Version;
                    if (whisperVer != null) deps.Add($"Whisper.net {whisperVer.Major}.{whisperVer.Minor}.{whisperVer.Build}");
                }
                catch { }

                DependenciesInfoText.Text = string.Join("\n", deps);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load version/dependency info: {ex}");
            }
        }

        private void LanguageCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            {
                SelectedLanguage = lang;
                _settings.Language = lang;
                LocalizationManager.Instance.SetCulture(lang);
                _settings.Save();
            }
        }

        private void ThemeCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            int i = ThemeCombo.SelectedIndex;
            if (i >= 0 && i < ThemeManager.Themes.Count)
                ThemeManager.ApplyTheme(ThemeManager.Themes[i].Key); // live preview
        }

        private void MeterSkinCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            int i = MeterSkinCombo.SelectedIndex;
            if (i >= 0 && i < ThemeManager.MeterSkins.Count)
                ThemeManager.ApplyMeterSkin(ThemeManager.MeterSkins[i].Key, _settings.MeterDigitalDots); // live preview
        }

        private void FontVariantCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            int i = FontVariantCombo.SelectedIndex;
            if (i >= 0 && i < App.FontVariants.Count)
                App.ApplyFont(App.FontVariants[i].Key); // live preview
        }

        private void MeterDigitalDotsCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (MeterDigitalDotsCheck.IsChecked.HasValue)
            {
                _settings.MeterDigitalDots = MeterDigitalDotsCheck.IsChecked.Value;
                int i = MeterSkinCombo.SelectedIndex;
                if (i >= 0 && i < ThemeManager.MeterSkins.Count)
                    ThemeManager.ApplyMeterSkin(ThemeManager.MeterSkins[i].Key, _settings.MeterDigitalDots);
            }
        }

        private void OnGlobalZoomScaleChanged(double newScale)
        {
            if (_suppressZoomSliderEvents) return;
            Dispatcher.InvokeAsync(() =>
            {
                _suppressZoomSliderEvents = true;
                double pct = Math.Clamp(Math.Round(newScale * 100.0), 50.0, 200.0);
                UiZoomSlider.Value = pct;
                UiZoomValueLabel.Text = $"{(int)pct}%";
                SelectedUiScale = newScale;
                _suppressZoomSliderEvents = false;
            });
        }

        private void UiZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressZoomSliderEvents) return;
            int pct = (int)Math.Round(e.NewValue);
            if (UiZoomValueLabel != null)
                UiZoomValueLabel.Text = $"{pct}%";

            double scale = Math.Round(pct / 100.0, 2);
            SelectedUiScale = scale;
            ZoomManager.SetScale(scale, saveSettings: false);
        }

        private void ResetUiZoomBtn_Click(object sender, RoutedEventArgs e)
        {
            _suppressZoomSliderEvents = true;
            UiZoomSlider.Value = 100;
            UiZoomValueLabel.Text = "100%";
            SelectedUiScale = 1.0;
            _suppressZoomSliderEvents = false;
            ZoomManager.ResetZoom(saveSettings: false);
        }

        private void ZoomPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && double.TryParse(fe.Tag?.ToString(), out double pct))
            {
                _suppressZoomSliderEvents = true;
                UiZoomSlider.Value = pct;
                UiZoomValueLabel.Text = $"{(int)pct}%";
                double scale = Math.Round(pct / 100.0, 2);
                SelectedUiScale = scale;
                _suppressZoomSliderEvents = false;
                ZoomManager.SetScale(scale, saveSettings: false);
            }
        }

        private void PopulateAudioEngines()
        {
            AudioEngineCombo.Items.Clear();
            AudioEngineCombo.Items.Add(LocalizationManager.Instance.GetString("AudioEngineNAudio"));
            AudioEngineCombo.Items.Add(LocalizationManager.Instance.GetString("AudioEngineBass"));
            AudioEngineCombo.SelectedIndex = _settings.AudioEngine == AudioEngineType.ManagedBass ? 1 : 0;
        }

        private void PopulateTrimOutputDevices()
        {
            TrimOutputDeviceCombo.Items.Clear();
            TrimOutputDeviceCombo.Items.Add("Default Output");

            using (var enumerator = new MMDeviceEnumerator())
            {
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                    TrimOutputDeviceCombo.Items.Add(device.FriendlyName);
            }

            int selected = Math.Clamp(_settings.TrimEditorOutputDeviceIndex, 0, TrimOutputDeviceCombo.Items.Count - 1);
            TrimOutputDeviceCombo.SelectedIndex = selected;
        }
        private void PopulateLiveMicDevices()
        {
            LiveMicDeviceCombo.Items.Clear();
            LiveMicDeviceCombo.Items.Add("Default Microphone");
            for (int i = 0; i < NAudio.Wave.WaveInEvent.DeviceCount; i++)
            {
                var caps = NAudio.Wave.WaveInEvent.GetCapabilities(i);
                LiveMicDeviceCombo.Items.Add(caps.ProductName);
            }

            int selectedLiveMic = Math.Clamp(_settings.LiveMicDeviceIndex + 1, 0, LiveMicDeviceCombo.Items.Count - 1);
            LiveMicDeviceCombo.SelectedIndex = selectedLiveMic;

            LiveMicOutputDeviceCombo.Items.Clear();
            LiveMicOutputDeviceCombo.Items.Add("Default Output");
            using (var enumerator = new MMDeviceEnumerator())
            {
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                    LiveMicOutputDeviceCombo.Items.Add(device.FriendlyName);
            }

            int selectedLiveMicOutput = Math.Clamp(_settings.LiveMicOutputDeviceIndex, 0, LiveMicOutputDeviceCombo.Items.Count - 1);
            LiveMicOutputDeviceCombo.SelectedIndex = selectedLiveMicOutput;

            LiveMicFxCheck.IsChecked = _settings.LiveMicFxEnabled;
            LiveMicGainSlider.Value = Math.Clamp(_settings.LiveMicGain, 0.0, 2.0);
            if (LiveMicGainValueText != null)
                LiveMicGainValueText.Text = $"{(_settings.LiveMicGain * 100):0}%";
        }

        private void LiveMicGainSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (LiveMicGainValueText != null)
                LiveMicGainValueText.Text = $"{e.NewValue * 100:0}%";
        }

        private void ClearAllDataBtn_Click(object sender, RoutedEventArgs e)
        {
            var res = System.Windows.MessageBox.Show(
                this,
                "⚠ Are you sure you want to CLEAR ALL DATA?\n\nThis will permanently delete ALL recordings, audio clips, custom folders, and reset all settings to defaults.\n\nThis action CANNOT be undone!",
                "Clear All Data — PaDDY",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (res == MessageBoxResult.Yes)
            {
                if (MainWindow.Instance != null)
                {
                    MainWindow.Instance.PerformClearAllData();
                    Confirmed = true;
                    Close();
                }
            }
        }

        private void CodecCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateCodecInfo();
        }

        private void UpdateCodecInfo()
        {
            if (CodecInfoText == null) return;
            int ci = CodecCombo.SelectedIndex;
            string codec = ci >= 0 && ci < _visibleCodecOptions.Count ? _visibleCodecOptions[ci].Value : "wav";
            CodecInfoText.Text = CodecDescriptions.TryGetValue(codec, out var info) ? info : string.Empty;
        }

        private void BufferDurationSlider_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (BufferDurationLabel == null) return;
            BufferDurationLabel.Text = $"{e.NewValue:0.#}s";
        }

        private void MaxRecordsSlider_Changed(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (MaxRecordsLabel == null) return;
            int val = (int)e.NewValue;
            MaxRecordsLabel.Text = val == 0 ? "∞" : val.ToString();
        }

        private void HotkeyKeyBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _capturingKey = true;
            HotkeyKeyBox.Text = "Press a key…";
            HotkeyKeyBox.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2F, 0x3A, 0x2F));
        }

        private void HotkeyKeyBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _capturingKey = false;
            HotkeyKeyBox.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2A, 0x2A, 0x2A));
            HotkeyKeyBox.Text = KeyHelper.VkToLabel(_capturedVk);
        }

        private void HotkeyKeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (!_capturingKey) return;
            e.Handled = true;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            // Ignore modifier-only presses
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return;

            _capturedVk = (uint)KeyInterop.VirtualKeyFromKey(key);
            HotkeyKeyBox.Text = KeyHelper.VkToLabel(_capturedVk);
            Keyboard.ClearFocus();
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            int ci = CodecCombo.SelectedIndex;
            SelectedCodec = ci >= 0 && ci < _visibleCodecOptions.Count ? _visibleCodecOptions[ci].Value : "wav";
            SelectedBufferDurationMs = (int)(BufferDurationSlider.Value * 1000);

            uint mods = 0;
            if (ModCtrl.IsChecked == true) mods |= MOD_CONTROL;
            if (ModAlt.IsChecked == true) mods |= MOD_ALT;
            if (ModShift.IsChecked == true) mods |= MOD_SHIFT;
            SelectedHotKeyModifiers = mods;
            SelectedHotKeyVk = _capturedVk;
            SelectedMaxRecords = (int)MaxRecordsSlider.Value;

            int fi = FontVariantCombo.SelectedIndex;
            SelectedFontVariant = (fi >= 0 && fi < App.FontVariants.Count)
                ? App.FontVariants[fi].Key
                : "condensed";

            SelectedDefaultPadTitleTemplate = string.IsNullOrWhiteSpace(DefaultPadTitleBox.Text)
                ? "Recording {timestamp}"
                : DefaultPadTitleBox.Text.Trim();
            SelectedUseFocusedAppForPadTitle = UseFocusedAppNameCheck.IsChecked == true;
            SelectedAudioEngine = AudioEngineCombo.SelectedIndex == 1 ? AudioEngineType.ManagedBass : AudioEngineType.NAudio;
            _settings.AudioEngine = SelectedAudioEngine;
            AudioOutputDeviceResolver.ActiveAudioEngine = SelectedAudioEngine;
            SelectedTrimEditorOutputDeviceIndex = TrimOutputDeviceCombo.SelectedIndex;
            SelectedNewRecordingsNonDestructive = NewRecordingsNonDestructiveCheck.IsChecked == true;

            int ti = ThemeCombo.SelectedIndex;
            SelectedTheme = (ti >= 0 && ti < ThemeManager.Themes.Count)
                ? ThemeManager.Themes[ti].Key : "dark";
            int si = MeterSkinCombo.SelectedIndex;
            SelectedMeterSkin = (si >= 0 && si < ThemeManager.MeterSkins.Count)
                ? ThemeManager.MeterSkins[si].Key : "default";
            SelectedUiScale = Math.Clamp(Math.Round(UiZoomSlider.Value / 100.0, 2), ZoomManager.MinScale, ZoomManager.MaxScale);
            _settings.UiScale = SelectedUiScale;
            ZoomManager.SetScale(SelectedUiScale, saveSettings: true);

            SelectedPerformanceMode = PerformanceModeCheck.IsChecked == true;
            SelectedPauseAnimationsWhenUnfocused = PauseAnimationsWhenUnfocusedCheck.IsChecked == true;
            SelectedPreloadAudioCache = PreloadAudioCacheCheck.IsChecked == true;
            _settings.PreloadAudioCache = SelectedPreloadAudioCache;

            SelectedMinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            SelectedCloseToTray = CloseToTrayCheck.IsChecked == true;
            SelectedStartMinimizedInTray = StartMinimizedCheck.IsChecked == true;
            SelectedRunOnWindowsStartup = RunOnStartupCheck.IsChecked == true;
            SelectedAutoRenameWithSpeech = AutoRenameSpeechCheck.IsChecked == true;
            SelectedCancelRecordingIfNoVoice = CancelRecordingIfNoVoiceCheck.IsChecked == true;
            SelectedSpeechModel = SpeechModelCombo.SelectedItem?.ToString() ?? "base";
            SelectedSpeechLanguage = string.IsNullOrWhiteSpace(SpeechLanguageBox.Text) ? "en" : SpeechLanguageBox.Text.Trim();
            SelectedUseCudaForSpeech = UseCudaCheck.IsChecked == true;

            // Save Discord settings
            SelectedDiscordRichPresenceEnabled = DiscordRichPresenceCheck.IsChecked == true;
            if (long.TryParse(DiscordClientIdBox.Text, out long cid))
                SelectedDiscordClientId = cid;
            else
                SelectedDiscordClientId = 461618159171141643;
            SelectedAutoInstallUpdates = AutoInstallUpdatesCheck.IsChecked == true;
            SelectedDownloadBetaUpdates = DownloadBetaUpdatesCheck.IsChecked == true;

            _settings.VstPluginPath = string.Empty;
            _settings.Vst3PluginPath = Vst3PluginPathTextBox.Text;
            _settings.AutoNormalizeOnCapture = AutoNormalizeCheck.IsChecked == true;
            _settings.TargetLoudnessLufs = Math.Round(TargetLufsSlider.Value, 1);
            SelectedLiveMicOutputDeviceIndex = LiveMicOutputDeviceCombo.SelectedIndex;
            _settings.LiveMicOutputDeviceIndex = LiveMicOutputDeviceCombo.SelectedIndex;
            _settings.LiveMicDeviceIndex = LiveMicDeviceCombo.SelectedIndex - 1;
            _settings.LiveMicFxEnabled = LiveMicFxCheck.IsChecked == true;
            _settings.LiveMicGain = LiveMicGainSlider.Value;

            // Global Effects & Playback
            SelectedGlobalFadeEnabled = GlobalFadeCheck.IsChecked == true;
            SelectedGlobalFadeInDurationMs = Math.Round(GlobalFadeInSlider.Value, 0);
            SelectedGlobalFadeOutDurationMs = Math.Round(GlobalFadeOutSlider.Value, 0);
            _settings.GlobalFadeEnabled = SelectedGlobalFadeEnabled;
            _settings.GlobalFadeInDurationMs = SelectedGlobalFadeInDurationMs;
            _settings.GlobalFadeOutDurationMs = SelectedGlobalFadeOutDurationMs;
            SelectedAllowMultiPadPlayback = AllowMultiPadPlaybackCheck.IsChecked == true;
            _settings.AllowMultiPadPlayback = SelectedAllowMultiPadPlayback;

            // Whisper's native runtime is loaded once per process, so switching
            // between CPU and CUDA only takes effect after a restart.
            if (SelectedUseCudaForSpeech != _settings.UseCudaForSpeech &&
                Services.SpeechRecognitionService.RequiresRestartForCudaChange(SelectedUseCudaForSpeech))
            {
                new PaDDY.Controls.InfoDialog(
                    "CUDA acceleration",
                    "The Whisper speech engine is already loaded, so the CUDA setting will take effect the next time PaDDY starts.")
                    .ShowDialog(this);
            }

            Confirmed = true;
            Close();
        }

        private void AutoNormalizeCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoNormalizeCheck.IsChecked.HasValue)
                _settings.AutoNormalizeOnCapture = AutoNormalizeCheck.IsChecked.Value;
        }

        private void TargetLufsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TargetLufsValueText != null)
                TargetLufsValueText.Text = $"{e.NewValue:0.0} LUFS";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GlobalFadeInSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GlobalFadeInValueText != null)
                GlobalFadeInValueText.Text = $"{e.NewValue:0} ms";
        }

        private void GlobalFadeOutSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (GlobalFadeOutValueText != null)
                GlobalFadeOutValueText.Text = $"{e.NewValue:0} ms";
        }

        private void UpdateStreamDeckButtonState()
        {
            string pluginPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.paddy.sdPlugin");
            if (System.IO.Directory.Exists(pluginPath))
            {
                InstallStreamDeckBtn.Content = LocalizationManager.Instance["UninstallStreamDeckBtn"];
                InstallStreamDeckBtn.IsEnabled = true;
                InstallStreamDeckBtn.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                InstallStreamDeckBtn.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
            }
            else
            {
                InstallStreamDeckBtn.Content = LocalizationManager.Instance["InstallStreamDeckBtn"];
                InstallStreamDeckBtn.IsEnabled = true;
                InstallStreamDeckBtn.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                InstallStreamDeckBtn.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
            }
        }

        private void InstallStreamDeckBtn_Click(object sender, RoutedEventArgs e)
        {
            string pluginPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.paddy.sdPlugin");
            if (System.IO.Directory.Exists(pluginPath))
            {
                UninstallStreamDeckPlugin();
            }
            else
            {
                InstallStreamDeckPlugin();
            }
        }

        private void InstallStreamDeckPlugin()
        {
            // Close Stream Deck if running before installing/updating the plugin
            string streamDeckPath = null;
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("StreamDeck"))
            {
                try
                {
                    streamDeckPath = process.MainModule?.FileName;
                }
                catch { }
            }

            if (!string.IsNullOrEmpty(streamDeckPath) && System.IO.File.Exists(streamDeckPath))
            {
                try
                {
                    string registryPath = null;
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\StreamDeck.exe"))
                    {
                        registryPath = key?.GetValue("")?.ToString();
                    }
                    if (string.IsNullOrEmpty(registryPath))
                    {
                        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\StreamDeck.exe"))
                        {
                            registryPath = key?.GetValue("")?.ToString();
                        }
                    }
                    if (string.IsNullOrEmpty(registryPath))
                    {
                        registryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck", "StreamDeck.exe");
                    }

                    if (!string.IsNullOrEmpty(registryPath) && System.IO.File.Exists(registryPath))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(registryPath) { UseShellExecute = true });
                    }
                }
                catch { }
            }

            // Give Stream Deck time to fully exit
            System.Threading.Thread.Sleep(2000);

            try
            {
                string pluginPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "com.paddy.streamDeckPlugin");

                // Extract from embedded resources
                using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("com.paddy.streamDeckPlugin"))
                {
                    if (stream != null)
                    {
                        using (var fileStream = System.IO.File.Create(pluginPath))
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Stream Deck Plugin not found in application resources.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pluginPath) { UseShellExecute = true });

                // Poll for installation success
                string installPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.paddy.sdPlugin");
                System.Threading.Tasks.Task.Run(async () =>
                {
                    for (int i = 0; i < 30; i++)
                    {
                        await System.Threading.Tasks.Task.Delay(1000);
                        if (System.IO.Directory.Exists(installPath))
                        {
                            await Dispatcher.InvokeAsync(() =>
                            {
                                UpdateStreamDeckButtonState();
                            });
                            break;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to install plugin:\n{ex.Message}", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UninstallStreamDeckPlugin()
        {
            string installPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.paddy.sdPlugin");
            try
            {
                if (System.IO.Directory.Exists(installPath))
                {
                    System.IO.Directory.Delete(installPath, true);
                }

                System.Windows.MessageBox.Show("Stream Deck Plugin uninstalled successfully.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Information);
                UpdateStreamDeckButtonState();
            }
            catch (System.IO.IOException)
            {
                var result = System.Windows.MessageBox.Show(
                    "Failed to uninstall plugin because some files are in use.\n" +
                    "Please close the Elgato Stream Deck software and try again.\n\n" +
                    "Would you like PaDDY to attempt to close the Stream Deck software and retry?",
                    "Stream Deck Plugin In Use",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        string streamDeckPath = null;
                        foreach (var process in System.Diagnostics.Process.GetProcessesByName("StreamDeck"))
                        {
                            try
                            {
                                if (string.IsNullOrEmpty(streamDeckPath))
                                {
                                    streamDeckPath = process.MainModule?.FileName;
                                }
                            }
                            catch { }

                            try
                            {
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                            catch { }
                        }

                        System.Threading.Thread.Sleep(1000);

                        if (System.IO.Directory.Exists(installPath))
                        {
                            System.IO.Directory.Delete(installPath, true);
                        }

                        if (!string.IsNullOrEmpty(streamDeckPath) && System.IO.File.Exists(streamDeckPath))
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(streamDeckPath) { UseShellExecute = true });
                            }
                            catch { }
                        }
                        else
                        {
                            try
                            {
                                string registryPath = null;
                                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\StreamDeck.exe"))
                                {
                                    registryPath = key?.GetValue("")?.ToString();
                                }
                                if (string.IsNullOrEmpty(registryPath))
                                {
                                    using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\StreamDeck.exe"))
                                    {
                                        registryPath = key?.GetValue("")?.ToString();
                                    }
                                }
                                if (string.IsNullOrEmpty(registryPath))
                                {
                                    registryPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Elgato", "StreamDeck", "StreamDeck.exe");
                                }

                                if (System.IO.File.Exists(registryPath))
                                {
                                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(registryPath) { UseShellExecute = true });
                                }
                            }
                            catch { }
                        }

                        System.Windows.MessageBox.Show("Stream Deck Plugin uninstalled successfully.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Information);
                        UpdateStreamDeckButtonState();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Failed to uninstall plugin after closing Stream Deck:\n{ex.Message}", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to uninstall plugin:\n{ex.Message}", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SpeechModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDownloadButtonState();
        }

        private void UpdateDownloadButtonState()
        {
            if (SpeechModelCombo.SelectedItem is string model)
            {
                if (PaDDY.Services.SpeechRecognitionService.ActiveDownloadingModel == model)
                {
                    DownloadModelBtn.IsEnabled = false;
                    SpeechModelCombo.IsEnabled = false;
                    ModelDownloadProgress.Visibility = Visibility.Visible;
                    ModelDownloadStatusText.Visibility = Visibility.Visible;
                    if (PaDDY.Services.SpeechRecognitionService.ActiveDownloadPercent < 0)
                    {
                        ModelDownloadProgress.IsIndeterminate = true;
                    }
                    else
                    {
                        ModelDownloadProgress.IsIndeterminate = false;
                        ModelDownloadProgress.Value = PaDDY.Services.SpeechRecognitionService.ActiveDownloadPercent * 100;
                    }
                    ModelDownloadStatusText.Text = PaDDY.Services.SpeechRecognitionService.ActiveDownloadStatusText;
                    return;
                }

                DownloadModelBtn.IsEnabled = true;
                SpeechModelCombo.IsEnabled = true;
                bool downloaded = PaDDY.Services.SpeechRecognitionService.IsModelDownloaded(model);
                if (downloaded)
                {
                    string sizeInfo = PaDDY.Services.SpeechRecognitionService.GetModelSizeString(model);
                    if (!string.IsNullOrEmpty(sizeInfo))
                        UninstallModelBtn.Content = $"Remove ({sizeInfo})";
                    else
                        UninstallModelBtn.Content = "Remove";

                    DownloadModelBtn.Visibility = Visibility.Collapsed;
                    UninstallModelBtn.Visibility = Visibility.Visible;
                }
                else
                {
                    string expectedSize = PaDDY.Services.SpeechRecognitionService.GetExpectedModelSizeString(model);
                    DownloadModelBtn.Content = $"Download ({expectedSize})";
                    DownloadModelBtn.Visibility = Visibility.Visible;
                    UninstallModelBtn.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OnSpeechModelDownloadProgressUpdated(string model, double percent, string statusText)
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (SpeechModelCombo.SelectedItem is string selected && selected == model)
                {
                    if (percent >= 0)
                    {
                        ModelDownloadProgress.IsIndeterminate = false;
                        ModelDownloadProgress.Value = percent * 100;
                        ModelDownloadProgress.Visibility = Visibility.Visible;
                        ModelDownloadStatusText.Visibility = Visibility.Visible;
                        ModelDownloadStatusText.Text = statusText;
                        DownloadModelBtn.IsEnabled = false;
                        SpeechModelCombo.IsEnabled = false;
                    }
                    else if (percent == -1 && !string.IsNullOrEmpty(statusText))
                    {
                        ModelDownloadProgress.IsIndeterminate = true;
                        ModelDownloadProgress.Visibility = Visibility.Visible;
                        ModelDownloadStatusText.Visibility = Visibility.Visible;
                        ModelDownloadStatusText.Text = statusText;
                        DownloadModelBtn.IsEnabled = false;
                        SpeechModelCombo.IsEnabled = false;
                    }
                    else
                    {
                        ModelDownloadProgress.Visibility = Visibility.Collapsed;
                        ModelDownloadStatusText.Visibility = Visibility.Collapsed;
                        SpeechModelCombo.IsEnabled = true;
                        DownloadModelBtn.IsEnabled = true;
                        UpdateDownloadButtonState();
                    }
                }
            });
        }

        private void UninstallModelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SpeechModelCombo.SelectedItem is not string model) return;

            var res = System.Windows.MessageBox.Show(
                $"Are you sure you want to uninstall the {model} model?",
                "PaDDY",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                bool deleted = PaDDY.Services.SpeechRecognitionService.DeleteModel(model);
                if (deleted)
                {
                    UpdateDownloadButtonState();
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not delete the model. It might be bundled with the application or currently in use.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private async void DownloadModelBtn_Click(object sender, RoutedEventArgs e)
        {
            if (SpeechModelCombo.SelectedItem is not string model) return;
            if (PaDDY.Services.SpeechRecognitionService.ActiveDownloadingModel != null) return;

            DownloadModelBtn.IsEnabled = false;
            SpeechModelCombo.IsEnabled = false;
            ModelDownloadProgress.Visibility = Visibility.Visible;
            ModelDownloadStatusText.Visibility = Visibility.Visible;
            ModelDownloadProgress.IsIndeterminate = true;
            ModelDownloadStatusText.Text = $"Downloading {model} model...";

            try
            {
                var progress = new Progress<(double Percent, string StatusText)>(p =>
                {
                    if (p.Percent < 0)
                    {
                        ModelDownloadProgress.IsIndeterminate = true;
                    }
                    else
                    {
                        ModelDownloadProgress.IsIndeterminate = false;
                        ModelDownloadProgress.Value = p.Percent * 100;
                    }
                    ModelDownloadStatusText.Text = p.StatusText;
                });

                await PaDDY.Services.SpeechRecognitionService.DownloadModelAsync(model, progress);

                ModelDownloadStatusText.Text = "Download complete!";
                await System.Threading.Tasks.Task.Delay(2000);
            }
            catch (Exception ex)
            {
                ModelDownloadStatusText.Text = $"Download failed: {ex.Message}";
                await System.Threading.Tasks.Task.Delay(3000);
            }
            finally
            {
                ModelDownloadProgress.Visibility = Visibility.Collapsed;
                ModelDownloadStatusText.Visibility = Visibility.Collapsed;
                SpeechModelCombo.IsEnabled = true;
                UpdateDownloadButtonState();
            }
        }

        private void UpdateCudaStatusAndButtons()
        {
            if (PaDDY.Services.CudaManager.IsDownloading)
            {
                UseCudaCheck.IsEnabled = false;
                DownloadCudaBtn.IsEnabled = false;
                RemoveCudaBtn.IsEnabled = false;
                CudaDownloadProgress.Visibility = Visibility.Visible;
                CudaDownloadStatusText.Visibility = Visibility.Visible;
                if (PaDDY.Services.CudaManager.ActiveDownloadPercent < 0)
                {
                    CudaDownloadProgress.IsIndeterminate = true;
                }
                else
                {
                    CudaDownloadProgress.IsIndeterminate = false;
                    CudaDownloadProgress.Value = PaDDY.Services.CudaManager.ActiveDownloadPercent * 100;
                }
                CudaDownloadStatusText.Text = PaDDY.Services.CudaManager.ActiveStatusText;
                return;
            }

            CudaDownloadProgress.Visibility = Visibility.Collapsed;
            CudaDownloadStatusText.Visibility = Visibility.Collapsed;
            DownloadCudaBtn.IsEnabled = true;
            RemoveCudaBtn.IsEnabled = true;

            bool nvidiaDetected = Helpers.GpuHelper.IsNvidiaGpuAvailable;
            bool cudaInstalled = PaDDY.Services.CudaManager.IsCudaPackInstalled();
            bool cudaRuntimeOk = Helpers.GpuHelper.IsCudaRuntimeAvailable;

            if (!nvidiaDetected)
            {
                UseCudaCheck.IsEnabled = false;
                UseCudaCheck.IsChecked = false;
                CudaStatusText.Text = LocalizationManager.Instance["CudaNotAvailable"];
                CudaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x90));
                DownloadCudaBtn.Visibility = Visibility.Collapsed;
                RemoveCudaBtn.Visibility = Visibility.Collapsed;
            }
            else if (cudaInstalled && cudaRuntimeOk)
            {
                UseCudaCheck.IsEnabled = true;
                UseCudaCheck.IsChecked = _settings.UseCudaForSpeech;
                CudaStatusText.Text = LocalizationManager.Instance["CudaPackInstalled"];
                CudaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x60, 0x90, 0x60));
                DownloadCudaBtn.Visibility = Visibility.Collapsed;
                RemoveCudaBtn.Visibility = Visibility.Visible;
            }
            else
            {
                UseCudaCheck.IsEnabled = true;
                UseCudaCheck.IsChecked = false;
                CudaStatusText.Text = LocalizationManager.Instance["CudaPackNotInstalled"];
                CudaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xB0, 0x90, 0x50));
                DownloadCudaBtn.Visibility = Visibility.Visible;
                RemoveCudaBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void OnCudaDownloadProgressUpdated(double percent, string statusText)
        {
            Dispatcher.Invoke(() =>
            {
                if (percent >= 0)
                {
                    CudaDownloadProgress.IsIndeterminate = false;
                    CudaDownloadProgress.Value = percent * 100;
                    CudaDownloadProgress.Visibility = Visibility.Visible;
                    CudaDownloadStatusText.Visibility = Visibility.Visible;
                    CudaDownloadStatusText.Text = statusText;
                    DownloadCudaBtn.IsEnabled = false;
                    RemoveCudaBtn.IsEnabled = false;
                    UseCudaCheck.IsEnabled = false;
                }
                else if (percent == -1 && !string.IsNullOrEmpty(statusText))
                {
                    CudaDownloadProgress.IsIndeterminate = true;
                    CudaDownloadProgress.Visibility = Visibility.Visible;
                    CudaDownloadStatusText.Visibility = Visibility.Visible;
                    CudaDownloadStatusText.Text = statusText;
                    DownloadCudaBtn.IsEnabled = false;
                    RemoveCudaBtn.IsEnabled = false;
                    UseCudaCheck.IsEnabled = false;
                }
                else
                {
                    UpdateCudaStatusAndButtons();
                }
            });
        }

        private async void UseCudaCheck_Click(object sender, RoutedEventArgs e)
        {
            if (UseCudaCheck.IsChecked == true && !PaDDY.Services.CudaManager.IsCudaPackInstalled())
            {
                var res = System.Windows.MessageBox.Show(
                    LocalizationManager.Instance["CudaPackNotInstalled"] + "\n\nWould you like to download it now?",
                    "PaDDY",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (res == MessageBoxResult.Yes)
                {
                    UseCudaCheck.IsChecked = false;
                    await StartCudaDownloadAsync();
                }
                else
                {
                    UseCudaCheck.IsChecked = false;
                }
            }
        }

        private async void DownloadCudaBtn_Click(object sender, RoutedEventArgs e)
        {
            await StartCudaDownloadAsync();
        }

        private async Task StartCudaDownloadAsync()
        {
            if (PaDDY.Services.CudaManager.IsDownloading) return;

            DownloadCudaBtn.IsEnabled = false;
            RemoveCudaBtn.IsEnabled = false;
            UseCudaCheck.IsEnabled = false;
            CudaDownloadProgress.Visibility = Visibility.Visible;
            CudaDownloadStatusText.Visibility = Visibility.Visible;
            CudaDownloadProgress.IsIndeterminate = true;
            CudaDownloadStatusText.Text = "Initializing CUDA download...";

            try
            {
                var progress = new Progress<(double Percent, string StatusText)>(p =>
                {
                    if (p.Percent < 0)
                    {
                        CudaDownloadProgress.IsIndeterminate = true;
                    }
                    else
                    {
                        CudaDownloadProgress.IsIndeterminate = false;
                        CudaDownloadProgress.Value = p.Percent * 100;
                    }
                    CudaDownloadStatusText.Text = p.StatusText;
                });

                await PaDDY.Services.CudaManager.DownloadCudaPackAsync(progress);
                Helpers.GpuHelper.InvalidateCudaCache();
                UseCudaCheck.IsChecked = true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"CUDA download failed: {ex.Message}", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                UpdateCudaStatusAndButtons();
            }
        }

        private void RemoveCudaBtn_Click(object sender, RoutedEventArgs e)
        {
            var res = System.Windows.MessageBox.Show(
                LocalizationManager.Instance["ConfirmDeleteCudaPrompt"],
                LocalizationManager.Instance["ConfirmDeleteCudaTitle"],
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                bool deleted = PaDDY.Services.CudaManager.DeleteCudaPack();
                Helpers.GpuHelper.InvalidateCudaCache();
                if (deleted)
                {
                    UseCudaCheck.IsChecked = false;
                    UpdateCudaStatusAndButtons();
                }
                else
                {
                    System.Windows.MessageBox.Show("Could not completely delete the CUDA pack. Some files may be currently in use. Please restart PaDDY and try again.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // If the dialog was not confirmed, revert any live theme/meter/font preview.
            if (!Confirmed)
            {
                ThemeManager.ApplyTheme(_settings.Theme);
                ThemeManager.ApplyMeterSkin(_settings.MeterSkin, _settings.MeterDigitalDots);
                App.ApplyFont(_settings.AppFontVariant);
            }
            base.OnClosing(e);
        }

        private void DiscordRichPresenceCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            // Unregister or disconnect immediately if desired, but applied on save
        }

        private void AddVst2Button_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "VST2 Plugins (*.dll)|*.dll|All Files (*.*)|*.*",
                Title = "Import VST2 Plugins",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) != true)
                return;

            try
            {
                Directory.CreateDirectory(AppDataPaths.ManagedVst2Folder);
                int addedCount = 0;

                foreach (string sourcePath in dlg.FileNames)
                {
                    string destinationPath = Path.Combine(
                        AppDataPaths.ManagedVst2Folder,
                        Path.GetFileName(sourcePath));
                    File.Copy(sourcePath, destinationPath, overwrite: true);

                    if (!_settings.UserVstPluginPaths.Contains(destinationPath, StringComparer.OrdinalIgnoreCase))
                    {
                        _settings.UserVstPluginPaths.Add(destinationPath);
                        addedCount++;
                    }
                }

                if (addedCount > 0)
                {
                    RefreshVst2PluginList();
                    Vst2PluginsListBox.SelectedIndex = _vst2PluginItems.Count - 1;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Could not import VST2 plugin:\n{ex.Message}",
                    "PaDDY",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void DeleteVst2Button_Click(object sender, RoutedEventArgs e)
        {
            if (Vst2PluginsListBox.SelectedItem is not VstPluginListItem item)
                return;

            var confirmDlg = new PaDDY.Controls.ConfirmDialog(
                "Delete VST2 Plugin",
                $"Remove '{item.Name}' from the managed VST plugins?");
            if (confirmDlg.ShowDialog(this) != true)
                return;

            bool isManaged = item.FullPath.StartsWith(AppDataPaths.ManagedVst2Folder, StringComparison.OrdinalIgnoreCase);
            bool deleted = isManaged && TryDeleteVstItem(item.FullPath);

            if (isManaged && !deleted)
            {
                var restartDlg = new PaDDY.Controls.RestartRequiredDialog(
                    "Plugin in use",
                    $"'{item.Name}' is currently loaded by PaDDY and cannot be deleted right now.");
                if (restartDlg.ShowDialog(this) != true)
                    return;

                _settings.PendingDeletedVstPluginPaths ??= new List<string>();
                if (!_settings.PendingDeletedVstPluginPaths.Contains(item.FullPath, StringComparer.OrdinalIgnoreCase))
                    _settings.PendingDeletedVstPluginPaths.Add(item.FullPath);

                // Persist immediately so the pending deletion survives a restart or crash.
                _settings.Save();

                if (restartDlg.Action == PaDDY.Controls.RestartRequiredDialog.RestartAction.RestartNow)
                    RestartApplication();
            }

            int idx = _settings.UserVstPluginPaths.FindIndex(
                p => p.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _settings.UserVstPluginPaths.RemoveAt(idx);

            RefreshVst2PluginList();
        }

        /// <summary>
        /// Attempts to delete a VST file or bundle directory.
        /// Returns true on success, false if the item is locked or inaccessible.
        /// </summary>
        private static bool TryDeleteVstItem(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    // If we can't open the file with Delete sharing, another process (or PaDDY itself) has it loaded.
                    if (IsFileLocked(path))
                        return false;

                    File.Delete(path);
                    return true;
                }

                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                    return true;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsFileLocked(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Delete);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void RestartApplication()
        {
            try
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    exePath = AppContext.BaseDirectory;
                    string possibleExe = System.IO.Path.Combine(exePath, "PaDDY.exe");
                    if (File.Exists(possibleExe))
                        exePath = possibleExe;
                }

                Process.Start(new ProcessStartInfo(exePath)
                {
                    UseShellExecute = true
                });

                System.Windows.Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                var errDlg = new PaDDY.Controls.InfoDialog(
                    "Restart PaDDY",
                    $"Could not restart PaDDY:\n{ex.Message}");
                errDlg.ShowDialog();
            }
        }

        private void RefreshVst2PluginList()
        {
            _vst2PluginItems.Clear();
            long totalBytes = 0;

            foreach (string path in _settings.UserVstPluginPaths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase) ?? Enumerable.Empty<string>())
            {
                long size = GetVstItemSize(path);
                totalBytes += size;
                _vst2PluginItems.Add(new VstPluginListItem
                {
                    FullPath = path,
                    SizeText = FormatBytes(size)
                });
            }

            Vst2TotalSizeText.Text = totalBytes > 0
                ? $"Total: {FormatBytes(totalBytes)}"
                : "Total: 0 B";
            DeleteVst2Button.IsEnabled = Vst2PluginsListBox.SelectedItem != null;
        }

        private static long GetVstItemSize(string path)
        {
            try
            {
                if (File.Exists(path))
                    return new FileInfo(path).Length;

                if (Directory.Exists(path))
                {
                    long size = 0;
                    foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { size += new FileInfo(file).Length; }
                        catch { /* ignore locked files */ }
                    }
                    return size;
                }
            }
            catch { /* ignore inaccessible paths */ }

            return 0;
        }

        private static string FormatBytes(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            if (bytes >= GB) return $"{bytes / (double)GB:0.00} GB";
            if (bytes >= MB) return $"{bytes / (double)MB:0.0} MB";
            if (bytes >= KB) return $"{bytes / (double)KB:0.0} KB";
            return bytes > 0 ? $"{bytes} B" : "--";
        }

        private void BrowseVst3Button_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "VST3 Plugins (*.vst3)|*.vst3|All Files (*.*)|*.*",
                Title = "Select VST3 Plugin"
            };

            if (dlg.ShowDialog(this) == true)
            {
                Vst3PluginPathTextBox.Text = dlg.FileName;
            }
        }

        private void NewRecordingsNonDestructiveCheck_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void NewRecordingsNonDestructiveCheck_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isChangingNonDestructiveGlobal) return;

            var res = System.Windows.MessageBox.Show(
                "Disabling this setting means all existing non-destructive recordings will lose their real-time trim, gain, and effects. Are you sure you want to proceed?",
                "PaDDY",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
            {
                _isChangingNonDestructiveGlobal = true;
                NewRecordingsNonDestructiveCheck.IsChecked = true;
                _isChangingNonDestructiveGlobal = false;
            }
        }

        public void ShowLoadingOverlay(string message = "Processing...")
        {
            UpdateLoadingOverlayTheme();
            SettingsLoadingOverlay.Show(message);
        }

        public void HideLoadingOverlay(bool instantly = false)
        {
            SettingsLoadingOverlay.Hide(instantly);
        }

        private void UpdateLoadingOverlayTheme()
        {
            try
            {
                var themeKey = _settings?.Theme ?? "dark";
                var palette = ThemeManager.GetPalette(themeKey);
                if (palette != null)
                {
                    var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["AccentGreenBrush"]);
                    var secondary = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["SubtleTextBrush"]);
                    var text = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["PrimaryTextBrush"]);
                    var bg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(palette["WindowBgBrush"]);

                    SettingsLoadingOverlay.ApplyThemeColors(accent, secondary, text);
                    SettingsLoadingOverlay.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, bg.R, bg.G, bg.B));
                }
            }
            catch
            {
                // Fallback gracefully on any conversion/loading error
            }
        }

        private async void ExportData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PaDDY Backup (*.PADBACK)|*.PADBACK",
                FileName = $"PaDDY_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.PADBACK"
            };

            if (dlg.ShowDialog() == true)
            {
                var mainWindow = Owner as MainWindow;
                ShowLoadingOverlay("Creating backup...");
                if (mainWindow != null)
                {
                    mainWindow.ShowLoadingOverlay("Creating backup...");
                }
                await Task.Delay(50); // Let the overlay render

                bool success = false;
                var backupPath = dlg.FileName;
                try
                {
                    success = await Task.Run(() =>
                    {
                        var backupService = new BackupService();
                        return backupService.CreateBackup(backupPath);
                    });
                }
                finally
                {
                    HideLoadingOverlay();
                    if (mainWindow != null)
                    {
                        mainWindow.HideLoadingOverlay();
                    }
                }

                if (success)
                {
                    System.Windows.MessageBox.Show(this, "Backup created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(this, "Failed to create backup. Please ensure your data files are intact.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PaDDY Backup (*.PADBACK)|*.PADBACK"
            };

            if (dlg.ShowDialog() == true)
            {
                var mainWindow = Owner as MainWindow;
                ShowLoadingOverlay("Restoring backup...");
                if (mainWindow != null)
                {
                    mainWindow.ShowLoadingOverlay("Restoring backup...");
                }
                await Task.Delay(50); // Let the overlay render

                try
                {
                    if (mainWindow != null)
                    {
                        mainWindow.PrepareRecordingDataRestore();
                    }

                    var backupPath = dlg.FileName;
                    BackupService backupService = null!;
                    bool restoreSuccess = await Task.Run(() =>
                    {
                        backupService = new BackupService();
                        return backupService.RestoreBackup(backupPath);
                    });

                    if (restoreSuccess)
                    {
                        if (mainWindow != null)
                        {
                            await mainWindow.ReloadRecordingDataFromDiskAsync();
                            System.Windows.MessageBox.Show(this, "Backup restored successfully and recordings have been reloaded.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            System.Windows.MessageBox.Show(this, "Backup restored successfully. Please restart the application to apply changes.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        string detail = !string.IsNullOrEmpty(backupService?.LastError) ? $"\n\nDetails: {backupService.LastError}" : "";
                        System.Windows.MessageBox.Show(this, $"Failed to restore backup. Please ensure the file is a valid PaDDY backup.{detail}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                finally
                {
                    HideLoadingOverlay();
                    if (mainWindow != null)
                    {
                        mainWindow.HideLoadingOverlay();
                    }
                }
            }
        }

        #region Virtual Audio Driver Integration

        private void RefreshVirtualDriverStatus()
        {
            try
            {
                bool isSpeakerInstalled = VirtualAudioDriverService.IsSpeakerInstalled();
                bool isMicInstalled = VirtualAudioDriverService.IsMicInstalled();
                bool isFullyReady = isSpeakerInstalled && isMicInstalled;
                bool isPartiallyReady = isSpeakerInstalled || isMicInstalled;

                if (isFullyReady)
                {
                    VirtualDriverStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0x4C, 0xAF, 0x50));
                    VirtualDriverStatusBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("AccentGreenBrush");
                    VirtualDriverStatusBadgeText.Text = "Installed & Ready";
                    VirtualDriverInstallBtn.Content = "Reinstall Driver";
                    VirtualDriverUninstallBtn.IsEnabled = true;
                    RouteSoundboardPresetBtn.IsEnabled = true;
                    RouteLiveMicPresetBtn.IsEnabled = true;
                }
                else if (isPartiallyReady)
                {
                    VirtualDriverStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0xFF, 0xC1, 0x07));
                    VirtualDriverStatusBadgeText.Foreground = (System.Windows.Media.Brush)FindResource("AccentAmberBrush");
                    VirtualDriverStatusBadgeText.Text = "Partially Installed";
                    VirtualDriverInstallBtn.Content = "Repair / Reinstall";
                    VirtualDriverUninstallBtn.IsEnabled = true;
                    RouteSoundboardPresetBtn.IsEnabled = true;
                    RouteLiveMicPresetBtn.IsEnabled = true;
                }
                else
                {
                    VirtualDriverStatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0xFF, 0x70, 0x70));
                    VirtualDriverStatusBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x70, 0x70));
                    VirtualDriverStatusBadgeText.Text = "Not Installed";
                    VirtualDriverInstallBtn.Content = "Install Driver";
                    VirtualDriverUninstallBtn.IsEnabled = false;
                    RouteSoundboardPresetBtn.IsEnabled = false;
                    RouteLiveMicPresetBtn.IsEnabled = false;
                }
            }
            catch { }
        }

        private void OpenVbAudioNotice_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VbCableNoticeDialog { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                VirtualDriverInstallBtn_Click(sender, e);
            }
        }

        private async void VirtualDriverInstallBtn_Click(object sender, RoutedEventArgs e)
        {
            // If invoked directly from the Install button, show the confirmation & credit dialog first
            if (sender == VirtualDriverInstallBtn)
            {
                var dialog = new VbCableNoticeDialog { Owner = this };
                if (dialog.ShowDialog() != true)
                    return;
            }

            VirtualDriverInstallBtn.IsEnabled = false;
            VirtualDriverInstallBtn.Content = "Starting...";

            try
            {
                var progress = new Progress<string>(status =>
                {
                    VirtualDriverInstallBtn.Content = status;
                });

                var (success, msg) = await VirtualAudioDriverService.InstallDriverAsync(progress);

                // Allow audio endpoint manager a moment to register newly initialized endpoints
                await Task.Delay(1000);

                PopulateTrimOutputDevices();
                PopulateLiveMicDevices();
                RefreshVirtualDriverStatus();

                if (System.Windows.Application.Current?.MainWindow is MainWindow mainWin)
                {
                    _ = mainWin.RefreshAudioDevicesAsync();
                }

                if (success && VirtualAudioDriverService.IsFullyOperational())
                {
                    var result = System.Windows.MessageBox.Show(
                        this,
                        "VB-Audio Virtual Cable (WHQL Signed) has been installed successfully!\n\n" +
                        "Audio devices have been refreshed in PaDDY.\n\n" +
                        "New endpoints available:\n" +
                        "• Output: 'CABLE Input (VB-Audio Virtual Cable)' (Speaker)\n" +
                        "• Input: 'CABLE Output (VB-Audio Virtual Cable)' (Microphone)\n\n" +
                        "Would you like to restart PaDDY now to ensure full system audio driver synchronization?",
                        "Driver Installed — PaDDY",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        RestartApplication();
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        this,
                        msg,
                        "Driver Installation Status",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Failed to install driver: {ex.Message}",
                    "Installation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RefreshVirtualDriverStatus();
                VirtualDriverInstallBtn.IsEnabled = true;
            }
        }

        private async void VirtualDriverUninstallBtn_Click(object sender, RoutedEventArgs e)
        {
            var res = System.Windows.MessageBox.Show(
                this,
                "Are you sure you want to uninstall the VB-Audio Virtual Cable from Windows?\n\n" +
                "This will remove the CABLE Input and CABLE Output virtual audio devices.",
                "Uninstall Driver — PaDDY",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes)
                return;

            VirtualDriverUninstallBtn.IsEnabled = false;
            VirtualDriverUninstallBtn.Content = "Removing...";

            try
            {
                var (success, msg) = await VirtualAudioDriverService.UninstallDriverAsync();
                await Task.Delay(1000);

                PopulateTrimOutputDevices();
                PopulateLiveMicDevices();
                RefreshVirtualDriverStatus();

                if (System.Windows.Application.Current?.MainWindow is MainWindow mainWin)
                {
                    _ = mainWin.RefreshAudioDevicesAsync();
                }

                System.Windows.MessageBox.Show(
                    this,
                    $"Driver uninstallation result:\n{msg}",
                    "Driver Uninstalled — PaDDY",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    this,
                    $"Failed to uninstall driver: {ex.Message}",
                    "Uninstall Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RefreshVirtualDriverStatus();
                VirtualDriverUninstallBtn.IsEnabled = true;
                VirtualDriverUninstallBtn.Content = "Uninstall";
            }
        }

        private void RouteSoundboardPresetBtn_Click(object sender, RoutedEventArgs e)
        {
            int speakerIdx = VirtualAudioDriverService.FindVirtualSpeakerIndex();
            if (speakerIdx >= 0)
            {
                _settings.ListenOutputEnabled = true;
                _settings.ListenOutputDeviceIndex = speakerIdx;
                _settings.Save();

                System.Windows.MessageBox.Show(
                    this,
                    "✔ Soundboard Routing Configured!\n\n" +
                    "PaDDY pad clips will now play through your primary headset AND stream directly into the VB-Audio Virtual Cable.\n\n" +
                    "Next step:\n" +
                    "In Discord / OBS / Zoom / Game Settings, set your Input Device (Microphone) to:\n" +
                    "👉 'CABLE Output (VB-Audio Virtual Cable)'",
                    "Soundboard Route Preset Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    this,
                    "VB-Audio Virtual Cable output device was not found.\nPlease install the driver first.",
                    "Virtual Driver Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RouteLiveMicPresetBtn_Click(object sender, RoutedEventArgs e)
        {
            int speakerIdx = VirtualAudioDriverService.FindVirtualSpeakerIndex();
            if (speakerIdx >= 0 && speakerIdx < LiveMicOutputDeviceCombo.Items.Count)
            {
                LiveMicOutputDeviceCombo.SelectedIndex = speakerIdx;
                _settings.LiveMicOutputDeviceIndex = speakerIdx;
                _settings.LiveMicEnabled = true;

                System.Windows.MessageBox.Show(
                    this,
                    "✔ Live Voice Modulator Routing Configured!\n\n" +
                    "Live mic audio with DSP effects is now set to stream into the VB-Audio Virtual Cable.\n\n" +
                    "Next step:\n" +
                    "In Discord / OBS / Zoom / Game Settings, set your Input Device (Microphone) to:\n" +
                    "👉 'CABLE Output (VB-Audio Virtual Cable)'",
                    "Voice Modulator Route Preset Applied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    this,
                    "VB-Audio Virtual Cable output device was not found.\nPlease install the driver first.",
                    "Virtual Driver Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
        {
            VirtualAudioDriverService.OpenSoundSettings();
        }

        private void OpenClassicSoundPanel_Click(object sender, RoutedEventArgs e)
        {
            VirtualAudioDriverService.OpenSoundControlPanel();
        }

        #endregion
    }
}
