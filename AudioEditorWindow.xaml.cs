using System;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.Wave;

namespace Paddy
{
    [SupportedOSPlatform("windows")]
    public partial class AudioEditorWindow : Window
    {
        private readonly string _filePath;
        private TimeSpan _totalDuration;
        private double _trimStartFraction;  // 0.0 – 1.0
        private double _trimEndFraction = 1.0;

        private WaveOutEvent? _player;
        private AudioFileReader? _reader;
        private DispatcherTimer? _playbackTimer;
        private bool _isPreviewing;
        private bool _isStoppingPreview;

        private double _totalDurationSeconds;
        private double _waveformWidth;

        private const double MinTrimSeconds = 0.05; // 50 ms minimum

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
                using var reader = new AudioFileReader(_filePath);
                _totalDuration = reader.TotalTime;
                _totalDurationSeconds = Math.Max(_totalDuration.TotalSeconds, 0.001);
                TotalDurationLabel.Text = FormatTime(_totalDuration);

                RenderWaveform(reader);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Could not read audio file:\n{ex.Message}", "Paddy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            UpdateHandlePositions();
            UpdateTimeLabels();
        }

        private void WaveformGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _waveformWidth = Math.Max(WaveformGrid.ActualWidth, 0);
            UpdateHandlePositions();

            if (_isPreviewing && _reader != null)
            {
                UpdatePlaybackLinePosition(_reader.CurrentTime.TotalSeconds);
            }
        }

        // ── Waveform rendering ──────────────────────────────────────────────

        private void RenderWaveform(AudioFileReader reader)
        {
            int width = (int)WaveformGrid.ActualWidth;
            int height = (int)WaveformGrid.ActualHeight;
            if (width < 10 || height < 10) { width = 680; height = 180; }

            // Read all samples (mono-mixed)
            reader.Position = 0;
            int channels = reader.WaveFormat.Channels;
            long totalSamples = reader.Length / (reader.WaveFormat.BitsPerSample / 8);
            long monoSamples = totalSamples / channels;

            float[] buffer = new float[reader.WaveFormat.SampleRate * channels]; // 1 sec chunks
            var peaks = new (float min, float max)[width];

            // Initialise peaks
            for (int i = 0; i < width; i++)
                peaks[i] = (0f, 0f);

            long samplesRead = 0;
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                int monoRead = read / channels;
                for (int i = 0; i < monoRead; i++)
                {
                    // Mix channels to mono
                    float sample = 0f;
                    for (int ch = 0; ch < channels; ch++)
                        sample += buffer[i * channels + ch];
                    sample /= channels;

                    long monoIndex = samplesRead + i;
                    int bucket = (int)(monoIndex * width / monoSamples);
                    if (bucket >= width) bucket = width - 1;

                    if (sample < peaks[bucket].min) peaks[bucket] = (sample, peaks[bucket].max);
                    if (sample > peaks[bucket].max) peaks[bucket] = (peaks[bucket].min, sample);
                }
                samplesRead += monoRead;
            }

            // Draw to WriteableBitmap
            var bmp = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            int midY = height / 2;

            // Background is transparent (the parent Border provides #FF161616)
            // Draw centre line
            for (int x = 0; x < width; x++)
            {
                SetPixel(pixels, stride, x, midY, 0x44, 0x44, 0x44, 0xFF);
            }

            // Draw waveform
            for (int x = 0; x < width; x++)
            {
                float pMin = peaks[x].min;
                float pMax = peaks[x].max;

                int yTop = midY - (int)(pMax * midY);
                int yBot = midY - (int)(pMin * midY);

                yTop = Math.Clamp(yTop, 0, height - 1);
                yBot = Math.Clamp(yBot, 0, height - 1);

                for (int y = yTop; y <= yBot; y++)
                {
                    // Gradient from bright green at centre to darker at extremes
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

        private void PlayBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isPreviewing)
            {
                StopPreview();
                return;
            }

            try
            {
                _reader = new AudioFileReader(_filePath);

                _player = new WaveOutEvent();
                _player.PlaybackStopped += Player_PlaybackStopped;
                _player.Init(_reader);

                double startSec = _trimStartFraction * _totalDurationSeconds;
                _reader.CurrentTime = TimeSpan.FromSeconds(startSec);
                _player.Play();

                _isPreviewing = true;
                PlayBtn.Content = "⏹  Stop";
                PlaybackLine.Visibility = Visibility.Visible;
                UpdatePlaybackLinePosition(startSec);

                _playbackTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _playbackTimer.Tick += PlaybackTimer_Tick;
                _playbackTimer.Start();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "Paddy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                StopPreview();
            }
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (_reader == null || _player == null) return;

            double currentSec = _reader.CurrentTime.TotalSeconds;
            double endSec = _trimEndFraction * _totalDurationSeconds;

            if (currentSec >= endSec)
            {
                StopPreview();
                return;
            }

            UpdatePlaybackLinePosition(currentSec);
        }

        private void Player_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => StopPreview(false)));
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
                if (_playbackTimer != null)
                {
                    _playbackTimer.Stop();
                    _playbackTimer.Tick -= PlaybackTimer_Tick;
                    _playbackTimer = null;
                }

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
                PlaybackLineTransform.X = 0;

                if (PlayBtn != null)
                {
                    PlayBtn.Content = "▶  Preview";
                }
            }
            finally
            {
                _isStoppingPreview = false;
            }
        }

        // ── Save (destructive trim) ────────────────────────────────────────

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            StopPreview();

            double startSec = _trimStartFraction * _totalDuration.TotalSeconds;
            double endSec = _trimEndFraction * _totalDuration.TotalSeconds;

            // Nothing to trim — the entire file is already selected
            if (_trimStartFraction <= 0.001 && _trimEndFraction >= 0.999)
            {
                DialogResult = true;
                return;
            }

            string tempPath = _filePath + ".trim.tmp";
            try
            {
                using (var reader = new AudioFileReader(_filePath))
                {
                    var format = reader.WaveFormat;
                    reader.CurrentTime = TimeSpan.FromSeconds(startSec);

                    long startByte = reader.Position;
                    long endByte = (long)(endSec / _totalDuration.TotalSeconds * reader.Length);
                    // Align to block boundary
                    int blockAlign = format.BlockAlign;
                    endByte = endByte / blockAlign * blockAlign;

                    long bytesToWrite = endByte - startByte;
                    if (bytesToWrite <= 0) return;

                    using var writer = new WaveFileWriter(tempPath, format);
                    byte[] buffer = new byte[format.SampleRate * blockAlign]; // ~1 sec chunks
                    long written = 0;
                    while (written < bytesToWrite)
                    {
                        int toRead = (int)Math.Min(buffer.Length, bytesToWrite - written);
                        int read = reader.Read(buffer, 0, toRead);
                        if (read == 0) break;
                        writer.Write(buffer, 0, read);
                        written += read;
                    }
                }

                // Replace original with trimmed file
                File.Delete(_filePath);
                File.Move(tempPath, _filePath);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                // Clean up temp file on failure
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

                System.Windows.MessageBox.Show($"Trim failed:\n{ex.Message}", "Paddy",
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
    }
}
