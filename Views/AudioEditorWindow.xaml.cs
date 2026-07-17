using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NoIDSoftwork.AudioProcessor;
using NoIDSoftwork.EffectProcessor;
using NoIDSoftwork.EffectProcessor.Effects;
using PaDDY.Helpers;
using PaDDY.Models;
using PaDDY.Services;
using PaDDY.Views;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class AudioEditorWindow : Window
    {
        private readonly string _filePath;
        private readonly string? _recordingId;
        private readonly int _outputDeviceIndex;
        private IEffectChain? _perClipChain;
        private TimeSpan _totalDuration;
        private double _trimStartFraction;  // 0.0 – 1.0
        private double _trimEndFraction = 1.0;

        private IWavePlayer? _player;
        private IUnifiedAudioReader? _reader;
        private bool _isPreviewing;
        private bool _isStoppingPreview;

        // Tracks the playback start wall-clock time and start position for animation restart on resize
        private DateTime _playbackStartedAt;
        private double _playbackStartSec;
        private double _playbackEndSec;

        private double _totalDurationSeconds;
        private double _waveformWidth;
        private double _gainDb = 0.0;

        private static readonly SolidColorBrush PeakHotBrush = new(System.Windows.Media.Color.FromRgb(0xFF, 0xC1, 0x07));
        private static readonly SolidColorBrush MeterOverlayBrush = new(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A));
        static AudioEditorWindow() { PeakHotBrush.Freeze(); MeterOverlayBrush.Freeze(); }

        // Inline effects panel
        private bool _effectsLoading = true; // suppresses slider events until LoadEffectValues() runs
        private FadeEffect? _fade;
        private NoiseGateEffect? _gate;
        private PitchShiftEffect? _pitchShift;
        private EchoEffect? _echo;
        private EqualizerEffect? _eq;
        private CompressorEffect? _compressor;
        private DistortionEffect? _distortion;
        private ReverbEffect? _reverb;
        private Vst2Effect? _vstEffect;

        private const double MinTrimSeconds = 0.05; // 50 ms minimum

        // Stored waveform peaks for gain-responsive re-render
        private (float min, float max)[]? _originalPeaks;

        // Vertical meter state
        private MeteringSampleProvider? _meterProvider;
        private VolumeSampleProvider? _previewGainProvider;
        private readonly List<WpfRectangle> _vertMeterOverlays = new();
        private readonly List<Border> _vertPeakLines = new();
        private readonly List<double> _vertPeakFracs = new();
        private readonly List<DateTime> _vertPeakHeldAt = new();

        public string? CopyFilePath { get; private set; }
        public bool ShouldSaveToFavorite => SaveToFavCheckBox.IsChecked == true;

        public bool OutIsNonDestructive { get; private set; }
        public double OutTrimStartFraction { get; private set; }
        public double OutTrimEndFraction { get; private set; }
        public double OutGainDb { get; private set; }

        private readonly bool _initialIsNonDestructive;
        private readonly long _initialTrimStartMs;
        private readonly long _initialTrimEndMs;
        private readonly double _initialGainDb;
        private bool _isChangingNonDestructive;

        public AudioEditorWindow(string filePath, string? recordingId = null, int outputDeviceIndex = -1, string? displayName = null,
            bool isNonDestructive = false, long trimStartMs = 0, long trimEndMs = 0, double gainDb = 0.0)
        {
            InitializeComponent();
            _filePath = filePath;
            _recordingId = recordingId;
            _outputDeviceIndex = outputDeviceIndex;
            _initialIsNonDestructive = isNonDestructive;
            _initialTrimStartMs = trimStartMs;
            _initialTrimEndMs = trimEndMs;
            _initialGainDb = gainDb;

            FileNameLabel.Text = !string.IsNullOrEmpty(displayName) ? displayName : Path.GetFileName(filePath); // Get real name
            //FileNameLabel.Text = Path.GetFileNameWithoutExtension(filePath); // Get raw name

            Loaded += OnLoaded;
            WaveformGrid.SizeChanged += WaveformGrid_SizeChanged;
            Closed += (_, _) => StopPreview();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                using var reader = AudioReaderFactory.Open(_filePath);
                _totalDuration = reader.TotalTime;
                _totalDurationSeconds = Math.Max(_totalDuration.TotalSeconds, 0.001);
                TotalDurationLabel.Text = FormatTime(_totalDuration);

                // Load initial settings
                if (_initialIsNonDestructive)
                {
                    _trimStartFraction = Math.Clamp((double)_initialTrimStartMs / 1000.0 / _totalDurationSeconds, 0.0, 1.0);
                    if (_initialTrimEndMs > 0)
                        _trimEndFraction = Math.Clamp((double)_initialTrimEndMs / 1000.0 / _totalDurationSeconds, _trimStartFraction, 1.0);
                    else
                        _trimEndFraction = 1.0;

                    _gainDb = _initialGainDb;
                }
                else
                {
                    _trimStartFraction = 0.0;
                    _trimEndFraction = 1.0;
                    _gainDb = 0.0;
                }

                RenderWaveform(reader.AsSampleProvider(), reader.WaveFormat);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not read audio file:\n{ex.Message}", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            UpdateHandlePositions();
            UpdateTimeLabels();
            EnsureVertMeterChannels(2);
            ResetVertMeter();

            NonDestructiveCheckBox.Checked -= NonDestructiveCheckBox_Checked;
            NonDestructiveCheckBox.Unchecked -= NonDestructiveCheckBox_Unchecked;
            NonDestructiveCheckBox.IsChecked = _initialIsNonDestructive;
            NonDestructiveCheckBox.Checked += NonDestructiveCheckBox_Checked;
            NonDestructiveCheckBox.Unchecked += NonDestructiveCheckBox_Unchecked;

            GainSlider.Value = _gainDb;

            // Load per-clip effect chain into inline panel
            _perClipChain = GetOrLoadEffectChain();
            foreach (var effect in _perClipChain.Effects)
            {
                switch (effect)
                {
                    case FadeEffect f: _fade = f; break;
                    case NoiseGateEffect g: _gate = g; break;
                    case PitchShiftEffect p: _pitchShift = p; break;
                    case EchoEffect ec: _echo = ec; break;
                    case EqualizerEffect q: _eq = q; break;
                    case CompressorEffect c: _compressor = c; break;
                    case DistortionEffect d: _distortion = d; break;
                    case ReverbEffect r: _reverb = r; break;
                }
            }
            LoadEffectValues();

            var settings = AppSettings.Load();
            if (!string.IsNullOrEmpty(settings.VstPluginPath) && 
                System.IO.File.Exists(settings.VstPluginPath))
            {
                try
                {
                    _vstEffect = new Vst2Effect(settings.VstPluginPath);
                    _perClipChain.Add(_vstEffect);
                    VstNameLabel.Text = _vstEffect.Name;
                    ShowVstEditorButton.IsEnabled = true;
                }
                catch (Exception ex)
                {
                    VstNameLabel.Text = "Load failed";
                    Console.WriteLine("VST Load error: " + ex);
                }
            }
        }

        private void WaveformGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            UpdateHandlePositions();

            if (_isPreviewing && _reader != null)
            {
                // Restart the linear animation from current playback position with remaining duration
                double elapsed = (DateTime.UtcNow - _playbackStartedAt).TotalSeconds;
                double currentSec = Math.Clamp(_playbackStartSec + elapsed, _playbackStartSec, _playbackEndSec);
                double remaining = _playbackEndSec - currentSec;
                if (remaining > 0)
                    StartPlaybackAnimation(currentSec, _playbackEndSec, TimeSpan.FromSeconds(remaining));
                else
                    UpdatePlaybackLinePosition(currentSec);
            }
        }

        // ── Waveform rendering ──────────────────────────────────────────────

        private void GainSlider_ValueChanged(object sender,
            System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            _gainDb = e.NewValue;
            if (GainLabel != null)
                GainLabel.Text = _gainDb == 0.0 ? "0 dB" : $"{_gainDb:+0;-0} dB";

            if (_previewGainProvider != null)
                _previewGainProvider.Volume = GainDbToFactor(_gainDb);

            if (_originalPeaks != null)
                RenderWaveformFromPeaks();
        }

        private static float GainDbToFactor(double gainDb)
            => (float)Math.Pow(10.0, gainDb / 20.0);

        private void RenderWaveform(ISampleProvider sampleProvider, WaveFormat waveFormat)
        {
            int width = (int)WaveformGrid.ActualWidth;
            int height = (int)WaveformGrid.ActualHeight;
            if (width < 10 || height < 10) { width = 680; height = 180; }

            int channels = waveFormat.Channels;
            long totalMonoSamples = (long)(_totalDurationSeconds * waveFormat.SampleRate);

            float[] buffer = new float[waveFormat.SampleRate * channels]; // 1 sec chunks
            var peaks = new (float min, float max)[width];
            for (int i = 0; i < width; i++)
                peaks[i] = (0f, 0f);

            long samplesRead = 0;
            int read;
            while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
            {
                int monoRead = read / channels;
                for (int i = 0; i < monoRead; i++)
                {
                    float sample = 0f;
                    for (int ch = 0; ch < channels; ch++)
                        sample += buffer[i * channels + ch];
                    sample /= channels;

                    long monoIndex = samplesRead + i;
                    int bucket = totalMonoSamples > 0
                        ? (int)(monoIndex * width / totalMonoSamples)
                        : 0;
                    if (bucket >= width) bucket = width - 1;

                    if (sample < peaks[bucket].min) peaks[bucket] = (sample, peaks[bucket].max);
                    if (sample > peaks[bucket].max) peaks[bucket] = (peaks[bucket].min, sample);
                }
                samplesRead += monoRead;
            }

            // Store peaks and delegate rendering to RenderWaveformFromPeaks (supports gain preview)
            _originalPeaks = peaks;
            RenderWaveformFromPeaks();
        }

        private void RenderWaveformFromPeaks()
        {
            if (_originalPeaks == null) return;

            int width = (int)WaveformGrid.ActualWidth;
            int height = (int)WaveformGrid.ActualHeight;
            if (width < 10 || height < 10) { width = 680; height = 180; }

            float gainFactor = (float)Math.Pow(10.0, _gainDb / 20.0);

            // Read accent color from the active theme so the waveform follows theme changes.
            var accent = GetThemeAccentColor();
            byte aR = accent.R, aG = accent.G, aB = accent.B;

            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            int midY = height / 2;

            // Draw centre line using a dimmed version of the accent color
            for (int x = 0; x < width; x++)
                SetPixel(pixels, stride, x, midY,
                    (byte)(aR * 0.22f), (byte)(aG * 0.22f), (byte)(aB * 0.22f), 0xFF);

            // Draw waveform with gain applied
            int peakLen = _originalPeaks.Length;
            for (int x = 0; x < width && x < peakLen; x++)
            {
                float pMin = Math.Clamp(_originalPeaks[x].min * gainFactor, -1f, 1f);
                float pMax = Math.Clamp(_originalPeaks[x].max * gainFactor, -1f, 1f);

                int yTop = midY - (int)(pMax * midY);
                int yBot = midY - (int)(pMin * midY);

                yTop = Math.Clamp(yTop, 0, height - 1);
                yBot = Math.Clamp(yBot, 0, height - 1);

                for (int y = yTop; y <= yBot; y++)
                {
                    // Bright near the centre, slightly dimmer at the amplitude peaks
                    float dist = Math.Abs(y - midY) / (float)midY;
                    float brightness = 0.92f - dist * 0.38f;
                    byte r = (byte)Math.Clamp(aR * brightness, 0, 255);
                    byte g = (byte)Math.Clamp(aG * brightness, 0, 255);
                    byte b = (byte)Math.Clamp(aB * brightness, 0, 255);
                    SetPixel(pixels, stride, x, y, r, g, b, 0xFF);
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            WaveformImage.Source = bmp;
        }

        /// <summary>
        /// Returns the current theme's accent color from <see cref="Application.Current"/> resources.
        /// Falls back to a neutral green if the resource is unavailable.
        /// </summary>
        private static System.Windows.Media.Color GetThemeAccentColor()
        {
            if (System.Windows.Application.Current?.Resources["AccentGreenBrush"] is SolidColorBrush brush)
                return brush.Color;
            return System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50); // dark-theme green fallback
        }

        private static void SetPixel(byte[] pixels, int stride, int x, int y, byte r, byte g, byte b, byte a)
        {
            int idx = y * stride + x * 4;
            pixels[idx] = b;
            pixels[idx + 1] = g;
            pixels[idx + 2] = r;
            pixels[idx + 3] = a;
        }


        // ── Handle dragging ─────────────────────────────────────────────────

        private void LeftHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double waveWidth = _waveformWidth;
            if (waveWidth <= 0) return;

            double minTrimFraction = MinTrimSeconds / _totalDurationSeconds;
            double delta = e.HorizontalChange / waveWidth;
            _trimStartFraction = Math.Clamp(_trimStartFraction + delta, 0.0, _trimEndFraction - minTrimFraction);

            UpdateHandlePositions();
            UpdateTimeLabels();
        }

        private void RightHandle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double waveWidth = _waveformWidth;
            if (waveWidth <= 0) return;

            double minTrimFraction = MinTrimSeconds / _totalDurationSeconds;
            double delta = e.HorizontalChange / waveWidth;
            _trimEndFraction = Math.Clamp(_trimEndFraction + delta, _trimStartFraction + minTrimFraction, 1.0);

            UpdateHandlePositions();
            UpdateTimeLabels();
        }

        private void UpdateHandlePositions()
        {
            double w = _waveformWidth;
            if (w <= 0) return;

            double leftPx = _trimStartFraction * w;
            double rightPx = (1.0 - _trimEndFraction) * w;

            double maxLeftMargin = Math.Max(0, w - LeftHandle.Width);
            double maxRightMargin = Math.Max(0, w - RightHandle.Width);

            // Keep handles visible at the waveform edges and centered on trim boundaries.
            double leftMargin = Math.Clamp(leftPx - (LeftHandle.Width / 2.0), 0, maxLeftMargin);
            double rightMargin = Math.Clamp(rightPx - (RightHandle.Width / 2.0), 0, maxRightMargin);

            LeftHandle.Margin = new Thickness(leftMargin, 0, 0, 0);
            RightHandle.Margin = new Thickness(0, 0, rightMargin, 0);

            LeftOverlay.Width = Math.Max(0, leftPx);
            RightOverlay.Width = Math.Max(0, rightPx);
        }

        private void UpdateTimeLabels()
        {
            double startSec = _trimStartFraction * _totalDuration.TotalSeconds;
            double endSec = _trimEndFraction * _totalDuration.TotalSeconds;
            double trimmed = endSec - startSec;

            StartTimeLabel.Text = $"{startSec:0.00}s";
            EndTimeLabel.Text = $"{endSec:0.00}s";
            TrimmedDurationLabel.Text = $"Trimmed: {FormatTime(TimeSpan.FromSeconds(trimmed))}";

            SaveBtn.IsEnabled = trimmed >= MinTrimSeconds;
        }

        // ── Playback preview ────────────────────────────────────────────────

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Inline effects panel ────────────────────────────────────────────

        private void LoadEffectValues()
        {
            _effectsLoading = true;
            try
            {
                if (_fade != null)
                {
                    FadeEnabledCheck.IsChecked = _fade.IsEnabled;
                    FadeInSlider.Value = _fade.FadeInDurationMs;
                    FadeOutSlider.Value = _fade.FadeOutDurationMs;
                }
                if (_gate != null)
                {
                    GateEnabledCheck.IsChecked = _gate.IsEnabled;
                    GateThresholdSlider.Value = _gate.ThresholdDb;
                    GateAttackSlider.Value = _gate.AttackMs;
                    GateReleaseSlider.Value = _gate.ReleaseMs;
                }
                if (_echo != null)
                {
                    EchoEnabledCheck.IsChecked = _echo.IsEnabled;
                    EchoDelaySlider.Value = _echo.DelayMs;
                    EchoFeedbackSlider.Value = _echo.Feedback;
                    EchoMixSlider.Value = _echo.Mix;
                }
                if (_eq != null)
                {
                    EqEnabledCheck.IsChecked = _eq.IsEnabled;
                    EqSubBassSlider.Value = _eq.SubBassDb;
                    EqBassSlider.Value = _eq.BassDb;
                    EqMidSlider.Value = _eq.MidDb;
                    EqPresenceSlider.Value = _eq.PresenceDb;
                    EqTrebleSlider.Value = _eq.TrebleDb;
                }
                if (_compressor != null)
                {
                    CompressorEnabledCheck.IsChecked = _compressor.IsEnabled;
                    CompThresholdSlider.Value = _compressor.ThresholdDb;
                    CompRatioSlider.Value = _compressor.Ratio;
                    CompAttackSlider.Value = _compressor.AttackMs;
                    CompReleaseSlider.Value = _compressor.ReleaseMs;
                    CompMakeupSlider.Value = _compressor.MakeupDb;
                }
                if (_distortion != null)
                {
                    DistortionEnabledCheck.IsChecked = _distortion.IsEnabled;
                    DistDriveSlider.Value = _distortion.Drive;
                    DistMixSlider.Value = _distortion.Mix;
                    DistOutputSlider.Value = _distortion.OutputLevel;
                }
                if (_reverb != null)
                {
                    ReverbEnabledCheck.IsChecked = _reverb.IsEnabled;
                    ReverbRoomSlider.Value = _reverb.RoomSize;
                    ReverbDampingSlider.Value = _reverb.Damping;
                    ReverbMixSlider.Value = _reverb.Mix;
                }
                if (_pitchShift != null)
                {
                    PitchShiftEnabledCheck.IsChecked = _pitchShift.IsEnabled;
                    PitchShiftSemitonesSlider.Value = _pitchShift.PitchSemitones;
                    PitchShiftGrainSizeSlider.Value = _pitchShift.GrainSizeMs;
                    PitchShiftMixSlider.Value = _pitchShift.Mix;
                }
                UpdateEffectLabels();
            }
            finally
            {
                _effectsLoading = false;
            }
        }

        private void UpdateEffectLabels()
        {
            FadeInLabel.Text = $"{(int)FadeInSlider.Value}";
            FadeOutLabel.Text = $"{(int)FadeOutSlider.Value}";
            GateThresholdLabel.Text = $"{(int)GateThresholdSlider.Value}";
            GateAttackLabel.Text = $"{(int)GateAttackSlider.Value}";
            GateReleaseLabel.Text = $"{(int)GateReleaseSlider.Value}";
            EchoDelayLabel.Text = $"{(int)EchoDelaySlider.Value}";
            EchoFeedbackLabel.Text = $"{EchoFeedbackSlider.Value:F2}";
            EchoMixLabel.Text = $"{EchoMixSlider.Value:F2}";
            EqSubBassLabel.Text = $"{(int)EqSubBassSlider.Value:+#;-#;0} dB";
            EqBassLabel.Text = $"{(int)EqBassSlider.Value:+#;-#;0} dB";
            EqMidLabel.Text = $"{(int)EqMidSlider.Value:+#;-#;0} dB";
            EqPresenceLabel.Text = $"{(int)EqPresenceSlider.Value:+#;-#;0} dB";
            EqTrebleLabel.Text = $"{(int)EqTrebleSlider.Value:+#;-#;0} dB";
            CompThresholdLabel.Text = $"{(int)CompThresholdSlider.Value}";
            CompRatioLabel.Text = $"{CompRatioSlider.Value:F1}";
            CompAttackLabel.Text = $"{(int)CompAttackSlider.Value}";
            CompReleaseLabel.Text = $"{(int)CompReleaseSlider.Value}";
            CompMakeupLabel.Text = $"{(int)CompMakeupSlider.Value}";
            DistDriveLabel.Text = $"{(int)DistDriveSlider.Value}";
            DistMixLabel.Text = $"{DistMixSlider.Value:F2}";
            DistOutputLabel.Text = $"{DistOutputSlider.Value:F2}";
            ReverbRoomLabel.Text = $"{ReverbRoomSlider.Value:F2}";
            ReverbDampingLabel.Text = $"{ReverbDampingSlider.Value:F2}";
            ReverbMixLabel.Text = $"{ReverbMixSlider.Value:F2}";
            if (PitchShiftSemitonesSlider != null)
                PitchShiftSemitonesLabel.Text = $"{(int)PitchShiftSemitonesSlider.Value:+#;-#;0}";
            if (PitchShiftGrainSizeSlider != null)
                PitchShiftGrainSizeLabel.Text = $"{(int)PitchShiftGrainSizeSlider.Value}";
            if (PitchShiftMixSlider != null)
                PitchShiftMixLabel.Text = $"{PitchShiftMixSlider.Value:F2}";
        }

        private void CommitEffectsToChain()
        {
            if (_fade != null)
            {
                _fade.IsEnabled = FadeEnabledCheck.IsChecked == true;
                _fade.FadeInDurationMs = FadeInSlider.Value;
                _fade.FadeOutDurationMs = FadeOutSlider.Value;
            }
            if (_gate != null)
            {
                _gate.IsEnabled = GateEnabledCheck.IsChecked == true;
                _gate.ThresholdDb = GateThresholdSlider.Value;
                _gate.AttackMs = GateAttackSlider.Value;
                _gate.ReleaseMs = GateReleaseSlider.Value;
            }
            if (_echo != null)
            {
                _echo.IsEnabled = EchoEnabledCheck.IsChecked == true;
                _echo.DelayMs = EchoDelaySlider.Value;
                _echo.Feedback = EchoFeedbackSlider.Value;
                _echo.Mix = EchoMixSlider.Value;
            }
            if (_eq != null)
            {
                _eq.IsEnabled = EqEnabledCheck.IsChecked == true;
                _eq.SubBassDb = EqSubBassSlider.Value;
                _eq.BassDb = EqBassSlider.Value;
                _eq.MidDb = EqMidSlider.Value;
                _eq.PresenceDb = EqPresenceSlider.Value;
                _eq.TrebleDb = EqTrebleSlider.Value;
            }
            if (_compressor != null)
            {
                _compressor.IsEnabled = CompressorEnabledCheck.IsChecked == true;
                _compressor.ThresholdDb = CompThresholdSlider.Value;
                _compressor.Ratio = CompRatioSlider.Value;
                _compressor.AttackMs = CompAttackSlider.Value;
                _compressor.ReleaseMs = CompReleaseSlider.Value;
                _compressor.MakeupDb = CompMakeupSlider.Value;
            }
            if (_distortion != null)
            {
                _distortion.IsEnabled = DistortionEnabledCheck.IsChecked == true;
                _distortion.Drive = DistDriveSlider.Value;
                _distortion.Mix = DistMixSlider.Value;
                _distortion.OutputLevel = DistOutputSlider.Value;
            }
            if (_reverb != null)
            {
                _reverb.IsEnabled = ReverbEnabledCheck.IsChecked == true;
                _reverb.RoomSize = ReverbRoomSlider.Value;
                _reverb.Damping = ReverbDampingSlider.Value;
                _reverb.Mix = ReverbMixSlider.Value;
            }
            if (_pitchShift != null)
            {
                _pitchShift.IsEnabled = PitchShiftEnabledCheck.IsChecked == true;
                _pitchShift.PitchSemitones = PitchShiftSemitonesSlider.Value;
                _pitchShift.GrainSizeMs = PitchShiftGrainSizeSlider.Value;
                _pitchShift.Mix = PitchShiftMixSlider.Value;
            }
            SaveEffectSettings();
        }

        private void SaveEffectSettings()
        {
            if (string.IsNullOrEmpty(_recordingId)) return;
            var settings = EffectSettingsManager.Load();
            settings.PerClipChains[_recordingId!] = EffectSettingsManager.ToConfig(GetOrLoadEffectChain());
            EffectSettingsManager.Save(settings);
        }

        private void EffectSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_effectsLoading) return;
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void EffectEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_effectsLoading) return;
            CommitEffectsToChain();
        }

        private void EffectsPanelChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            bool expand = EffectsPanelContent.Visibility == Visibility.Collapsed;
            EffectsPanelContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            EffectsPanelChevron.Text = expand ? "\u25C4" : "\u25BA";
        }

        private void FadeHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = FadeContent.Visibility == Visibility.Collapsed;
            FadeContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            FadeChevron.Text = expand ? "▼" : "►";
        }

        private void GateHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = GateContent.Visibility == Visibility.Collapsed;
            GateContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            GateChevron.Text = expand ? "▼" : "►";
        }

        private void EchoHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = EchoContent.Visibility == Visibility.Collapsed;
            EchoContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            EchoChevron.Text = expand ? "▼" : "►";
        }

        private void EqHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = EqContent.Visibility == Visibility.Collapsed;
            EqContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            EqChevron.Text = expand ? "▼" : "►";
        }

        private void CompressorHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = CompressorContent.Visibility == Visibility.Collapsed;
            CompressorContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            CompressorChevron.Text = expand ? "▼" : "►";
        }

        private void DistortionHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = DistortionContent.Visibility == Visibility.Collapsed;
            DistortionContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            DistortionChevron.Text = expand ? "▼" : "►";
        }

        private void ReverbHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = ReverbContent.Visibility == Visibility.Collapsed;
            ReverbContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            ReverbChevron.Text = expand ? "▼" : "►";
        }

        private void PitchShiftHeaderButton_Click(object sender, RoutedEventArgs e)
        {
            bool expand = PitchShiftContent.Visibility == Visibility.Collapsed;
            PitchShiftContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            PitchShiftChevron.Text = expand ? "▼" : "►";
        }

        private void ResetEffects_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                FadeEnabledCheck.IsChecked = false;
                FadeInSlider.Value = 500;
                FadeOutSlider.Value = 500;
                GateEnabledCheck.IsChecked = false;
                GateThresholdSlider.Value = -40;
                GateAttackSlider.Value = 10;
                GateReleaseSlider.Value = 100;
                EchoEnabledCheck.IsChecked = false;
                EchoDelaySlider.Value = 200;
                EchoFeedbackSlider.Value = 0.3;
                EchoMixSlider.Value = 0.4;
                EqEnabledCheck.IsChecked = false;
                EqSubBassSlider.Value = 0;
                EqBassSlider.Value = 0;
                EqMidSlider.Value = 0;
                EqPresenceSlider.Value = 0;
                EqTrebleSlider.Value = 0;
                CompressorEnabledCheck.IsChecked = false;
                CompThresholdSlider.Value = -18;
                CompRatioSlider.Value = 4;
                CompAttackSlider.Value = 10;
                CompReleaseSlider.Value = 120;
                CompMakeupSlider.Value = 0;
                DistortionEnabledCheck.IsChecked = false;
                DistDriveSlider.Value = 8;
                DistMixSlider.Value = 0.6;
                DistOutputSlider.Value = 0.8;
                ReverbEnabledCheck.IsChecked = false;
                ReverbRoomSlider.Value = 0.5;
                ReverbDampingSlider.Value = 0.5;
                ReverbMixSlider.Value = 0.3;
                PitchShiftEnabledCheck.IsChecked = false;
                PitchShiftSemitonesSlider.Value = 0;
                PitchShiftGrainSizeSlider.Value = 50;
                PitchShiftMixSlider.Value = 1.0;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        /// <summary>
        /// Returns (and lazily creates) the per-clip effect chain, populated from saved settings.
        /// </summary>
        private IEffectChain GetOrLoadEffectChain()
        {
            if (_perClipChain != null) return _perClipChain;

            _perClipChain = EffectChainFactory.CreatePerClip();
            var settings = EffectSettingsManager.Load();
            if (!string.IsNullOrEmpty(_recordingId) &&
                settings.PerClipChains.TryGetValue(_recordingId!, out var cfg))
                EffectSettingsManager.ApplyConfig(_perClipChain, cfg);
            else
                EffectSettingsManager.ApplyConfig(_perClipChain, settings.GlobalChain);
            return _perClipChain;
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPreviewing)
            {
                StopPreview();
                return;
            }

            try
            {
                _reader = AudioReaderFactory.Open(_filePath);

                double startSec = _trimStartFraction * _totalDurationSeconds;
                double endSec = _trimEndFraction * _totalDurationSeconds;

                // Seek to trim start (Opus-safe: only seek when we need to skip ahead)
                if (startSec > 0.001)
                    _reader.CurrentTime = TimeSpan.FromSeconds(startSec);

                ISampleProvider sp = _reader.AsSampleProvider();

                // Apply gain
                _previewGainProvider = new VolumeSampleProvider(sp)
                {
                    Volume = GainDbToFactor(_gainDb)
                };
                sp = _previewGainProvider;

                // Limit to trim region
                sp = new OffsetSampleProvider(sp) { Take = TimeSpan.FromSeconds(endSec - startSec) };

                // Apply per-clip effect chain
                var effectChain = GetOrLoadEffectChain();
                PrepareEffectChain(effectChain, endSec - startSec, sp.WaveFormat.SampleRate);
                sp = new EffectSampleProvider(sp, effectChain);

                // Wrap with metering
                _meterProvider = new MeteringSampleProvider(sp);
                _meterProvider.SamplesPerNotification = Math.Max(1, _meterProvider.WaveFormat.SampleRate / 60);
                _meterProvider.StreamVolume += OnMeterStreamVolume;
                EnsureVertMeterChannels(_meterProvider.WaveFormat.Channels);
                ResetVertMeter();

                _player = AudioOutputDeviceResolver.CreateWasapiPlayer(_outputDeviceIndex, 100);
                _player.PlaybackStopped += Player_PlaybackStopped;
                _player.Init(BuildPlaybackSource(_meterProvider).ToWaveProvider16());
                _player.Play();

                _isPreviewing = true;
                PlayBtn.Content = "⏹  Stop";
                PlaybackLine.Visibility = Visibility.Visible;

                _playbackStartSec = startSec;
                _playbackEndSec = endSec;
                _playbackStartedAt = DateTime.UtcNow;
                StartPlaybackAnimation(startSec, endSec, TimeSpan.FromSeconds(endSec - startSec));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StopPreview();
            }
        }

        private void Player_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => StopPreview(false)));
        }

        private static ISampleProvider BuildPlaybackSource(ISampleProvider source)
        {
            if (source.WaveFormat.Channels == 1)
                return source.ToStereo();

            if (source.WaveFormat.Channels > 2)
            {
                var mux = new MultiplexingSampleProvider(new[] { source }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                return mux;
            }

            return source;
        }

        private void StartPlaybackAnimation(double fromSec, double toSec, TimeSpan duration)
        {
            if (_totalDurationSeconds <= 0 || _waveformWidth <= 0) return;

            double fromX = fromSec / _totalDurationSeconds * _waveformWidth;
            double toX = toSec / _totalDurationSeconds * _waveformWidth;

            var anim = new DoubleAnimation(fromX, toX, new Duration(duration))
            {
                FillBehavior = FillBehavior.HoldEnd
            };
            // No EasingFunction — linear by default
            PlaybackLineTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, anim);
        }

        private void UpdatePlaybackLinePosition(double currentSec)
        {
            if (_totalDurationSeconds <= 0 || _waveformWidth <= 0)
            {
                PlaybackLineTransform.X = 0;
                return;
            }

            double clampedSec = Math.Clamp(currentSec, 0.0, _totalDurationSeconds);
            double fraction = clampedSec / _totalDurationSeconds;
            // Detach any running animation then set value directly
            PlaybackLineTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            PlaybackLineTransform.X = fraction * _waveformWidth;
        }

        private void StopPreview(bool stopPlayer = true)
        {
            if (_isStoppingPreview) return;
            _isStoppingPreview = true;

            try
            {
                if (_meterProvider != null)
                {
                    _meterProvider.StreamVolume -= OnMeterStreamVolume;
                    _meterProvider = null;
                }
                _previewGainProvider = null;

                if (_player != null)
                {
                    _player.PlaybackStopped -= Player_PlaybackStopped;
                    if (stopPlayer && _player.PlaybackState != PlaybackState.Stopped)
                    {
                        _player.Stop();
                    }

                    _player.Dispose();
                    _player = null;
                }

                _reader?.Dispose();
                _reader = null;

                _isPreviewing = false;
                PlaybackLine.Visibility = Visibility.Collapsed;
                // Detach the animation and reset position
                PlaybackLineTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                PlaybackLineTransform.X = 0;

                if (PlayBtn != null)
                    PlayBtn.Content = "▶  Preview";

                ResetVertMeter();
            }
            finally
            {
                _isStoppingPreview = false;
            }
        }

        // ── Vertical meter ─────────────────────────────────────────────────────────

        private void OnMeterStreamVolume(object? sender, NAudio.Wave.SampleProviders.StreamVolumeEventArgs e)
        {
            var snapshot = (float[])e.MaxSampleValues.Clone();
            Dispatcher.BeginInvoke(new Action(() => UpdateVertMeter(snapshot)));
        }

        private void EnsureVertMeterChannels(int channelCount)
        {
            channelCount = Math.Clamp(channelCount, 1, 8);
            if (VertMeterHost == null) return;
            if (_vertMeterOverlays.Count == channelCount) return;

            VertMeterHost.Children.Clear();
            VertMeterHost.ColumnDefinitions.Clear();
            _vertMeterOverlays.Clear();
            _vertPeakLines.Clear();
            _vertPeakFracs.Clear();
            _vertPeakHeldAt.Clear();

            for (int i = 0; i < channelCount; i++)
            {
                VertMeterHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var meterGrid = new Grid
                {
                    Margin = new Thickness(2, 0, 2, 0),
                    ClipToBounds = true
                };

                var fillBar = new WpfRectangle { VerticalAlignment = VerticalAlignment.Stretch };
                fillBar.Fill = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 1),
                    EndPoint = new System.Windows.Point(0, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50), 0.35),
                        new GradientStop(System.Windows.Media.Color.FromRgb(0xFD, 0xD8, 0x35), 0.70),
                        new GradientStop(System.Windows.Media.Color.FromRgb(0xFF, 0x98, 0x00), 0.85),
                        new GradientStop(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36), 0.95),
                        new GradientStop(System.Windows.Media.Color.FromRgb(0xD5, 0x00, 0x00), 1.0)
                    }
                };

                var overlay = new WpfRectangle
                {
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = 10000
                };
                overlay.SetResourceReference(WpfRectangle.FillProperty, "WindowBgBrush");

                var peak = new Border
                {
                    Height = 2,
                    Background = PeakHotBrush,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Visibility = Visibility.Collapsed
                };

                meterGrid.Children.Add(fillBar);
                meterGrid.Children.Add(overlay);
                meterGrid.Children.Add(peak);

                Grid.SetColumn(meterGrid, i);
                VertMeterHost.Children.Add(meterGrid);

                _vertMeterOverlays.Add(overlay);
                _vertPeakLines.Add(peak);
                _vertPeakFracs.Add(0.0);
                _vertPeakHeldAt.Add(DateTime.MinValue);
            }
        }

        private void UpdateVertMeter(float[] channels)
        {
            if (VertMeterHost.ActualHeight <= 0) return;
            if (_vertMeterOverlays.Count == 0)
                EnsureVertMeterChannels(channels.Length);

            int meterCount = Math.Min(channels.Length, _vertMeterOverlays.Count);
            double totalH = VertMeterHost.ActualHeight;
            const double peakHold = 1.5;
            var now = DateTime.UtcNow;

            for (int i = 0; i < meterCount; i++)
            {
                double linear = channels[i];
                double db = linear > 0 ? 20.0 * Math.Log10(linear) : -60.0;
                double frac = DbToMeterFraction(db);

                // Overlay covers the top (unfilled) portion.
                _vertMeterOverlays[i].Height = Math.Max(0, totalH * (1.0 - frac));

                if (frac > _vertPeakFracs[i] || (now - _vertPeakHeldAt[i]).TotalSeconds > peakHold)
                {
                    _vertPeakFracs[i] = frac;
                    _vertPeakHeldAt[i] = now;
                }

                double peakBottom = Math.Clamp(_vertPeakFracs[i] * totalH, 0, Math.Max(0, totalH - 2));
                _vertPeakLines[i].Margin = new Thickness(0, 0, 0, peakBottom);
                _vertPeakLines[i].Visibility = Visibility.Visible;
            }

            for (int i = meterCount; i < _vertPeakLines.Count; i++)
            {
                _vertPeakLines[i].Visibility = Visibility.Collapsed;
            }
        }

        private void ResetVertMeter()
        {
            if (VertMeterHost == null) return;

            double totalH = VertMeterHost.ActualHeight;
            for (int i = 0; i < _vertMeterOverlays.Count; i++)
            {
                _vertPeakFracs[i] = 0;
                _vertPeakHeldAt[i] = DateTime.MinValue;
                if (totalH > 0)
                    _vertMeterOverlays[i].Height = totalH;
                _vertPeakLines[i].Visibility = Visibility.Collapsed;
                _vertPeakLines[i].Margin = new Thickness(0);
            }
        }

        private static double DbToMeterFraction(double db)
        {
            if (db <= -60.0) return 0.0;
            if (db >= 0.0) return 1.0;
            return (db + 60.0) / 60.0;
        }

        private void NonDestructiveCheckBox_Checked(object sender, RoutedEventArgs e)
        {
        }

        private void NonDestructiveCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isChangingNonDestructive) return;

            var res = System.Windows.MessageBox.Show(
                "Disabling non-destructive playback will discard its real-time effects, trim range, and gain settings. Are you sure you want to proceed?",
                "PaDDY",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (res != MessageBoxResult.Yes)
            {
                _isChangingNonDestructive = true;
                NonDestructiveCheckBox.IsChecked = true;
                _isChangingNonDestructive = false;
            }
        }

        // ── Save (destructive trim) ────────────────────────────────────────

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();

            double startSec = _trimStartFraction * _totalDuration.TotalSeconds;
            double endSec = _trimEndFraction * _totalDuration.TotalSeconds;

            bool noTrim = _trimStartFraction <= 0.001 && _trimEndFraction >= 0.999;
            bool noGain = Math.Abs(_gainDb) < 0.01;
            bool noEffects = GetOrLoadEffectChain().Effects.All(e => !e.IsEnabled);

            if (NonDestructiveCheckBox.IsChecked == true)
            {
                SaveEffectSettings();
                OutIsNonDestructive = true;
                OutTrimStartFraction = _trimStartFraction;
                OutTrimEndFraction = _trimEndFraction;
                OutGainDb = _gainDb;
                DialogResult = true;
                return;
            }

            // Destructive Save: clear per-clip effects first
            if (!string.IsNullOrEmpty(_recordingId))
            {
                try
                {
                    var settings = EffectSettingsManager.Load();
                    settings.PerClipChains.Remove(_recordingId!);
                    EffectSettingsManager.Save(settings);
                }
                catch { }
            }

            OutIsNonDestructive = false;

            // Nothing to do — no trim, no gain, no enabled effects
            if (noTrim && noGain && noEffects)
            {
                DialogResult = true;
                return;
            }

            string tempPath = _filePath + ".trim.tmp";
            try
            {
                using (var reader = AudioReaderFactory.Open(_filePath))
                {
                    var format = reader.WaveFormat;

                    // Advance to the trim start point.
                    // For Opus files, OpusOggReadStream.SeekTo does not clear its internal
                    // _nextDataPacket after seeking, so the first decoded frame is stale audio
                    // from before the seek.  SeekTo(0) also corrupts stream state entirely.
                    // For FLAC, FlacReader.Position silently fails to seek when the target sample
                    // falls within the final block (no frame has SampleOffset >= target), leaving
                    // the reader at position 0 and causing the wrong region to be encoded.
                    // Use decode-and-discard for both Opus and FLAC to guarantee exact seek accuracy.
                    string fileExt = Path.GetExtension(_filePath).TrimStart('.').ToLowerInvariant();
                    if (startSec > 0.001)
                    {
                        if (fileExt == "opus" || fileExt == "flac")
                        {
                            if (fileExt == "flac")
                            {
                                reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                            }
                            else
                            {
                                // Opus still needs decode-and-discard because SeekTo can surface stale packets.
                                int blockAlignSkip = format.BlockAlign;
                                long skipBytes = (long)(startSec * format.AverageBytesPerSecond);
                                skipBytes = skipBytes / blockAlignSkip * blockAlignSkip;
                                byte[] skipBuf = new byte[Math.Min(65536, (int)Math.Min(skipBytes, 65536L))];
                                long skipped = 0;
                                while (skipped < skipBytes)
                                {
                                    int toSkip = (int)Math.Min(skipBuf.Length, skipBytes - skipped);
                                    int readSkip = reader.Read(skipBuf, 0, toSkip);
                                    if (readSkip == 0) break;
                                    skipped += readSkip;
                                }
                            }
                        }
                        else
                        {
                            reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                        }
                    }

                    // Duration of the trimmed region in bytes
                    double trimDuration = endSec - startSec;
                    long bytesToWrite = (long)(trimDuration * format.AverageBytesPerSecond);
                    int blockAlign = format.BlockAlign;
                    bytesToWrite = bytesToWrite / blockAlign * blockAlign;
                    if (bytesToWrite <= 0) return;

                    float gainFactor = noGain ? 1f : (float)Math.Pow(10.0, _gainDb / 20.0);

                    var effectChain = GetOrLoadEffectChain();
                    PrepareEffectChain(effectChain, trimDuration,
                        (double)format.SampleRate);

                    using var recorder = StreamingRecorderFactory.CreateForFile(_filePath);
                    recorder.BeginRecording(tempPath, format);

                    byte[] buffer = new byte[format.SampleRate * blockAlign];
                    long written = 0;
                    while (written < bytesToWrite)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        int alignedRead = AlignRecordedByteCount(read, format);
                        if (alignedRead <= 0) continue;
                        if (!noGain)
                            ApplyGainToBuffer(buffer, alignedRead, format, gainFactor);
                        ApplyEffectsToBuffer(buffer, alignedRead, format, effectChain);
                        recorder.AppendSamples(buffer, 0, alignedRead);
                        written += alignedRead;
                    }
                    recorder.Finish();
                }

                // Replace original with trimmed file
                File.Delete(_filePath);
                File.Move(tempPath, _filePath);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                System.Windows.MessageBox.Show($"Trim failed:\n{ex.Message}", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        // ── Save as Copy ─────────────────────────────────────────────────

        private void SaveCopyBtn_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();

            double startSec = _trimStartFraction * _totalDuration.TotalSeconds;
            double endSec = _trimEndFraction * _totalDuration.TotalSeconds;

            bool noTrim = _trimStartFraction <= 0.001 && _trimEndFraction >= 0.999;
            bool noGain = Math.Abs(_gainDb) < 0.01;
            bool noEffects = GetOrLoadEffectChain().Effects.All(e => !e.IsEnabled);

            // Generate a unique copy path
            string dir = Path.GetDirectoryName(_filePath)!;
            string nameNoExt = Path.GetFileNameWithoutExtension(_filePath);
            string ext = Path.GetExtension(_filePath);
            string copyPath = Path.Combine(dir, nameNoExt + "_copy" + ext);
            int counter = 2;
            while (File.Exists(copyPath))
                copyPath = Path.Combine(dir, $"{nameNoExt}_copy{counter++}{ext}");

            if (noTrim && noGain && noEffects)
            {
                try
                {
                    File.Copy(_filePath, copyPath, overwrite: false);
                    CopyFilePath = copyPath;
                    DialogResult = true;
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Copy failed:\n{ex.Message}", "PaDDY",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return;
            }

            string tempPath = copyPath + ".tmp";
            try
            {
                using (var reader = AudioReaderFactory.Open(_filePath))
                {
                    var format = reader.WaveFormat;
                    string fileExt = ext.TrimStart('.').ToLowerInvariant();
                    if (startSec > 0.001)
                    {
                        if (fileExt == "opus" || fileExt == "flac")
                        {
                            if (fileExt == "flac")
                            {
                                reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                            }
                            else
                            {
                                long skipBytes = (long)(startSec * format.AverageBytesPerSecond);
                                skipBytes = skipBytes / format.BlockAlign * format.BlockAlign;
                                byte[] skipBuf = new byte[Math.Min(65536, (int)Math.Min(skipBytes, 65536L))];
                                long skipped = 0;
                                while (skipped < skipBytes)
                                {
                                    int toSkip = (int)Math.Min(skipBuf.Length, skipBytes - skipped);
                                    int readSkip = reader.Read(skipBuf, 0, toSkip);
                                    if (readSkip == 0) break;
                                    skipped += readSkip;
                                }
                            }
                        }
                        else
                        {
                            reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                        }
                    }

                    double trimDuration = endSec - startSec;
                    long bytesToWrite = (long)(trimDuration * format.AverageBytesPerSecond);
                    int blockAlign = format.BlockAlign;
                    bytesToWrite = bytesToWrite / blockAlign * blockAlign;
                    if (bytesToWrite <= 0) return;

                    float gainFactor = noGain ? 1f : (float)Math.Pow(10.0, _gainDb / 20.0);

                    var effectChain = GetOrLoadEffectChain();
                    PrepareEffectChain(effectChain, trimDuration,
                        (double)format.SampleRate);

                    using var recorder = StreamingRecorderFactory.CreateForFile(_filePath);
                    recorder.BeginRecording(tempPath, format);

                    byte[] buffer = new byte[format.SampleRate * blockAlign];
                    long written = 0;
                    while (written < bytesToWrite)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        int alignedRead = AlignRecordedByteCount(read, format);
                        if (alignedRead <= 0) continue;
                        if (!noGain)
                            ApplyGainToBuffer(buffer, alignedRead, format, gainFactor);
                        ApplyEffectsToBuffer(buffer, alignedRead, format, effectChain);
                        recorder.AppendSamples(buffer, 0, alignedRead);
                        written += alignedRead;
                    }
                    recorder.Finish();
                }

                File.Move(tempPath, copyPath);
                CopyFilePath = copyPath;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                System.Windows.MessageBox.Show($"Save as copy failed:\n{ex.Message}", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        // ── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Resets the chain and primes FadeEffect with the clip's total frame count.
        /// </summary>
        private static void PrepareEffectChain(IEffectChain chain, double durationSec, double sampleRate)
        {
            chain.Reset();
            long totalFrames = (long)(durationSec * sampleRate);
            foreach (var effect in chain.Effects)
            {
                if (effect is FadeEffect fade)
                {
                    fade.TotalFrames = totalFrames;
                    break;
                }
            }
        }

        private static int AlignRecordedByteCount(int count, WaveFormat format)
        {
            if (count <= 0) return 0;
            int blockAlign = Math.Max(1, format.BlockAlign);
            return count - (count % blockAlign);
        }

        /// <summary>
        /// Decodes <paramref name="buffer"/> to float samples, runs them through
        /// <paramref name="chain"/>, then re-encodes back in-place.
        /// Supports 16/24/32-bit PCM and 32-bit IEEE float. Other formats are skipped.
        /// </summary>
        private static void ApplyEffectsToBuffer(byte[] buffer, int count, WaveFormat format, IEffectChain chain)
        {
            int bps = format.BitsPerSample / 8;
            if (bps <= 0) return;
            count -= count % bps;
            int sampleCount = count / bps;
            if (sampleCount <= 0) return;

            var floats = new float[sampleCount];

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bps == 4)
            {
                Buffer.BlockCopy(buffer, 0, floats, 0, sampleCount * 4);
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bps == 2)
            {
                for (int i = 0; i < sampleCount; i++)
                    floats[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bps == 3)
            {
                for (int i = 0; i < sampleCount; i++)
                    floats[i] = ReadPcm24(buffer, i * 3) / 8388608f;
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bps == 4)
            {
                for (int i = 0; i < sampleCount; i++)
                    floats[i] = BitConverter.ToInt32(buffer, i * 4) / 2147483648f;
            }
            else return; // unsupported format

            chain.ProcessBuffer(floats, 0, sampleCount, format.Channels, format.SampleRate);

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && bps == 4)
            {
                Buffer.BlockCopy(floats, 0, buffer, 0, sampleCount * 4);
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bps == 2)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    short s = (short)Math.Clamp((int)(floats[i] * 32768f), short.MinValue, short.MaxValue);
                    buffer[i * 2] = (byte)(s & 0xFF);
                    buffer[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bps == 3)
            {
                for (int i = 0; i < sampleCount; i++)
                    WritePcm24(buffer, i * 3, Math.Clamp((int)(floats[i] * 8388608f), -8388608, 8388607));
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && bps == 4)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    int s = (int)Math.Clamp((long)(floats[i] * 2147483648f), int.MinValue, int.MaxValue);
                    buffer[i * 4] = (byte)(s & 0xFF);
                    buffer[i * 4 + 1] = (byte)((s >> 8) & 0xFF);
                    buffer[i * 4 + 2] = (byte)((s >> 16) & 0xFF);
                    buffer[i * 4 + 3] = (byte)((s >> 24) & 0xFF);
                }
            }
        }

        private static string FormatTime(TimeSpan ts)
        {
            return ts.TotalSeconds < 60
                ? $"{ts.TotalSeconds:0.0}s"
                : $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s";
        }

        /// <summary>
        /// Multiplies every PCM sample in <paramref name="buffer"/> by <paramref name="factor"/>,
        /// clamping to avoid overflow. Supports 16/24/32-bit PCM and 32-bit IEEE float formats.
        /// </summary>
        private static void ApplyGainToBuffer(byte[] buffer, int count, WaveFormat format, float factor)
        {
            int bytesPerSample = format.BitsPerSample / 8;
            if (bytesPerSample <= 0)
                return;

            count -= count % bytesPerSample;
            int samples = count / bytesPerSample;

            if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
            {
                for (int i = 0; i < samples; i++)
                {
                    int offset = i * 2;
                    short s = BitConverter.ToInt16(buffer, offset);
                    int scaled = (int)(s * factor);
                    short clamped = (short)Math.Clamp(scaled, short.MinValue, short.MaxValue);
                    buffer[offset] = (byte)(clamped & 0xFF);
                    buffer[offset + 1] = (byte)((clamped >> 8) & 0xFF);
                }
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
            {
                for (int i = 0; i < samples; i++)
                {
                    int offset = i * 3;
                    int sample = ReadPcm24(buffer, offset);
                    int scaled = (int)Math.Round(sample * factor);
                    WritePcm24(buffer, offset, Math.Clamp(scaled, -8388608, 8388607));
                }
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 32)
            {
                for (int i = 0; i < samples; i++)
                {
                    int offset = i * 4;
                    int sample = BitConverter.ToInt32(buffer, offset);
                    long scaled = (long)Math.Round(sample * factor);
                    int clamped = (int)Math.Clamp(scaled, int.MinValue, int.MaxValue);
                    byte[] bytes = BitConverter.GetBytes(clamped);
                    buffer[offset] = bytes[0];
                    buffer[offset + 1] = bytes[1];
                    buffer[offset + 2] = bytes[2];
                    buffer[offset + 3] = bytes[3];
                }
            }
            else if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                for (int i = 0; i < samples; i++)
                {
                    int offset = i * 4;
                    float f = BitConverter.ToSingle(buffer, offset);
                    f = Math.Clamp(f * factor, -1f, 1f);
                    byte[] fb = BitConverter.GetBytes(f);
                    buffer[offset] = fb[0];
                    buffer[offset + 1] = fb[1];
                    buffer[offset + 2] = fb[2];
                    buffer[offset + 3] = fb[3];
                }
            }
        }

        private static int ReadPcm24(byte[] buffer, int offset)
        {
            int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
            return (sample & 0x800000) != 0 ? sample | unchecked((int)0xFF000000) : sample;
        }

        private static void WritePcm24(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        }

        // ── Byte-limiting wrapper ───────────────────────────────────────────

        /// <summary>
        /// Wraps an <see cref="IUnifiedAudioReader"/> so that WaveOutEvent only
        /// receives PCM bytes for the [startSec, endSec) range.  When the byte
        /// budget is exhausted the wrapper returns 0, letting WaveOutEvent drain
        /// its internal buffers naturally before firing PlaybackStopped.
        /// </summary>
        private sealed class TrimmedWaveProvider : IWaveProvider
        {
            private readonly IUnifiedAudioReader _reader;
            private long _bytesRemaining;

            public WaveFormat WaveFormat => _reader.WaveFormat;

            public TrimmedWaveProvider(IUnifiedAudioReader reader, double startSec, double endSec)
            {
                _reader = reader;

                // Only seek when we genuinely need to skip ahead.
                // Seeking to zero on a freshly-opened Opus stream triggers
                // SeekToGranulePosition(0) which resets decoder state and
                // corrupts the internal packet queue, causing early EOF.
                if (startSec > 0.001)
                    _reader.CurrentTime = TimeSpan.FromSeconds(startSec);

                double duration = endSec - startSec;
                long totalBytes = (long)(duration * _reader.WaveFormat.AverageBytesPerSecond);
                int blockAlign = _reader.WaveFormat.BlockAlign;
                _bytesRemaining = totalBytes / blockAlign * blockAlign;
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                if (_bytesRemaining <= 0) return 0;

                int toRead = (int)Math.Min(count, _bytesRemaining);
                int read = _reader.Read(buffer, offset, toRead);
                _bytesRemaining -= read;
                return read;
            }
        }
        private void VstChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            bool expand = VstContent.Visibility == Visibility.Collapsed;
            VstContent.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            VstChevron.Text = expand ? "\u25BC" : "\u25BA";
        }

        private void ShowVstEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_vstEffect != null)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                _vstEffect.OpenEditor(hwnd);
            }
        }
    }
}
