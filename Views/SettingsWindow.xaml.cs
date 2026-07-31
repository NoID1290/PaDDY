using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        public string SelectedFontVariant { get; private set; } = "condensed-display";
        public string SelectedDefaultPadTitleTemplate { get; private set; } = "Recording {timestamp}";
        public bool SelectedUseFocusedAppForPadTitle { get; private set; }
        public int SelectedTrimEditorOutputDeviceIndex { get; private set; }
        public bool SelectedNewRecordingsNonDestructive { get; private set; }

        // Appearance / system
        public string SelectedLanguage { get; private set; } = "en";
        public string SelectedTheme { get; private set; } = "dark";
        public string SelectedMeterSkin { get; private set; } = "default";
        public bool SelectedPerformanceMode { get; private set; }
        public bool SelectedPauseAnimationsWhenUnfocused { get; private set; }
        public bool SelectedMinimizeToTray { get; private set; }
        public bool SelectedCloseToTray { get; private set; }
        public bool SelectedStartMinimizedInTray { get; private set; }
        public bool SelectedRunOnWindowsStartup { get; private set; }
        public int SelectedDetectionAlgorithm { get; private set; }
        public bool SelectedAutoRenameWithSpeech { get; private set; }
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

        // Win32 ModKey flags
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();

            VstSettingsPanel.Visibility = Visibility.Visible;
            Vst3PluginRow.Visibility = App.IsDebugMode ? Visibility.Visible : Visibility.Collapsed;
            App.DebugModeChanged += OnDebugModeChanged;
            
            PaDDY.Services.SpeechRecognitionService.DownloadProgressUpdated += OnSpeechModelDownloadProgressUpdated;

            Loaded += OnLoaded;
            Closed += (_, _) =>
            {
                App.DebugModeChanged -= OnDebugModeChanged;
                PaDDY.Services.SpeechRecognitionService.DownloadProgressUpdated -= OnSpeechModelDownloadProgressUpdated;
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
            string pluginPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.paddy.sdPlugin");
            if (System.IO.Directory.Exists(pluginPath))
            {
                InstallStreamDeckBtn.Content = "Stream Deck Plugin Installed";
                InstallStreamDeckBtn.IsEnabled = false;
                InstallStreamDeckBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4CAF50"));
                InstallStreamDeckBtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            }

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
            VstPluginPathTextBox.Text = _settings.VstPluginPath;
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

            // Trim editor output & Live Mic
            PopulateTrimOutputDevices();
            PopulateLiveMicDevices();

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

            PerformanceModeCheck.IsChecked = _settings.PerformanceMode;
            PauseAnimationsWhenUnfocusedCheck.IsChecked = _settings.PauseAnimationsWhenUnfocused;

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
            bool nvidiaDetected = Helpers.GpuHelper.IsNvidiaGpuAvailable;
            UseCudaCheck.IsEnabled = nvidiaDetected;
            UseCudaCheck.IsChecked = nvidiaDetected && _settings.UseCudaForSpeech;
            if (nvidiaDetected)
            {
                CudaStatusText.Text = "NVIDIA GPU detected — CUDA acceleration available.";
                CudaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x60, 0x90, 0x60));
            }
            else
            {
                CudaStatusText.Text = "No NVIDIA GPU detected — CUDA acceleration unavailable.";
                CudaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x60, 0x60, 0x90));
            }

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
                : "condensed-display";

            SelectedDefaultPadTitleTemplate = string.IsNullOrWhiteSpace(DefaultPadTitleBox.Text)
                ? "Recording {timestamp}"
                : DefaultPadTitleBox.Text.Trim();
            SelectedUseFocusedAppForPadTitle = UseFocusedAppNameCheck.IsChecked == true;
            SelectedTrimEditorOutputDeviceIndex = TrimOutputDeviceCombo.SelectedIndex;
            SelectedNewRecordingsNonDestructive = NewRecordingsNonDestructiveCheck.IsChecked == true;

            int ti = ThemeCombo.SelectedIndex;
            SelectedTheme = (ti >= 0 && ti < ThemeManager.Themes.Count)
                ? ThemeManager.Themes[ti].Key : "dark";
            int si = MeterSkinCombo.SelectedIndex;
            SelectedMeterSkin = (si >= 0 && si < ThemeManager.MeterSkins.Count)
                ? ThemeManager.MeterSkins[si].Key : "default";
            SelectedPerformanceMode = PerformanceModeCheck.IsChecked == true;
            SelectedPauseAnimationsWhenUnfocused = PauseAnimationsWhenUnfocusedCheck.IsChecked == true;

            SelectedMinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            SelectedCloseToTray = CloseToTrayCheck.IsChecked == true;
            SelectedStartMinimizedInTray = StartMinimizedCheck.IsChecked == true;
            SelectedRunOnWindowsStartup = RunOnStartupCheck.IsChecked == true;
            SelectedAutoRenameWithSpeech = AutoRenameSpeechCheck.IsChecked == true;
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

            _settings.VstPluginPath = VstPluginPathTextBox.Text;
            _settings.Vst3PluginPath = Vst3PluginPathTextBox.Text;
            _settings.AutoNormalizeOnCapture = AutoNormalizeCheck.IsChecked == true;
            _settings.TargetLoudnessLufs = Math.Round(TargetLufsSlider.Value, 1);
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

        private void InstallStreamDeckBtn_Click(object sender, RoutedEventArgs e)
        {
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
                                InstallStreamDeckBtn.Content = "Stream Deck Plugin Installed";
                                InstallStreamDeckBtn.IsEnabled = false;
                                InstallStreamDeckBtn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4CAF50"));
                                InstallStreamDeckBtn.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
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

        private void BrowseVstButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "VST2 Plugins (*.dll)|*.dll|All Files (*.*)|*.*",
                Title = "Select VST2 Plugin"
            };

            if (dlg.ShowDialog(this) == true)
            {
                VstPluginPathTextBox.Text = dlg.FileName;
            }
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
    }
}
