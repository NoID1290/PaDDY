using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // Codec — populate first then rebuild dependent controls
            CodecCombo.Items.Clear();
            foreach (var (_, label) in CodecOptions)
                CodecCombo.Items.Add(label);
            // Temporarily detach SelectionChanged to avoid double-rebuild during init
            CodecCombo.SelectionChanged -= CodecCombo_SelectionChanged;
            int codecIdx = Array.FindIndex(CodecOptions, c => c.Value == _settings.RecordCodec);
            CodecCombo.SelectedIndex = codecIdx >= 0 ? codecIdx : 0;
            CodecCombo.SelectionChanged += CodecCombo_SelectionChanged;

            // Sample rate / Bit depth — built by helper so codec constraints are applied
            RebuildFormatControls(
                CodecOptions[CodecCombo.SelectedIndex >= 0 ? CodecCombo.SelectedIndex : 0].Value,
                _settings.RecordSampleRate,
                _settings.RecordBitDepth);

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

        private void CodecCombo_SelectionChanged(object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SampleRateCombo == null || BitDepthCombo == null) return;
            int ci = CodecCombo.SelectedIndex;
            string codec = ci >= 0 && ci < CodecOptions.Length ? CodecOptions[ci].Value : "wav";

            // Remember current selections before rebuild
            int currentSr = GetSelectedSampleRate();
            int currentBd = BitDepthCombo.SelectedIndex == 1 ? 32 : 16;
            RebuildFormatControls(codec, currentSr, currentBd);
        }

        /// <summary>
        /// Rebuilds SampleRateCombo and BitDepthCombo to show only options valid for the given codec.
        /// Tries to preserve the previously selected sample rate and bit depth where possible.
        /// </summary>
        private void RebuildFormatControls(string codec, int preferredSr, int preferredBd)
        {
            // Opus: only supports 8000, 12000, 16000, 24000, 48000 Hz; PCM 16-bit only
            // MP3 (LAME): 32-bit float not supported
            // WAV, Ogg: all combinations valid

            int[] validRates = codec == "opus"
                ? SampleRates.Where(r => r != 44100).ToArray()
                : SampleRates;

            bool allow32bit = codec != "opus" && codec != "mp3";

            // Rebuild sample rate combo
            SampleRateCombo.Items.Clear();
            foreach (var sr in validRates)
                SampleRateCombo.Items.Add($"{sr / 1000.0:0.#} kHz");
            // Select closest available rate
            int srIdx = Array.IndexOf(validRates, preferredSr);
            if (srIdx < 0)
            {
                // Preferred rate not available — pick nearest
                int nearest = validRates.OrderBy(r => Math.Abs(r - preferredSr)).First();
                srIdx = Array.IndexOf(validRates, nearest);
            }
            SampleRateCombo.SelectedIndex = Math.Max(0, srIdx);

            // Rebuild bit depth combo
            BitDepthCombo.Items.Clear();
            BitDepthCombo.Items.Add("16-bit PCM");
            if (allow32bit)
                BitDepthCombo.Items.Add("32-bit float");
            BitDepthCombo.SelectedIndex = (allow32bit && preferredBd == 32) ? 1 : 0;
        }

        private int GetSelectedSampleRate()
        {
            // Determine which rate array is currently shown based on codec
            int ci = CodecCombo.SelectedIndex;
            string codec = ci >= 0 && ci < CodecOptions.Length ? CodecOptions[ci].Value : "wav";
            int[] validRates = codec == "opus"
                ? SampleRates.Where(r => r != 44100).ToArray()
                : SampleRates;
            int idx = SampleRateCombo.SelectedIndex;
            return idx >= 0 && idx < validRates.Length ? validRates[idx] : 16000;
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
            SelectedSampleRate = GetSelectedSampleRate();
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
