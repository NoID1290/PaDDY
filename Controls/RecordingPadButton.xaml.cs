using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfControl = System.Windows.Controls.UserControl;
using WpfButton = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using System.Windows.Media.Animation;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Models;
using PaDDY.Services;

namespace PaDDY.Controls
{
    [SupportedOSPlatform("windows")]
    public partial class RecordingPadButton : WpfControl
    {
        private static readonly SolidColorBrush BrushNormal;
        private static readonly SolidColorBrush BrushFavorite;
        private static readonly SolidColorBrush BrushPlaying;

        static RecordingPadButton()
        {
            BrushNormal = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            BrushNormal.Freeze();
            BrushFavorite = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // amber / gold
            BrushFavorite.Freeze();
            BrushPlaying = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // green
            BrushPlaying.Freeze();
        }

        public RecordingEntry? Entry { get; private set; }

        // Device routing injected from MainWindow
        public int OutputDeviceIndex { get; set; } = 0;

        // Listen device: -2 = disabled, -1 = default, 0..N = specific
        public int ListenDeviceIndex { get; set; } = -2;

        // Volume controls (0.0–1.0)
        public float OutputVolume { get; set; } = 1.0f;
        public float ListenVolume { get; set; } = 1.0f;

        /// <summary>Fired with (left, right) normalised 0-100 values during playback on the main output.</summary>
        public event Action<double, double>? PlaybackRmsChanged;

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                _isFavorite = value;
                FavBtn.Content = value ? "★" : "☆";
                FavBtn.Foreground = value
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07))
                    : new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
                if (!_isPlaying)
                    TileBorder.BorderBrush = value ? BrushFavorite : BrushNormal;
            }
        }

        private IWavePlayer? _player;
        private IUnifiedAudioReader? _reader;
        private PlaybackMeterProvider? _meterProvider;
        private VolumeSampleProvider? _outputVolumeProvider;
        private IWavePlayer? _listenPlayer;
        private IUnifiedAudioReader? _listenReader;
        private VolumeSampleProvider? _listenVolumeProvider;
        private bool _isPlaying;

        /// <summary>Fired when the user clicks the inline delete (âœ•) or menu Delete.</summary>
        public event EventHandler? DeleteRequested;

        /// <summary>Fired when the user toggles the favorite (â­) button.</summary>
        public event EventHandler? FavoriteToggled;
        /// <summary>Fired after a successful rename; args are (oldFilePath, newFilePath).</summary>
        public event Action<string, string>? FileRenamed;
        /// <summary>Fired when "Save as Copy" produces a new file; args are (newFilePath, addToFavorite).</summary>
        public event Action<string, bool>? RecordingCopied;
        public RecordingPadButton()
        {
            InitializeComponent();

            // Play entrance animation when loaded
            Loaded += (_, _) =>
            {
                try
                {
                    var entrance = (Storyboard)FindResource("EntranceAnimation");
                    entrance.Begin(this);
                }
                catch { }
            };

            MouseLeftButtonUp += (_, e) =>
            {
                // Don't trigger playback if the click was on an overlay button
                if (e.OriginalSource is not FrameworkElement fe ||
                    (!IsOverlayButton(fe)))
                {
                    TogglePlay();
                }
            };
        }

        private static bool IsOverlayButton(FrameworkElement el)
        {
            DependencyObject? current = el;
            while (current != null)
            {
                if (current is WpfButton) return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        public void SetEntry(RecordingEntry entry)
        {
            Entry = entry;
            NameLabel.Text = Path.GetFileNameWithoutExtension(entry.FilePath)
                                 .Replace("Recording_", "")
                                 .Replace("_", " ");
            DurationLabel.Text = entry.DurationLabel;
            ToolTip = entry.FilePath;
        }

        // â”€â”€ Overlay button handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void FavBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            IsFavorite = !IsFavorite;
            if (Entry != null) Entry.IsFavorite = IsFavorite;
            FavoriteToggled?.Invoke(this, EventArgs.Empty);
        }

        private void DelBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            MenuDelete_Click(sender, e);
        }

        private void RenameBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OpenRename();
        }

        private void TrimBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OpenAudioEditor();
        }

        public void OpenAudioEditor()
        {
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            StopPlayback();

            var editor = new AudioEditorWindow(Entry.FilePath)
            {
                Owner = Window.GetWindow(this)
            };
            if (editor.ShowDialog() == true)
            {
                if (editor.CopyFilePath != null)
                {
                    // "Save as Copy" — fire event so MainWindow adds a new pad
                    RecordingCopied?.Invoke(editor.CopyFilePath, editor.ShouldSaveToFavorite);
                }
                else
                {
                    // In-place save — re-read duration from the trimmed file
                    try
                    {
                        using var reader = AudioReaderFactory.Open(Entry.FilePath);
                        Entry.Duration = reader.TotalTime;
                    }
                    catch { }
                    SetEntry(Entry);

                    if (editor.ShouldSaveToFavorite && !IsFavorite)
                    {
                        IsFavorite = true;
                        Entry.IsFavorite = true;
                        FavoriteToggled?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        // â”€â”€ Right-click: play on listen/monitor device only â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void OnMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (IsOverlayButton(e.OriginalSource as FrameworkElement ?? this)) return;
            e.Handled = true;
            StartPlaybackListenOnly();
        }

        // â”€â”€ Playback â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        public void TogglePlay()
        {
            if (_isPlaying) StopPlayback();
            else StartPlayback();
        }

        private void StartPlayback()
        {
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            StopPlayback();

            try
            {
                _reader = AudioReaderFactory.Open(Entry.FilePath);
                ISampleProvider playbackSource = BuildPlaybackSource(_reader.AsSampleProvider());
                _outputVolumeProvider = new VolumeSampleProvider(playbackSource)
                {
                    Volume = Math.Clamp(OutputVolume, 0.0f, 1.0f)
                };
                _meterProvider = new PlaybackMeterProvider(_outputVolumeProvider);
                _meterProvider.RmsLevelChanged += (l, r) => PlaybackRmsChanged?.Invoke(l, r);
                _player = CreateWasapiPlayer(OutputDeviceIndex, 100);
                _player.Init(_meterProvider.ToWaveProvider16());
                _player.Volume = 1.0f;
                _player.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _player.Play();

                if (ListenDeviceIndex >= -1)
                {
                    _listenReader = AudioReaderFactory.Open(Entry.FilePath);
                    ISampleProvider listenSource = BuildPlaybackSource(_listenReader.AsSampleProvider());
                    _listenVolumeProvider = new VolumeSampleProvider(listenSource)
                    {
                        Volume = Math.Clamp(ListenVolume, 0.0f, 1.0f)
                    };
                    _listenPlayer = CreateWasapiPlayer(ListenDeviceIndex, 120);
                    _listenPlayer.Init(_listenVolumeProvider.ToWaveProvider16());
                    _listenPlayer.Volume = 1.0f;
                    _listenPlayer.Play();
                }

                SetPlayingVisual(true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "PaDDY",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StopPlayback();
            }
        }

        /// <summary>Plays audio only on the listen/monitor device (right-click behaviour).</summary>
        private void StartPlaybackListenOnly()
        {
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            if (ListenDeviceIndex < -1) return; // listen disabled

            StopPlayback();
            try
            {
                _listenReader = AudioReaderFactory.Open(Entry.FilePath);
                ISampleProvider listenSource = BuildPlaybackSource(_listenReader.AsSampleProvider());
                _listenVolumeProvider = new VolumeSampleProvider(listenSource)
                {
                    Volume = Math.Clamp(ListenVolume, 0.0f, 1.0f)
                };
                _listenPlayer = CreateWasapiPlayer(ListenDeviceIndex, 120);
                _listenPlayer.Init(_listenVolumeProvider.ToWaveProvider16());
                _listenPlayer.Volume = 1.0f;
                _listenPlayer.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _listenPlayer.Play();
                SetPlayingVisual(true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "PaDDY",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StopPlayback();
            }
        }

        public void StopPlayback()
        {
            _player?.Stop();
            _player?.Dispose();
            _player = null;
            if (_meterProvider != null)
                _meterProvider.RmsLevelChanged -= null; // event will be GC'd with the provider
            _meterProvider = null;
            _outputVolumeProvider = null;
            _reader?.Dispose();
            _reader = null;
            _listenPlayer?.Stop();
            _listenPlayer?.Dispose();
            _listenPlayer = null;
            _listenReader?.Dispose();
            _listenReader = null;
            _listenVolumeProvider = null;
            PlaybackRmsChanged?.Invoke(0, 0);
            SetPlayingVisual(false);
        }

        public void RefreshLiveVolumes()
        {
            if (_outputVolumeProvider != null)
                _outputVolumeProvider.Volume = Math.Clamp(OutputVolume, 0.0f, 1.0f);
            if (_listenVolumeProvider != null)
                _listenVolumeProvider.Volume = Math.Clamp(ListenVolume, 0.0f, 1.0f);

            if (_player != null)
                _player.Volume = 1.0f;
            if (_listenPlayer != null)
                _listenPlayer.Volume = 1.0f;
        }

        private void SetPlayingVisual(bool playing)
        {
            _isPlaying = playing;
            IconText.Text = playing ? "⏹" : "🎤";

            TileBorder.BorderBrush = playing
                ? BrushPlaying
                : (_isFavorite ? BrushFavorite : BrushNormal);
        }

        private static IWavePlayer CreateWasapiPlayer(int deviceIndex, int latencyMs)
        {
            MMDevice? device = ResolveRenderDevice(deviceIndex);
            return device != null
                ? new WasapiOut(device, AudioClientShareMode.Shared, true, latencyMs)
                : new WasapiOut(AudioClientShareMode.Shared, true, latencyMs);
        }

        private static MMDevice? ResolveRenderDevice(int deviceIndex)
        {
            if (deviceIndex < 0)
                return null;

            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            return deviceIndex < devices.Count ? devices[deviceIndex] : null;
        }

        private static ISampleProvider BuildPlaybackSource(ISampleProvider source)
        {
            if (source.WaveFormat.Channels == 1)
            {
                return source.ToStereo();
            }

            if (source.WaveFormat.Channels > 2)
            {
                // Route front-left/front-right to stereo for robust device compatibility.
                var mux = new MultiplexingSampleProvider(new[] { source }, 2);
                mux.ConnectInputToOutput(0, 0);
                mux.ConnectInputToOutput(1, 1);
                return mux;
            }

            return source;
        }

        // â”€â”€ Context menu handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            OpenRename();
        }

        public void OpenRename()
        {
            if (Entry == null) return;

            var dialog = new RenameDialog(Path.GetFileNameWithoutExtension(Entry.FilePath))
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;
            string originalExt = Path.GetExtension(Entry.FilePath);
            if (!Path.HasExtension(newName))
                newName += originalExt;

            string newPath = Path.Combine(Path.GetDirectoryName(Entry.FilePath)!, newName);
            try
            {
                StopPlayback();
                string oldPath = Entry.FilePath;
                File.Move(Entry.FilePath, newPath);
                Entry.FilePath = newPath;
                SetEntry(Entry);
                FileRenamed?.Invoke(oldPath, newPath);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Rename failed:\n{ex.Message}", "PaDDY",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null) return;
            StopPlayback();
            try { File.Delete(Entry.FilePath); } catch { }
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
