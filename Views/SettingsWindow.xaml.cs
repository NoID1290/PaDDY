using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using NAudio.CoreAudioApi;
using PaDDY.Helpers;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;
        private List<(string Value, string Label)> _visibleCodecOptions = new();

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

        // Appearance / system
        public string SelectedTheme { get; private set; } = "dark";
        public string SelectedMeterSkin { get; private set; } = "default";
        public bool SelectedPerformanceMode { get; private set; }
        public bool SelectedMinimizeToTray { get; private set; }
        public bool SelectedCloseToTray { get; private set; }
        public bool SelectedStartMinimizedInTray { get; private set; }
        public bool SelectedRunOnWindowsStartup { get; private set; }
        public int SelectedDetectionAlgorithm { get; private set; }
        public bool SelectedAutoRenameWithSpeech { get; private set; }
        public string SelectedSpeechModel { get; private set; } = "base";
        public string SelectedSpeechLanguage { get; private set; } = "en";

        private static readonly (string Value, string Label)[] CodecOptions =
        {
            ("wav",  "WAV (LCPM FORMAT)"),
            ("mp3",  "MP3 (LAME)"),
            ("opus", "Opus (.opus)"),
            ("ogg",  "Ogg Vorbis (.ogg)"),
            ("flac", "FLAC (lossless)"),
        };

        private static readonly Dictionary<string, string> CodecDescriptions = new()
        {
            ["wav"] = "Lossless \u00b7 Raw audio LCPM format.",
            ["mp3"] = "Lossy \u00b7 Old, but still widely supported.",
            ["opus"] = "Lossy \u00b7 Optimised for voice.",
            ["ogg"] = "Lossy \u00b7 High efficiency, provides better audio quality than MP3.",
            ["flac"] = "Lossless \u00b7 Raw quality, better size than WAV.",
        };

        private uint _capturedVk;
        private bool _capturingKey;

        // Win32 ModKey flags
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT = 0x0001;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
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
            FontVariantCombo.Items.Clear();
            int fontIdx = 0;
            for (int i = 0; i < App.FontVariants.Count; i++)
            {
                var v = App.FontVariants[i];
                FontVariantCombo.Items.Add(v.DisplayName);
                if (v.Key == _settings.AppFontVariant) fontIdx = i;
            }
            FontVariantCombo.SelectedIndex = fontIdx;

            // New pad naming
            DefaultPadTitleBox.Text = string.IsNullOrWhiteSpace(_settings.DefaultPadTitleTemplate)
                ? "Recording {timestamp}"
                : _settings.DefaultPadTitleTemplate;
            UseFocusedAppNameCheck.IsChecked = _settings.UseFocusedAppForPadTitle;

            // Trim editor output
            PopulateTrimOutputDevices();

            // Appearance: theme + meter skin
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

            PerformanceModeCheck.IsChecked = _settings.PerformanceMode;

            // System tray / startup
            MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
            CloseToTrayCheck.IsChecked = _settings.CloseToTray;
            StartMinimizedCheck.IsChecked = _settings.StartMinimizedInTray;
            RunOnStartupCheck.IsChecked = _settings.RunOnWindowsStartup;

            // Detection algorithm is chosen from the main window's Mode combo;
            // preserve the current value so committing settings won't change it.
            SelectedDetectionAlgorithm = _settings.DetectionAlgorithm;

            // Speech-to-text
            AutoRenameSpeechCheck.IsChecked = _settings.AutoRenameWithSpeech;
            SpeechModelCombo.Items.Clear();
            string[] models = { "tiny", "base", "small" };
            int modelIdx = 1;
            for (int i = 0; i < models.Length; i++)
            {
                SpeechModelCombo.Items.Add(models[i]);
                if (models[i] == _settings.SpeechModel) modelIdx = i;
            }
            SpeechModelCombo.SelectedIndex = modelIdx;
            SpeechLanguageBox.Text = string.IsNullOrWhiteSpace(_settings.SpeechLanguage) ? "en" : _settings.SpeechLanguage;
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
                ThemeManager.ApplyMeterSkin(ThemeManager.MeterSkins[i].Key); // live preview
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

            int ti = ThemeCombo.SelectedIndex;
            SelectedTheme = (ti >= 0 && ti < ThemeManager.Themes.Count)
                ? ThemeManager.Themes[ti].Key : "dark";
            int si = MeterSkinCombo.SelectedIndex;
            SelectedMeterSkin = (si >= 0 && si < ThemeManager.MeterSkins.Count)
                ? ThemeManager.MeterSkins[si].Key : "default";
            SelectedPerformanceMode = PerformanceModeCheck.IsChecked == true;

            SelectedMinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
            SelectedCloseToTray = CloseToTrayCheck.IsChecked == true;
            SelectedStartMinimizedInTray = StartMinimizedCheck.IsChecked == true;
            SelectedRunOnWindowsStartup = RunOnStartupCheck.IsChecked == true;
            SelectedAutoRenameWithSpeech = AutoRenameSpeechCheck.IsChecked == true;
            SelectedSpeechModel = SpeechModelCombo.SelectedItem?.ToString() ?? "base";
            SelectedSpeechLanguage = string.IsNullOrWhiteSpace(SpeechLanguageBox.Text) ? "en" : SpeechLanguageBox.Text.Trim();

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // If the dialog was not confirmed, revert any live theme/meter preview.
            if (DialogResult != true)
            {
                ThemeManager.ApplyTheme(_settings.Theme);
                ThemeManager.ApplyMeterSkin(_settings.MeterSkin);
            }
            base.OnClosing(e);
        }
    }
}
