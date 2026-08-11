using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
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
        private System.Windows.Threading.DispatcherTimer? _timecodeTimer;
        private System.Windows.Threading.DispatcherTimer? _waveformResizeTimer;
        private bool _isPreviewing;
        private bool _isStoppingPreview;
        private bool _isClipping;

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
        private RemasterEffect? _remaster;
        private readonly List<IVstEffect> _vstEffects = new();

        private const double MinTrimSeconds = 0.05; // 50 ms minimum

        // ── Fullscreen state ───────────────────────────────────────────────────
        private bool _isFullscreen;
        private WindowState _preFullscreenWindowState;
        private WindowStyle _preFullscreenWindowStyle;
        private ResizeMode _preFullscreenResizeMode;
        private Rect _preFullscreenBounds;
        private double _preFullscreenChromeHeight;
        private bool _preFullscreenTopmost;

        // Stored waveform peaks for gain-responsive re-render
        private (float min, float max)[]? _originalPeaks;
        private List<(float min, float max)>? _rawBlockPeaks;

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

        private static readonly System.Reflection.FieldInfo? IsModalField = typeof(Window).GetField("_isModal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private bool? _dialogResult;
        public new bool? DialogResult
        {
            get => _dialogResult ?? (IsModalField?.GetValue(this) is true ? base.DialogResult : null);
            set
            {
                _dialogResult = value;
                if (IsModalField?.GetValue(this) is true)
                {
                    base.DialogResult = value;
                }
                else if (value != null)
                {
                    Close();
                }
            }
        }

        private readonly bool _initialIsNonDestructive;
        private readonly long _initialTrimStartMs;
        private readonly long _initialTrimEndMs;
        private readonly double _initialGainDb;
        private bool _isChangingNonDestructive;

        public AudioEditorWindow(string filePath, string? recordingId = null, int outputDeviceIndex = -1, string? displayName = null,
            bool isNonDestructive = false, long trimStartMs = 0, long trimEndMs = 0, double gainDb = 0.0)
        {
            InitializeComponent();

            VstSection.Visibility = Visibility.Visible;
            App.DebugModeChanged += OnDebugModeChanged;

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
            StateChanged += OnStateChanged;
            WaveformGrid.SizeChanged += WaveformGrid_SizeChanged;
            ThemeManager.ThemeChanged += OnThemeChanged;
            Closed += (_, _) =>
            {
                _waveformResizeTimer?.Stop();
                StopPreview();
                App.DebugModeChanged -= OnDebugModeChanged;
                ThemeManager.ThemeChanged -= OnThemeChanged;
            };
        }

        private void OnThemeChanged()
        {
            if (_originalPeaks != null)
                RenderWaveformFromPeaks();
        }

        private void OnDebugModeChanged()
        {
            // VST section is always visible — shipped VST2 plugins load regardless of debug mode.
            // VST3 availability is controlled separately via VstPluginManager.IsVst3Enabled.
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            AudioEditorLoadingOverlay.Show("Loading audio...");

            string filePath = _filePath;
            double totalDurationSeconds = 0;
            TimeSpan totalDuration = TimeSpan.Zero;
            int sampleRate = 44100;
            int channels = 2;
            long fileSize = 0;
            string format = System.IO.Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();

            try
            {
                var fi = new System.IO.FileInfo(filePath);
                if (fi.Exists) fileSize = fi.Length;
            }
            catch { }

            int width = (int)WaveformGrid.ActualWidth;
            int height = (int)WaveformGrid.ActualHeight;
            if (width < 10 || height < 10) { width = 680; height = 180; }

            bool success = false;
            string? errorMessage = null;

            try
            {
                // Parallelize audio decoding and VST/effect loading on background threads
                var audioLoadTask = System.Threading.Tasks.Task.Run(() =>
                {
                    using var reader = AudioReaderFactory.Open(filePath);
                    totalDuration = reader.TotalTime;
                    totalDurationSeconds = Math.Max(totalDuration.TotalSeconds, 0.001);

                    var sampleProvider = reader.AsSampleProvider();
                    var waveFormat = reader.WaveFormat;
                    channels = waveFormat.Channels;
                    sampleRate = waveFormat.SampleRate;

                    // Pass 1: Decode all samples and accumulate min/max into a flat list.
                    // We don't know the exact sample count up-front (TotalTime estimate from
                    // different decoders can differ), so we use a dynamic list keyed by
                    // time-fraction bucket and resize at the end.
                    float[] buffer = new float[waveFormat.SampleRate * channels]; // 1-sec chunks
                    var dynamicPeaks = new List<(float min, float max)>();
                    long samplesRead = 0;
                    int read;

                    // First decode pass — accumulate peaks into a per-sample list
                    // (temporarily one entry per mono sample; we merge into width buckets below)
                    // To avoid huge allocations we track min/max per 64-sample block
                    const int blockSize = 64;
                    float blockMin = 0f, blockMax = 0f;
                    int blockCount = 0;

                    if (channels == 1)
                    {
                        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            for (int i = 0; i < read; i++)
                            {
                                float sample = buffer[i];

                                if (sample < blockMin) blockMin = sample;
                                if (sample > blockMax) blockMax = sample;
                                blockCount++;

                                if (blockCount >= blockSize)
                                {
                                    dynamicPeaks.Add((blockMin, blockMax));
                                    blockMin = 0f; blockMax = 0f; blockCount = 0;
                                }
                            }
                            samplesRead += read;
                        }
                    }
                    else if (channels == 2)
                    {
                        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            int monoRead = read / 2;
                            for (int i = 0; i < monoRead; i++)
                            {
                                float sample = (buffer[i * 2] + buffer[i * 2 + 1]) * 0.5f;

                                if (sample < blockMin) blockMin = sample;
                                if (sample > blockMax) blockMax = sample;
                                blockCount++;

                                if (blockCount >= blockSize)
                                {
                                    dynamicPeaks.Add((blockMin, blockMax));
                                    blockMin = 0f; blockMax = 0f; blockCount = 0;
                                }
                            }
                            samplesRead += monoRead;
                        }
                    }
                    else
                    {
                        while ((read = sampleProvider.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            int monoRead = read / channels;
                            for (int i = 0; i < monoRead; i++)
                            {
                                float sample = 0f;
                                for (int ch = 0; ch < channels; ch++)
                                    sample += buffer[i * channels + ch];
                                sample /= channels;

                                if (sample < blockMin) blockMin = sample;
                                if (sample > blockMax) blockMax = sample;
                                blockCount++;

                                if (blockCount >= blockSize)
                                {
                                    dynamicPeaks.Add((blockMin, blockMax));
                                    blockMin = 0f; blockMax = 0f; blockCount = 0;
                                }
                            }
                            samplesRead += monoRead;
                        }
                    }

                    if (blockCount > 0)
                        dynamicPeaks.Add((blockMin, blockMax));

                    _rawBlockPeaks = dynamicPeaks;
                });

                IEffectChain localPerClipChain = null;
                var localVstEffects = new List<IVstEffect>();
                string recordingId = _recordingId;

                var vstLoadTask = System.Threading.Tasks.Task.Run(() =>
                {
                    // 1. Create and apply effect config from saved settings
                    localPerClipChain = EffectChainFactory.CreatePerClip();
                    var effectSettings = EffectSettingsManager.Load();
                    if (!string.IsNullOrEmpty(recordingId) &&
                        effectSettings.PerClipChains.TryGetValue(recordingId!, out var cfg))
                        EffectSettingsManager.ApplyConfig(localPerClipChain, cfg);
                    else
                        EffectSettingsManager.ApplyConfig(localPerClipChain, effectSettings.GlobalChain);

                    // 2. Load all default vendored VST plugins (VST2 + VST3)
                    var defaultPlugins = VstPluginManager.LoadDefaultPlugins();
                    foreach (var plugin in defaultPlugins)
                    {
                        localVstEffects.Add(plugin);
                        localPerClipChain.Add(plugin);
                    }

                    // 3. Load legacy and managed user plugins (if different from defaults).
                    var appSettings = AppSettings.Load();
                    var userPluginPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (!string.IsNullOrWhiteSpace(appSettings.VstPluginPath))
                        userPluginPaths.Add(appSettings.VstPluginPath);
                    if (!string.IsNullOrWhiteSpace(appSettings.Vst3PluginPath))
                        userPluginPaths.Add(appSettings.Vst3PluginPath);
                    foreach (string path in appSettings.UserVstPluginPaths ?? new List<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(path))
                            userPluginPaths.Add(path);
                    }

                    foreach (string path in userPluginPaths)
                    {
                        var plugin = VstPluginManager.TryLoadUserPlugin(path);
                        if (plugin != null)
                        {
                            bool alreadyLoaded = false;
                            foreach (var vst in localVstEffects)
                            {
                                if (string.Equals(vst.Name, plugin.Name, StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyLoaded = true;
                                    break;
                                }
                            }

                            if (!alreadyLoaded)
                            {
                                localVstEffects.Add(plugin);
                                localPerClipChain.Add(plugin);
                            }
                            else
                            {
                                (plugin as IDisposable)?.Dispose();
                            }
                        }
                    }
                });

                await System.Threading.Tasks.Task.WhenAll(audioLoadTask, vstLoadTask);

                _perClipChain = localPerClipChain;
                _vstEffects.AddRange(localVstEffects);

                success = true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            if (!success)
            {
                System.Windows.MessageBox.Show($"Could not read audio file:\n{errorMessage}", "PaDDY",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _totalDuration = totalDuration;
            _totalDurationSeconds = totalDurationSeconds;
            TotalDurationLabel.Text = FormatTime(_totalDuration);

            // Populate File Info metadata details
            FileFormatLabel.Text = string.IsNullOrEmpty(format) ? "Unknown" : format;
            FileSampleRateLabel.Text = $"{sampleRate / 1000.0:0.0} kHz ({sampleRate} Hz)";
            FileChannelsLabel.Text = channels == 1 ? "1 (Mono)" : (channels == 2 ? "2 (Stereo)" : $"{channels} Channels");
            FileSizeLabel.Text = FormatFileSize(fileSize);

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

            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            UpdatePeaksForWidth((int)_waveformWidth);
            RenderWaveformFromPeaks();
            UpdateHandlePositions();
            UpdateTimeLabels();
            UpdatePlaybackTimecode(0.0);
            EnsureVertMeterChannels(2);
            ResetVertMeter();

            NonDestructiveCheckBox.Checked -= NonDestructiveCheckBox_Checked;
            NonDestructiveCheckBox.Unchecked -= NonDestructiveCheckBox_Unchecked;
            NonDestructiveCheckBox.IsChecked = _initialIsNonDestructive;
            NonDestructiveCheckBox.Checked += NonDestructiveCheckBox_Checked;
            NonDestructiveCheckBox.Unchecked += NonDestructiveCheckBox_Unchecked;

            GainSlider.Value = _gainDb;

            // Setup effect controls from local per clip chain
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
                    case RemasterEffect rm: _remaster = rm; break;
                }
            }
            LoadEffectValues();
            PopulateVstPluginRack();

            AudioEditorLoadingOverlay.Hide(instantly: true);
        }

        private void PopulateVstPluginRack()
        {
            VstPluginRackPanel.Children.Clear();
            VstNameLabel.Text = _vstEffects.Count == 0
                ? "No VST Plugin Loaded"
                : $"{_vstEffects.Count} plugin{(_vstEffects.Count == 1 ? string.Empty : "s")} available";
            ShowVstEditorButton.IsEnabled = _vstEffects.Count > 0;

            foreach (var plugin in _vstEffects)
            {
                var toggle = new System.Windows.Controls.CheckBox
                {
                    Content = plugin.Name,
                    IsChecked = plugin.IsEnabled,
                    Margin = new Thickness(0, 3, 0, 3),
                    FontSize = 11,
                    Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush")
                };
                toggle.Checked += (_, _) => plugin.IsEnabled = true;
                toggle.Unchecked += (_, _) => plugin.IsEnabled = false;
                VstPluginRackPanel.Children.Add(toggle);
            }
        }

        private void WaveformGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) < 1.0 && Math.Abs(e.NewSize.Height - e.PreviousSize.Height) < 1.0)
                return;

            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            ScheduleWaveformResizeRender();
        }

        private void ScheduleWaveformResizeRender()
        {
            _waveformResizeTimer ??= new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(75),
                System.Windows.Threading.DispatcherPriority.Background,
                (_, _) => RenderWaveformAfterResize(),
                Dispatcher);

            _waveformResizeTimer.Stop();
            _waveformResizeTimer.Start();
        }

        private void RenderWaveformAfterResize()
        {
            _waveformResizeTimer?.Stop();
            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            int width = (int)_waveformWidth;
            int height = (int)WaveformGrid.ActualHeight;
            if (width <= 0 || height <= 0) return;

            if (_originalPeaks?.Length != width)
                UpdatePeaksForWidth(width);

            if (_lastRenderedWaveformWidth != width || _lastRenderedWaveformHeight != height)
                RenderWaveformFromPeaks();

            UpdateHandlePositions();
            UpdateTimeLabels();

            if (_isPreviewing)
            {
                double elapsed = (DateTime.UtcNow - _playbackStartedAt).TotalSeconds;
                double currentSec = Math.Clamp(_playbackStartSec + elapsed, _playbackStartSec, _playbackEndSec);
                UpdatePlaybackLinePosition(currentSec);
            }
        }

        private void UpdatePeaksForWidth(int width)
        {
            if (_rawBlockPeaks == null || width <= 0) return;
            var peaks = new (float min, float max)[width];
            for (int i = 0; i < width; i++) peaks[i] = (0f, 0f);

            int dynCount = _rawBlockPeaks.Count;
            if (dynCount > 0)
            {
                for (int bi = 0; bi < dynCount; bi++)
                {
                    int bucket = (int)((long)bi * width / dynCount);
                    if (bucket >= width) bucket = width - 1;
                    var (dMin, dMax) = _rawBlockPeaks[bi];
                    if (dMin < peaks[bucket].min) peaks[bucket] = (dMin, peaks[bucket].max);
                    if (dMax > peaks[bucket].max) peaks[bucket] = (peaks[bucket].min, dMax);
                }
            }
            _originalPeaks = peaks;
        }

        private void ZoomSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (ZoomLabel != null)
            {
                ZoomLabel.Text = $"{e.NewValue:0.0}x";
            }
            UpdateWaveformZoom();
            UpdateTimeLabels();
        }

        private void ResetZoom_Click(object sender, RoutedEventArgs e)
        {
            ZoomSlider.Value = 1.0;
        }

        private void WaveformScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateWaveformZoom();
            UpdateTimeLabels();
        }

        private void WaveformScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.HorizontalChange != 0 || e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0)
            {
                UpdateTimeLabels();
            }
        }

        private void TimeMarkersGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateTimeLabels();
        }

        private void UpdateWaveformZoom()
        {
            if (WaveformScrollViewer == null || WaveformGrid == null || ZoomSlider == null) return;
            double zoom = ZoomSlider.Value;
            if (zoom <= 1.0)
            {
                WaveformGrid.Width = double.NaN; // Auto-size to viewport
                WaveformScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            else
            {
                double viewportWidth = WaveformScrollViewer.ActualWidth;
                if (viewportWidth > 0)
                {
                    WaveformGrid.Width = viewportWidth * zoom;
                    WaveformScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                }
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

        private WriteableBitmap? _waveformBmp;
        private byte[]? _waveformPixels;
        private int _lastRenderedWaveformWidth;
        private int _lastRenderedWaveformHeight;

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

            if (_waveformBmp == null || (int)_waveformBmp.Width != width || (int)_waveformBmp.Height != height)
            {
                _waveformBmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                WaveformImage.Source = _waveformBmp;
            }

            int stride = width * 4;
            int byteCount = stride * height;
            if (_waveformPixels == null || _waveformPixels.Length < byteCount)
                _waveformPixels = new byte[byteCount];
            byte[] pixels = _waveformPixels;

            Array.Clear(pixels, 0, byteCount);
            int midY = height / 2;

            // Draw centre line using a dimmed version of the accent color
            byte centreR = (byte)(aR * 0.22f);
            byte centreG = (byte)(aG * 0.22f);
            byte centreB = (byte)(aB * 0.22f);
            for (int x = 0; x < width; x++)
                SetPixel(pixels, stride, x, midY, centreR, centreG, centreB, 0xFF);

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

            _waveformBmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            _lastRenderedWaveformWidth = width;
            _lastRenderedWaveformHeight = height;
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
            if (StartTimeLabel == null || EndTimeLabel == null || TrimmedDurationLabel == null) return;

            double totalSec = _totalDuration.TotalSeconds;
            double startSec = _trimStartFraction * totalSec;
            double endSec = _trimEndFraction * totalSec;
            double trimmed = endSec - startSec;

            // Timeline ruler reflects the track timeline (0.0s to totalSec), not trim handles
            double rangeStartSec = 0.0;
            double rangeEndSec = totalSec;

            // If zoomed in, reflect the visible waveform region in the ruler start/end
            if (ZoomSlider != null && ZoomSlider.Value > 1.0 && WaveformScrollViewer != null && WaveformGrid != null && WaveformGrid.ActualWidth > 0)
            {
                double viewportWidth = WaveformScrollViewer.ActualWidth;
                double totalWidth = WaveformGrid.ActualWidth;
                if (viewportWidth > 0 && totalWidth > 0)
                {
                    double scrollOffset = WaveformScrollViewer.HorizontalOffset;
                    rangeStartSec = Math.Clamp((scrollOffset / totalWidth) * totalSec, 0, totalSec);
                    rangeEndSec = Math.Clamp(((scrollOffset + viewportWidth) / totalWidth) * totalSec, 0, totalSec);
                }
            }

            StartTimeLabel.Text = FormatTime(TimeSpan.FromSeconds(rangeStartSec));
            EndTimeLabel.Text = FormatTime(TimeSpan.FromSeconds(rangeEndSec));
            TrimmedDurationLabel.Text = string.Format(LocalizationManager.Instance["TrimmedLabel"], FormatTime(TimeSpan.FromSeconds(trimmed)));

            SaveBtn.IsEnabled = trimmed >= MinTrimSeconds;

            // Dynamically update intermediate timecode markers (TimeMarker1..4)
            if (TimeMarkersGrid != null && TimeMarker1 != null && TimeMarker2 != null && TimeMarker3 != null && TimeMarker4 != null)
            {
                double gridWidth = TimeMarkersGrid.ActualWidth;
                if (gridWidth < 50) return;

                if (gridWidth < 380)
                {
                    TimeMarker1.Visibility = Visibility.Collapsed;
                    TimeMarker4.Visibility = Visibility.Collapsed;
                }
                else
                {
                    TimeMarker1.Visibility = Visibility.Visible;
                    TimeMarker4.Visibility = Visibility.Visible;
                }

                double span = rangeEndSec - rangeStartSec;
                double frac1 = 0.167;
                double frac2 = 0.333;
                double frac3 = 0.667;
                double frac4 = 0.833;

                TimeMarker1.Text = FormatTime(TimeSpan.FromSeconds(rangeStartSec + span * frac1));
                TimeMarker2.Text = FormatTime(TimeSpan.FromSeconds(rangeStartSec + span * frac2));
                TimeMarker3.Text = FormatTime(TimeSpan.FromSeconds(rangeStartSec + span * frac3));
                TimeMarker4.Text = FormatTime(TimeSpan.FromSeconds(rangeStartSec + span * frac4));

                double pos1 = Math.Max(0, gridWidth * frac1 - 15);
                double pos2 = Math.Max(0, gridWidth * frac2 - 15);
                double pos3 = Math.Max(0, gridWidth * frac3 - 15);
                double pos4 = Math.Max(0, gridWidth * frac4 - 15);

                TimeMarker1.Margin = new Thickness(pos1, 0, 0, 0);
                TimeMarker2.Margin = new Thickness(pos2, 0, 0, 0);
                TimeMarker3.Margin = new Thickness(pos3, 0, 0, 0);
                TimeMarker4.Margin = new Thickness(pos4, 0, 0, 0);
            }
        }

        private void UpdatePlaybackTimecode(double currentSec)
        {
            if (PlaybackTimecodeLabel != null)
            {
                PlaybackTimecodeLabel.Text = $"{FormatDetailedTimecode(TimeSpan.FromSeconds(currentSec))} / {FormatDetailedTimecode(_totalDuration)}";
            }
        }

        private static string FormatDetailedTimecode(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
            {
                return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
            }
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
        }

        private void StartTimecodeTimer()
        {
            StopTimecodeTimer();
            if (Helpers.ThemeManager.PerformanceMode)
            {
                _timecodeTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _timecodeTimer.Tick += TimecodeTimer_Tick;
                _timecodeTimer.Start();
            }
            else
            {
                System.Windows.Media.CompositionTarget.Rendering += OnCompositionRendering;
            }
        }

        private void StopTimecodeTimer()
        {
            if (_timecodeTimer != null)
            {
                _timecodeTimer.Stop();
                _timecodeTimer.Tick -= TimecodeTimer_Tick;
                _timecodeTimer = null;
            }
            System.Windows.Media.CompositionTarget.Rendering -= OnCompositionRendering;
        }

        private void OnCompositionRendering(object? sender, EventArgs e)
        {
            UpdatePlaybackTimecodeTick();
        }

        private void TimecodeTimer_Tick(object? sender, EventArgs e)
        {
            UpdatePlaybackTimecodeTick();
        }

        private void UpdatePlaybackTimecodeTick()
        {
            if (!_isPreviewing)
            {
                StopTimecodeTimer();
                return;
            }
            double elapsed = (DateTime.UtcNow - _playbackStartedAt).TotalSeconds;
            double currentSec = Math.Clamp(_playbackStartSec + elapsed, _playbackStartSec, _playbackEndSec);
            UpdatePlaybackTimecode(currentSec);
            UpdatePlaybackLinePosition(currentSec);
        }



        // ── Custom Window Chrome ───────────────────────────────────────────────
        private void ChromeMinimize_Click(object sender, RoutedEventArgs e)
            => SystemCommands.MinimizeWindow(this);

        private void ChromeMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen)
            {
                ExitFullscreen();
                return;
            }

            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (!_isFullscreen)
            {
                // CanResize (not NoResize) when maximized: it hides the resize grip while
                // keeping WS_THICKFRAME, so Windows maximizes to the work area instead of
                // treating the window as fullscreen (which would cover the taskbar).
                ResizeMode desiredResizeMode = WindowState == WindowState.Maximized
                    ? ResizeMode.CanResize
                    : ResizeMode.CanResizeWithGrip;
                if (ResizeMode != desiredResizeMode)
                    ResizeMode = desiredResizeMode;
            }

            if (ChromeMaxIcon != null && ChromeMaxRestoreBtn != null)
            {
                if (_isFullscreen || WindowState == WindowState.Maximized)
                {
                    ChromeMaxIcon.Text = "\uE923";
                    ChromeMaxRestoreBtn.ToolTip = "Restore";
                }
                else
                {
                    ChromeMaxIcon.Text = "\uE922";
                    ChromeMaxRestoreBtn.ToolTip = "Maximize";
                }
            }
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Fullscreen (F11) ──────────────────────────────────────────────────
        private void ChromeFullscreen_Click(object sender, RoutedEventArgs e)
            => ToggleFullscreen();

        private void ToggleFullscreen()
        {
            if (_isFullscreen)
                ExitFullscreen();
            else
                EnterFullscreen();
        }

        private void EnterFullscreen()
        {
            if (_isFullscreen) return;

            // Save current state for restoration
            _preFullscreenWindowState = WindowState;
            _preFullscreenWindowStyle = WindowStyle;
            _preFullscreenResizeMode = ResizeMode;
            _preFullscreenBounds = new Rect(Left, Top, Width, Height);
            _preFullscreenTopmost = Topmost;

            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            _preFullscreenChromeHeight = chrome?.CaptionHeight ?? 36;

            // Must restore first if maximized, then set style, then maximize again.
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;

            // Remove chrome caption so the title bar area becomes content space
            if (chrome != null)
                chrome.CaptionHeight = 0;

            WindowState = WindowState.Normal;
            if (!TryApplyFullscreenBounds())
                WindowState = WindowState.Maximized;
            Topmost = true;
            _isFullscreen = true;

            // Update maximize button icon to reflect state
            ChromeMaxIcon.Text = "\uE923"; // Restore icon
            ChromeMaxRestoreBtn.ToolTip = "Restore";

            // Update fullscreen button
            ChromeFullscreenIcon.Text = "\uE73F"; // Exit fullscreen icon
            ChromeFullscreenBtn.ToolTip = "Exit Fullscreen (F11)";

            ScheduleWaveformResizeRender();
        }

        private void ExitFullscreen()
        {
            if (!_isFullscreen) return;

            _isFullscreen = false;

            // Restore window chrome
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(this);
            if (chrome != null)
                chrome.CaptionHeight = _preFullscreenChromeHeight;

            // Restore window style
            WindowState = WindowState.Normal;
            WindowStyle = _preFullscreenWindowStyle;
            ResizeMode = _preFullscreenResizeMode;
            Topmost = _preFullscreenTopmost;

            // Restore position and size
            Left = _preFullscreenBounds.Left;
            Top = _preFullscreenBounds.Top;
            Width = _preFullscreenBounds.Width;
            Height = _preFullscreenBounds.Height;

            // Restore previous window state
            WindowState = _preFullscreenWindowState;

            // Update maximize button icon
            if (WindowState == WindowState.Maximized)
            {
                ChromeMaxIcon.Text = "\uE923"; // Restore icon
                ChromeMaxRestoreBtn.ToolTip = "Restore";
            }
            else
            {
                ChromeMaxIcon.Text = "\uE922"; // Maximize icon
                ChromeMaxRestoreBtn.ToolTip = "Maximize";
            }

            // Update fullscreen button
            ChromeFullscreenIcon.Text = "\uE740"; // Enter fullscreen icon
            ChromeFullscreenBtn.ToolTip = "Fullscreen (F11)";

            ScheduleWaveformResizeRender();
        }

        private bool TryApplyFullscreenBounds()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null)
                return false;

            var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
            var bounds = screen.Bounds;

            // Screen.Bounds is in physical pixels; Window Left/Top/Width/Height are DIPs.
            // Without this conversion the window overshoots the screen on scaled displays.
            var toDip = source.CompositionTarget.TransformFromDevice;
            var topLeft = toDip.Transform(new System.Windows.Point(bounds.Left, bounds.Top));
            var bottomRight = toDip.Transform(new System.Windows.Point(bounds.Right, bounds.Bottom));

            Left = topLeft.X;
            Top = topLeft.Y;
            Width = bottomRight.X - topLeft.X;
            Height = bottomRight.Y - topLeft.Y;
            return true;
        }

        // ── Maximize bounds (keep taskbar visible with custom chrome) ─────────
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = (HwndSource?)PresentationSource.FromVisual(this);
            source?.AddHook(WindowProc);
        }

        private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_GETMINMAXINFO = 0x0024;
            if (msg == WM_GETMINMAXINFO)
                ClampMaximizedBoundsToWorkArea(hwnd, lParam);
            return IntPtr.Zero;
        }

        // With WindowChrome, WPF maximizes the window larger than the monitor (overhang
        // by the resize border) and content bleeds offscreen/under the taskbar. Clamp
        // the maximized size/position to the current monitor's work area instead.
        private static void ClampMaximizedBoundsToWorkArea(IntPtr hwnd, IntPtr lParam)
        {
            const int MONITOR_DEFAULTTONEAREST = 0x0002;
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref mi))
                return;

            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
            mmi.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
            mmi.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
            mmi.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int dwFlags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public NativePoint ptReserved;
            public NativePoint ptMaxSize;
            public NativePoint ptMaxPosition;
            public NativePoint ptMinTrackSize;
            public NativePoint ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public int dwFlags;
        }

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            // F11: toggle fullscreen
            if (e.Key == System.Windows.Input.Key.F11)
            {
                e.Handled = true;
                ToggleFullscreen();
                return;
            }

            // Escape: exit fullscreen
            if (e.Key == System.Windows.Input.Key.Escape && _isFullscreen)
            {
                e.Handled = true;
                ExitFullscreen();
                return;
            }

            base.OnKeyDown(e);
        }

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
                if (_remaster != null)
                {
                    RemasterEnabledCheck.IsChecked = _remaster.IsEnabled;
                    RemasterPresetCombo.SelectedIndex = _remaster.Preset switch
                    {
                        RemasterPreset.CleanAndTransparent => 0,
                        RemasterPreset.WarmAnalog => 1,
                        RemasterPreset.PunchyClub => 2,
                        RemasterPreset.VocalAcoustic => 3,
                        RemasterPreset.LoudMaximizer => 4,
                        _ => 5
                    };
                    RemasterWarmthSlider.Value = _remaster.WarmthDb;
                    RemasterPunchSlider.Value = _remaster.PunchDb;
                    RemasterBrillianceSlider.Value = _remaster.BrillianceDb;
                    RemasterWidthSlider.Value = _remaster.StereoWidth;
                    RemasterDriveSlider.Value = _remaster.Drive;
                    RemasterRatioSlider.Value = _remaster.Ratio;
                    RemasterCeilingSlider.Value = _remaster.LimiterCeilingDb;
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
            if (RemasterWarmthSlider != null)
                RemasterWarmthLabel.Text = $"{(RemasterWarmthSlider.Value >= 0 ? "+" : "")}{RemasterWarmthSlider.Value:F1} dB";
            if (RemasterPunchSlider != null)
                RemasterPunchLabel.Text = $"{(RemasterPunchSlider.Value >= 0 ? "+" : "")}{RemasterPunchSlider.Value:F1} dB";
            if (RemasterBrillianceSlider != null)
                RemasterBrillianceLabel.Text = $"{(RemasterBrillianceSlider.Value >= 0 ? "+" : "")}{RemasterBrillianceSlider.Value:F1} dB";
            if (RemasterWidthSlider != null)
                RemasterWidthLabel.Text = $"{RemasterWidthSlider.Value:F2}x";
            if (RemasterDriveSlider != null)
                RemasterDriveLabel.Text = $"{RemasterDriveSlider.Value:F2}";
            if (RemasterRatioSlider != null)
                RemasterRatioLabel.Text = $"{RemasterRatioSlider.Value:F1}:1";
            if (RemasterCeilingSlider != null)
                RemasterCeilingLabel.Text = $"{RemasterCeilingSlider.Value:F1} dB";

            UpdateEffectVisualGraphs();
        }

        private void UpdateEffectVisualGraphs()
        {
            UpdateFadeVisualGraph();
            UpdateGateVisualGraph();
            UpdateCompVisualGraph();
            UpdateEqVisualGraph();
            UpdatePitchShiftVisualGraph();
            UpdateDistVisualGraph();
            UpdateEchoVisualGraph();
            UpdateReverbVisualGraph();
        }

        private void UpdateFadeVisualGraph() { }
        private void UpdateGateVisualGraph() { }
        private void UpdateCompVisualGraph() { }
        private void UpdateEqVisualGraph() { }
        private void UpdatePitchShiftVisualGraph() { }
        private void UpdateDistVisualGraph() { }
        private void UpdateEchoVisualGraph() { }
        private void UpdateReverbVisualGraph() { }



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
            if (_remaster != null)
            {
                _remaster.IsEnabled = RemasterEnabledCheck.IsChecked == true;
                _remaster.Preset = RemasterPresetCombo.SelectedIndex switch
                {
                    0 => RemasterPreset.CleanAndTransparent,
                    1 => RemasterPreset.WarmAnalog,
                    2 => RemasterPreset.PunchyClub,
                    3 => RemasterPreset.VocalAcoustic,
                    4 => RemasterPreset.LoudMaximizer,
                    _ => RemasterPreset.Custom
                };
                _remaster.WarmthDb = RemasterWarmthSlider.Value;
                _remaster.PunchDb = RemasterPunchSlider.Value;
                _remaster.BrillianceDb = RemasterBrillianceSlider.Value;
                _remaster.StereoWidth = RemasterWidthSlider.Value;
                _remaster.Drive = RemasterDriveSlider.Value;
                _remaster.Ratio = RemasterRatioSlider.Value;
                _remaster.LimiterCeilingDb = RemasterCeilingSlider.Value;
            }
            SaveEffectSettings();
        }

        private void RemasterPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_effectsLoading) return;
            var preset = RemasterPresetCombo.SelectedIndex switch
            {
                0 => RemasterPreset.CleanAndTransparent,
                1 => RemasterPreset.WarmAnalog,
                2 => RemasterPreset.PunchyClub,
                3 => RemasterPreset.VocalAcoustic,
                4 => RemasterPreset.LoudMaximizer,
                _ => RemasterPreset.Custom
            };

            if (preset != RemasterPreset.Custom)
            {
                var temp = new RemasterEffect();
                temp.ApplyPreset(preset);

                _effectsLoading = true;
                try
                {
                    RemasterWarmthSlider.Value = temp.WarmthDb;
                    RemasterPunchSlider.Value = temp.PunchDb;
                    RemasterBrillianceSlider.Value = temp.BrillianceDb;
                    RemasterWidthSlider.Value = temp.StereoWidth;
                    RemasterDriveSlider.Value = temp.Drive;
                    RemasterRatioSlider.Value = temp.Ratio;
                    RemasterCeilingSlider.Value = temp.LimiterCeilingDb;
                }
                finally
                {
                    _effectsLoading = false;
                }
            }
            UpdateEffectLabels();
            CommitEffectsToChain();
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
            if (sender is Slider slider && slider.Name.StartsWith("Remaster") && RemasterPresetCombo != null && RemasterPresetCombo.SelectedIndex != 5)
            {
                RemasterPresetCombo.SelectedIndex = 5;
            }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void EffectEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (_effectsLoading) return;
            CommitEffectsToChain();
        }

        private void EffectsPanelChevron_Click(object sender, RoutedEventArgs e)
        {
            if (EffectsRackScrollViewer == null) return;
            bool expand = EffectsRackScrollViewer.Visibility == Visibility.Collapsed;
            EffectsRackScrollViewer.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FadeHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void GateHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void EchoHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void EqHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void CompressorHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void DistortionHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void ReverbHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void PitchShiftHeaderButton_Click(object sender, RoutedEventArgs e) { }
        private void RemasterHeaderButton_Click(object sender, RoutedEventArgs e) { }

        private void ResetGate_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                GateEnabledCheck.IsChecked = false;
                GateThresholdSlider.Value = -40;
                GateAttackSlider.Value = 10;
                GateReleaseSlider.Value = 100;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetEq_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                EqEnabledCheck.IsChecked = false;
                EqSubBassSlider.Value = 0;
                EqBassSlider.Value = 0;
                EqMidSlider.Value = 0;
                EqPresenceSlider.Value = 0;
                EqTrebleSlider.Value = 0;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetCompressor_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                CompressorEnabledCheck.IsChecked = false;
                CompThresholdSlider.Value = -20;
                CompRatioSlider.Value = 4;
                CompAttackSlider.Value = 10;
                CompReleaseSlider.Value = 100;
                CompMakeupSlider.Value = 0;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetPitchShift_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                PitchShiftEnabledCheck.IsChecked = false;
                PitchShiftSemitonesSlider.Value = 0;
                PitchShiftGrainSizeSlider.Value = 50;
                PitchShiftMixSlider.Value = 1.0;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetDistortion_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                DistortionEnabledCheck.IsChecked = false;
                DistDriveSlider.Value = 8;
                DistMixSlider.Value = 0.80;
                DistOutputSlider.Value = 0.80;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetEcho_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                EchoEnabledCheck.IsChecked = false;
                EchoDelaySlider.Value = 200;
                EchoFeedbackSlider.Value = 0.30;
                EchoMixSlider.Value = 0.40;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetReverb_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                ReverbEnabledCheck.IsChecked = false;
                ReverbRoomSlider.Value = 0.5;
                ReverbDampingSlider.Value = 0.5;
                ReverbMixSlider.Value = 0.3;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetFade_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                FadeEnabledCheck.IsChecked = false;
                FadeInSlider.Value = 500;
                FadeOutSlider.Value = 500;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetRemaster_Click(object sender, RoutedEventArgs e)
        {
            _effectsLoading = true;
            try
            {
                RemasterEnabledCheck.IsChecked = false;
                RemasterPresetCombo.SelectedIndex = 5; // Custom / Default
                RemasterWarmthSlider.Value = 0;
                RemasterPunchSlider.Value = 0;
                RemasterBrillianceSlider.Value = 0;
                RemasterWidthSlider.Value = 1.0;
                RemasterDriveSlider.Value = 1.0;
                RemasterRatioSlider.Value = 2.0;
                RemasterCeilingSlider.Value = -0.1;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetMasterBus_Click(object sender, RoutedEventArgs e)
        {
            GainSlider.Value = 0;
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
                EchoFeedbackSlider.Value = 0.30;
                EchoMixSlider.Value = 0.40;
                EqEnabledCheck.IsChecked = false;
                EqSubBassSlider.Value = 0;
                EqBassSlider.Value = 0;
                EqMidSlider.Value = 0;
                EqPresenceSlider.Value = 0;
                EqTrebleSlider.Value = 0;
                CompressorEnabledCheck.IsChecked = false;
                CompThresholdSlider.Value = -20;
                CompRatioSlider.Value = 4;
                CompAttackSlider.Value = 10;
                CompReleaseSlider.Value = 100;
                CompMakeupSlider.Value = 0;
                DistortionEnabledCheck.IsChecked = false;
                DistDriveSlider.Value = 8;
                DistMixSlider.Value = 0.80;
                DistOutputSlider.Value = 0.80;
                ReverbEnabledCheck.IsChecked = false;
                ReverbRoomSlider.Value = 0.5;
                ReverbDampingSlider.Value = 0.5;
                ReverbMixSlider.Value = 0.3;
                PitchShiftEnabledCheck.IsChecked = false;
                PitchShiftSemitonesSlider.Value = 0;
                PitchShiftGrainSizeSlider.Value = 50;
                PitchShiftMixSlider.Value = 1.0;
                RemasterEnabledCheck.IsChecked = false;
                RemasterPresetCombo.SelectedIndex = 5;
                RemasterWarmthSlider.Value = 0;
                RemasterPunchSlider.Value = 0;
                RemasterBrillianceSlider.Value = 0;
                RemasterWidthSlider.Value = 1.0;
                RemasterDriveSlider.Value = 1.0;
                RemasterRatioSlider.Value = 2.0;
                RemasterCeilingSlider.Value = -0.1;
            }
            finally { _effectsLoading = false; }
            UpdateEffectLabels();
            CommitEffectsToChain();
        }

        private void ResetAllBtn_Click(object sender, RoutedEventArgs e)
        {
            ResetEffects_Click(sender, e);
            GainSlider.Value = 0;
            _trimStartFraction = 0.0;
            _trimEndFraction = 1.0;
            UpdateHandlePositions();
            UpdateTimeLabels();
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

        private void CardHeader_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject orig && IsOrInside<System.Windows.Controls.CheckBox>(orig))
                return;

            if (sender is FrameworkElement header && header.Tag is UIElement body)
            {
                bool willCollapse = body.Visibility == Visibility.Visible;
                body.Visibility = willCollapse ? Visibility.Collapsed : Visibility.Visible;

                if (header is System.Windows.Controls.Panel panel)
                {
                    foreach (var child in panel.Children)
                    {
                        if (child is StackPanel sp)
                        {
                            foreach (var spChild in sp.Children)
                            {
                                if (spChild is TextBlock tb && (tb.Text == "▾" || tb.Text == "▸"))
                                {
                                    tb.Text = willCollapse ? "▸" : "▾";
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static bool IsOrInside<T>(DependencyObject? obj) where T : DependencyObject
        {
            while (obj != null)
            {
                if (obj is T) return true;
                obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
            }
            return false;
        }

        private void TrimInBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_totalDurationSeconds <= 0) return;

            double currentFraction;
            if (_isPreviewing)
            {
                double elapsed = (DateTime.UtcNow - _playbackStartedAt).TotalSeconds;
                double currentSec = Math.Clamp(_playbackStartSec + elapsed, _playbackStartSec, _playbackEndSec);
                currentFraction = currentSec / _totalDurationSeconds;
            }
            else
            {
                currentFraction = _trimStartFraction;
            }

            double minTrimFraction = MinTrimSeconds / _totalDurationSeconds;
            _trimStartFraction = Math.Clamp(currentFraction, 0.0, _trimEndFraction - minTrimFraction);

            UpdateHandlePositions();
            UpdateTimeLabels();
        }

        private void TrimOutBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_totalDurationSeconds <= 0) return;

            double currentFraction;
            if (_isPreviewing)
            {
                double elapsed = (DateTime.UtcNow - _playbackStartedAt).TotalSeconds;
                double currentSec = Math.Clamp(_playbackStartSec + elapsed, _playbackStartSec, _playbackEndSec);
                currentFraction = currentSec / _totalDurationSeconds;
            }
            else
            {
                currentFraction = _trimEndFraction;
            }

            double minTrimFraction = MinTrimSeconds / _totalDurationSeconds;
            _trimEndFraction = Math.Clamp(currentFraction, _trimStartFraction + minTrimFraction, 1.0);

            UpdateHandlePositions();
            UpdateTimeLabels();
        }

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPreviewing)
            {
                StopPreview();
                return;
            }

            StartPreview();
        }

        private void StartPreview()
        {
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
                StartTimecodeTimer();
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                bool shouldLoop = e.Exception == null &&
                                  _isPreviewing &&
                                  LoopToggle.IsChecked == true;

                StopPreview(false);

                if (shouldLoop && IsLoaded)
                    StartPreview();
            }));
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
            UpdatePlaybackLinePosition(fromSec);
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
            PlaybackLineTransform.X = fraction * _waveformWidth;
        }

        private void StopPreview(bool stopPlayer = true)
        {
            if (_isStoppingPreview) return;
            _isStoppingPreview = true;

            try
            {
                StopTimecodeTimer();
                UpdatePlaybackTimecode(0.0);
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
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!_isClipping && Array.Exists(snapshot, sample => sample >= 1.0f))
                {
                    _isClipping = true;
                    ClipAlertButton.Visibility = Visibility.Visible;
                }

                UpdateVertMeter(snapshot);
                if (snapshot.Length > 0)
                {
                    MasterSpectrumVisualizer?.SetAudioLevel(snapshot[0]);
                }
            }));
        }

        private void ResetClipAlert_Click(object sender, RoutedEventArgs e)
        {
            _isClipping = false;
            ClipAlertButton.Visibility = Visibility.Collapsed;
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

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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

                    // Seek to trim start.
                    // For Opus: SeekTo can surface stale packets, so use decode-and-discard.
                    // For FLAC: FlacReaderAdapter.CurrentTime now seeks via FlakeReader.Position,
                    //   which is sample-frame accurate and does not silently fail.
                    // For all other formats: CurrentTime seek is reliable.
                    string fileExt = Path.GetExtension(_filePath).TrimStart('.').ToLowerInvariant();
                    if (startSec > 0.001)
                    {
                        if (fileExt == "opus")
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
                        else
                        {
                            reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                        }
                    }

                    // Duration of the trimmed region.
                    // Use frame count to avoid AverageBytesPerSecond estimation errors
                    // (especially relevant for FLAC where the PCM layout from FlakeReader
                    //  is now exact integer PCM and sampleRate * blockAlign is the real rate).
                    double trimDuration = endSec - startSec;
                    int blockAlign = format.BlockAlign;
                    long framesToWrite = (long)(trimDuration * format.SampleRate);
                    if (framesToWrite <= 0) return;

                    float gainFactor = noGain ? 1f : (float)Math.Pow(10.0, _gainDb / 20.0);

                    var effectChain = GetOrLoadEffectChain();
                    PrepareEffectChain(effectChain, trimDuration,
                        (double)format.SampleRate);

                    using var recorder = StreamingRecorderFactory.CreateForFile(_filePath);
                    recorder.BeginRecording(tempPath, format);

                    byte[] buffer = new byte[format.SampleRate * blockAlign];
                    long framesWritten = 0;
                    while (framesWritten < framesToWrite)
                    {
                        long framesRemaining = framesToWrite - framesWritten;
                        int toRead = (int)Math.Min(buffer.Length, framesRemaining * blockAlign);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        int alignedRead = AlignRecordedByteCount(read, format);
                        if (alignedRead <= 0) continue;
                        if (!noGain)
                            ApplyGainToBuffer(buffer, alignedRead, format, gainFactor);
                        ApplyEffectsToBuffer(buffer, alignedRead, format, effectChain);
                        recorder.AppendSamples(buffer, 0, alignedRead);
                        framesWritten += alignedRead / blockAlign;
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
                        if (fileExt == "opus")
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
                        else
                        {
                            reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                        }
                    }

                    double trimDuration = endSec - startSec;
                    int blockAlign = format.BlockAlign;
                    long framesToWrite = (long)(trimDuration * format.SampleRate);
                    if (framesToWrite <= 0) return;

                    float gainFactor = noGain ? 1f : (float)Math.Pow(10.0, _gainDb / 20.0);

                    var effectChain = GetOrLoadEffectChain();
                    PrepareEffectChain(effectChain, trimDuration,
                        (double)format.SampleRate);

                    using var recorder = StreamingRecorderFactory.CreateForFile(_filePath);
                    recorder.BeginRecording(tempPath, format);

                    byte[] buffer = new byte[format.SampleRate * blockAlign];
                    long framesWritten = 0;
                    while (framesWritten < framesToWrite)
                    {
                        long framesRemaining = framesToWrite - framesWritten;
                        int toRead = (int)Math.Min(buffer.Length, framesRemaining * blockAlign);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        int alignedRead = AlignRecordedByteCount(read, format);
                        if (alignedRead <= 0) continue;
                        if (!noGain)
                            ApplyGainToBuffer(buffer, alignedRead, format, gainFactor);
                        ApplyEffectsToBuffer(buffer, alignedRead, format, effectChain);
                        recorder.AppendSamples(buffer, 0, alignedRead);
                        framesWritten += alignedRead / blockAlign;
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
        private void VstChevron_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { }

        private VstPluginWindow? _activeVstPluginWindow;

        private void ShowVstEditor_Click(object sender, RoutedEventArgs e)
        {
            if (_activeVstPluginWindow != null && _activeVstPluginWindow.IsLoaded)
            {
                if (_activeVstPluginWindow.WindowState == WindowState.Minimized)
                    _activeVstPluginWindow.WindowState = WindowState.Normal;
                _activeVstPluginWindow.Activate();
                _activeVstPluginWindow.Focus();
                return;
            }

            var win = new VstPluginWindow(_vstEffects);
            _activeVstPluginWindow = win;
            win.Closed += (s, args) => _activeVstPluginWindow = null;
            win.Show();
        }

        private bool IsPluginAlreadyLoaded(string name)
        {
            foreach (var vst in _vstEffects)
            {
                if (string.Equals(vst.Name, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ── Workspace View Modes & Category Filtering ─────────────────────

        private void ViewMode_Split_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(0);
        }

        private void ViewMode_Waveform_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(1);
        }

        private void ViewMode_Effects_Click(object sender, RoutedEventArgs e)
        {
            SetViewMode(2);
        }

        private void SetViewMode(int mode)
        {
            // View modes handled safely by layout
        }

        private void RackCategory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag is string category)
            {
                FilterRackCategory(category);
            }
        }

        private void FilterRackCategory(string category)
        {
            // Category filter handled safely by layout
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return $"{kb:0.0} KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return $"{mb:0.00} MB";
            double gb = mb / 1024.0;
            return $"{gb:0.00} GB";
        }

        private static void SetModuleVisibility(FrameworkElement? element, bool visible)
        {
            if (element != null)
            {
                element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
}
