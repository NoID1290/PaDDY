using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using PaDDY.Helpers;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class SettingsWindow : Window
    {
        private readonly AppSettings _settings;

        // Resolved output values
        public int SelectedSampleRate { get; private set; }
        public int SelectedBitDepth { get; private set; }
        public int SelectedChannels { get; private set; }
        public string SelectedCodec { get; private set; } = "wav";
        public string SelectedSaveFolder { get; private set; } = string.Empty;
        public int SelectedBufferDurationMs { get; private set; }
        public uint SelectedHotKeyModifiers { get; private set; }
        public uint SelectedHotKeyVk { get; private set; }
        public int SelectedMaxRecords { get; private set; }

        private static readonly int[] SampleRates = { 8000, 16000, 44100, 48000 };
        private static readonly int[] BitDepths = { 16, 32 };
        private static readonly (int Value, string Label)[] ChannelOptions =
        {
            (1, "Mono (1ch)"),
            (2, "Stereo (2ch)")
        };
        private static readonly (string Value, string Label)[] CodecOptions =
        {
            ("wav",  "WAV (uncompressed)"),
            ("mp3",  "MP3 (LAME)"),
            ("opus", "Opus (.opus)"),
            ("ogg",  "Ogg Vorbis (.ogg)"),
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
            CodecCombo.Items.Clear();
            foreach (var (_, label) in CodecOptions)
                CodecCombo.Items.Add(label);
            int codecIdx = Array.FindIndex(CodecOptions, c => c.Value == _settings.RecordCodec);
            CodecCombo.SelectedIndex = codecIdx >= 0 ? codecIdx : 0;

            // Sample rate
            SampleRateCombo.Items.Clear();
            foreach (var sr in SampleRates)
                SampleRateCombo.Items.Add($"{sr / 1000.0:0.#} kHz");
            SampleRateCombo.SelectedIndex = Math.Max(0,
                Array.IndexOf(SampleRates, _settings.RecordSampleRate));

            // Bit depth
            BitDepthCombo.Items.Clear();
            BitDepthCombo.Items.Add("16-bit PCM");
            BitDepthCombo.Items.Add("32-bit float");
            BitDepthCombo.SelectedIndex = _settings.RecordBitDepth == 32 ? 1 : 0;

            // Channels
            ChannelsCombo.Items.Clear();
            foreach (var (_, label) in ChannelOptions)
                ChannelsCombo.Items.Add(label);
            ChannelsCombo.SelectedIndex = _settings.RecordChannels == 2 ? 1 : 0;

            // Save folder
            SaveFolderBox.Text = string.IsNullOrWhiteSpace(_settings.SaveFolder)
                ? Path.Combine(AppContext.BaseDirectory, "recordings")
                : _settings.SaveFolder;

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
        }

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = "Select folder to save recordings",
                UseDescriptionForTitle = true,
                SelectedPath = SaveFolderBox.Text
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                SaveFolderBox.Text = dlg.SelectedPath;
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

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            int srIdx = SampleRateCombo.SelectedIndex;
            SelectedSampleRate = srIdx >= 0 && srIdx < SampleRates.Length ? SampleRates[srIdx] : 16000;
            SelectedBitDepth = BitDepthCombo.SelectedIndex == 1 ? 32 : 16;
            SelectedChannels = ChannelsCombo.SelectedIndex == 1 ? 2 : 1;
            int ci = CodecCombo.SelectedIndex;
            SelectedCodec = ci >= 0 && ci < CodecOptions.Length ? CodecOptions[ci].Value : "wav";
            SelectedSaveFolder = SaveFolderBox.Text;
            SelectedBufferDurationMs = (int)(BufferDurationSlider.Value * 1000);

            uint mods = 0;
            if (ModCtrl.IsChecked == true) mods |= MOD_CONTROL;
            if (ModAlt.IsChecked == true) mods |= MOD_ALT;
            if (ModShift.IsChecked == true) mods |= MOD_SHIFT;
            SelectedHotKeyModifiers = mods;
            SelectedHotKeyVk = _capturedVk;
            SelectedMaxRecords = (int)MaxRecordsSlider.Value;

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
