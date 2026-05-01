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
using NAudio.CoreAudioApi;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Services;

namespace PaDDY
{
    [SupportedOSPlatform("windows")]
    public partial class AudioEditorWindow : Window
    {
        private readonly string _filePath;
        private TimeSpan _totalDuration;
        private double _trimStartFraction;  // 0.0 – 1.0
        private double _trimEndFraction = 1.0;

        private WasapiOut? _player;
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

        public AudioEditorWindow(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;

            FileNameLabel.Text = Path.GetFileName(filePath);

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

            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];
            int midY = height / 2;

            // Draw centre line
            for (int x = 0; x < width; x++)
                SetPixel(pixels, stride, x, midY, 0x44, 0x44, 0x44, 0xFF);

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
                    float dist = Math.Abs(y - midY) / (float)midY;
                    byte g = (byte)(200 - (int)(dist * 80));
                    byte r = (byte)(40 + (int)(dist * 30));
                    SetPixel(pixels, stride, x, y, r, g, 0x30, 0xFF);
                }
            }

            bmp.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            WaveformImage.Source = bmp;
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

                // Wrap with metering
                _meterProvider = new MeteringSampleProvider(sp);
                _meterProvider.SamplesPerNotification = Math.Max(1, _meterProvider.WaveFormat.SampleRate / 60);
                _meterProvider.StreamVolume += OnMeterStreamVolume;
                EnsureVertMeterChannels(_meterProvider.WaveFormat.Channels);
                ResetVertMeter();

                _player = new WasapiOut(AudioClientShareMode.Shared, true, 100);
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
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x1A, 0x1A)),
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = 10000
                };

                var peak = new Border
                {
                    Height = 2,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xC1, 0x07)),
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

        // ── Save (destructive trim) ────────────────────────────────────────

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();

            double startSec = _trimStartFraction * _totalDuration.TotalSeconds;
            double endSec = _trimEndFraction * _totalDuration.TotalSeconds;

            bool noTrim = _trimStartFraction <= 0.001 && _trimEndFraction >= 0.999;
            bool noGain = Math.Abs(_gainDb) < 0.01;

            // Nothing to do — no trim and no gain
            if (noTrim && noGain)
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
                    // Avoid both issues by decoding-and-discarding up to startSec instead.
                    string fileExt = Path.GetExtension(_filePath).TrimStart('.').ToLowerInvariant();
                    if (startSec > 0.001)
                    {
                        if (fileExt == "opus")
                        {
                            // Decode-and-discard to reach startSec (guarantees correct position).
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

                    // Duration of the trimmed region in bytes
                    double trimDuration = endSec - startSec;
                    long bytesToWrite = (long)(trimDuration * format.AverageBytesPerSecond);
                    int blockAlign = format.BlockAlign;
                    bytesToWrite = bytesToWrite / blockAlign * blockAlign;
                    if (bytesToWrite <= 0) return;

                    float gainFactor = noGain ? 1f : (float)Math.Pow(10.0, _gainDb / 20.0);

                    using var recorder = StreamingRecorderFactory.CreateForFile(_filePath);
                    recorder.BeginRecording(tempPath, format);

                    byte[] buffer = new byte[format.SampleRate * blockAlign];
                    long written = 0;
                    while (written < bytesToWrite)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        if (!noGain)
                            ApplyGainToBuffer(buffer, read, format, gainFactor);
                        recorder.AppendSamples(buffer, 0, read);
                        written += read;
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

            // Generate a unique copy path
            string dir = Path.GetDirectoryName(_filePath)!;
            string nameNoExt = Path.GetFileNameWithoutExtension(_filePath);
            string ext = Path.GetExtension(_filePath);
            string copyPath = Path.Combine(dir, nameNoExt + "_copy" + ext);
            int counter = 2;
            while (File.Exists(copyPath))
                copyPath = Path.Combine(dir, $"{nameNoExt}_copy{counter++}{ext}");

            if (noTrim && noGain)
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
                    long bytesToWrite = (long)(trimDuration * format.AverageBytesPerSecond);
                    int blockAlign = format.BlockAlign;
                    bytesToWrite = bytesToWrite / blockAlign * blockAlign;
                    if (bytesToWrite <= 0) return;

                    float gainFactor = noGain ? 1f : (float)Math.Pow(10.0, _gainDb / 20.0);

                    using var recorder = StreamingRecorderFactory.CreateForFile(_filePath);
                    recorder.BeginRecording(tempPath, format);

                    byte[] buffer = new byte[format.SampleRate * blockAlign];
                    long written = 0;
                    while (written < bytesToWrite)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        if (!noGain)
                            ApplyGainToBuffer(buffer, read, format, gainFactor);
                        recorder.AppendSamples(buffer, 0, read);
                        written += read;
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
    }
}
