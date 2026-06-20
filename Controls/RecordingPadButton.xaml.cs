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
        private static readonly SolidColorBrush BrushFavStar;
        private static readonly SolidColorBrush BrushUnfavStar;

        static RecordingPadButton()
        {
            BrushNormal = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
            BrushNormal.Freeze();
            BrushFavorite = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); // amber / gold
            BrushFavorite.Freeze();
            BrushPlaying = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // green
            BrushPlaying.Freeze();
            BrushFavStar = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07));
            BrushFavStar.Freeze();
            BrushUnfavStar = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
            BrushUnfavStar.Freeze();
        }

        /// <summary>
        /// When >0 the entrance animation is suppressed (bulk load in progress).
        /// Increment before loading a batch, decrement after.
        /// </summary>
        public static int SuppressEntranceAnimation;

        public RecordingEntry? Entry { get; private set; }

        // Device routing injected from MainWindow
        public int OutputDeviceIndex { get; set; } = 0;

        // Listen device: -2 = disabled, -1 = default, 0..N = specific
        public int ListenDeviceIndex { get; set; } = -2;

        // 0 = default, 1..N = devices 0..N-1
        public int TrimEditorOutputDeviceIndex { get; set; } = 0;

        // Volume controls (0.0–1.0)
        public float OutputVolume { get; set; } = 1.0f;
        public float ListenVolume { get; set; } = 1.0f;

        /// <summary>Fired with (left, right) normalised 0-100 values during playback on the main output.</summary>
        public event Action<double, double>? PlaybackRmsChanged;
        /// <summary>Fired with (left, right) normalised 0-100 values during playback on the monitor output.</summary>
        public event Action<double, double>? ListenPlaybackRmsChanged;

        private bool _isFavorite;
        public bool IsFavorite
        {
            get => _isFavorite;
            set
            {
                _isFavorite = value;
                FavBtn.Content = value ? "★" : "☆";
                FavBtn.Foreground = value ? BrushFavStar : BrushUnfavStar;
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
        private PlaybackMeterProvider? _listenMeterProvider;
        private bool _isPlaying;

        /// <summary>Fired when the user clicks the inline delete (âœ•) or menu Delete.</summary>
        public event EventHandler? DeleteRequested;

        /// <summary>Fired when the user toggles the favorite (★) button.</summary>
        public event EventHandler? FavoriteToggled;
        /// <summary>Fired after a successful rename; args are (entry, newDisplayName).</summary>
        public event Action<RecordingEntry, string>? RecordingRenamed;
        /// <summary>Fired after an in-place editor save; arg is the updated entry.</summary>
        public event Action<RecordingEntry>? RecordingEdited;
        /// <summary>Fired when "Save as Copy" produces a new file; args are (newFilePath, addToFavorite).</summary>
        public event Action<string, bool>? RecordingCopied;
        public RecordingPadButton()
        {
            InitializeComponent();

            // Play entrance animation when loaded — skip during bulk loads (startup / page switch)
            Loaded += (_, _) =>
            {
                if (SuppressEntranceAnimation > 0) return;
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
                if (_dragInProgress)
                {
                    _dragInProgress = false;
                    return;
                }
                if (e.OriginalSource is not FrameworkElement fe ||
                    (!IsOverlayButton(fe)))
                {
                    TogglePlay();
                }
            };

            PreviewMouseLeftButtonDown += (_, e) =>
            {
                _dragInProgress = false;
                _dragStartPoint = e.GetPosition(null);
            };

            PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragInProgress)
                    return;
                if (Entry == null || string.IsNullOrEmpty(Entry.RecordingId))
                    return;
                if (e.OriginalSource is FrameworkElement fe && IsOverlayButton(fe))
                    return;

                var pos = e.GetPosition(null);
                if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
                    return;

                _dragInProgress = true;
                DragGrabOffset = e.GetPosition(this);
                try
                {
                    DragStarting?.Invoke(this);
                    var data = new System.Windows.DataObject(PadDragFormat, this);
                    System.Windows.DragDrop.DoDragDrop(this, data, System.Windows.DragDropEffects.Move);
                }
                catch { }
                finally
                {
                    DragFinished?.Invoke(this);
                }
            };
        }

        /// <summary>DataObject format used when dragging a pad between panels/pages.</summary>
        public const string PadDragFormat = "PaddyRecordingPad";

        /// <summary>Mouse offset within the pad where the drag began (used to position the drag ghost).</summary>
        public System.Windows.Point DragGrabOffset { get; private set; }

        /// <summary>Raised just before the drag-drop loop begins (host sets up the drag visual).</summary>
        public event Action<RecordingPadButton>? DragStarting;

        /// <summary>Raised after the drag-drop loop completes (host tears down the visual and commits).</summary>
        public event Action<RecordingPadButton>? DragFinished;

        private System.Windows.Point _dragStartPoint;
        private bool _dragInProgress;

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
            NameLabel.Text = entry.FileName;
            DurationLabel.Text = entry.DurationLabel;
            ToolTip = entry.FileName;
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

            var editor = new AudioEditorWindow(
                Entry.FilePath,
                Entry.RecordingId,
                TrimEditorOutputDeviceIndex - 1,
                Entry.DisplayName)
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
                    // In-place save — re-read duration from the trimmed temp file
                    try
                    {
                        using var reader = AudioReaderFactory.Open(Entry.FilePath);
                        Entry.Duration = reader.TotalTime;
                    }
                    catch { }
                    SetEntry(Entry);
                    RecordingEdited?.Invoke(Entry);

                    if (editor.ShouldSaveToFavorite && !IsFavorite)
                    {
                        IsFavorite = true;
                        Entry.IsFavorite = true;
                        FavoriteToggled?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }

        // ── Right-click: play on listen/monitor device only ───────────────────────────────────
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
                _player = AudioOutputDeviceResolver.CreateWasapiPlayer(OutputDeviceIndex, 100);
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
                    _listenMeterProvider = new PlaybackMeterProvider(_listenVolumeProvider);
                    _listenMeterProvider.RmsLevelChanged += (l, r) => ListenPlaybackRmsChanged?.Invoke(l, r);
                    _listenPlayer = AudioOutputDeviceResolver.CreateWasapiPlayer(ListenDeviceIndex, 120);
                    _listenPlayer.Init(_listenMeterProvider.ToWaveProvider16());
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
                _listenMeterProvider = new PlaybackMeterProvider(_listenVolumeProvider);
                _listenMeterProvider.RmsLevelChanged += (l, r) => ListenPlaybackRmsChanged?.Invoke(l, r);
                _listenPlayer = AudioOutputDeviceResolver.CreateWasapiPlayer(ListenDeviceIndex, 120);
                _listenPlayer.Init(_listenMeterProvider.ToWaveProvider16());
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
            _listenMeterProvider = null;
            PlaybackRmsChanged?.Invoke(0, 0);
            ListenPlaybackRmsChanged?.Invoke(0, 0);
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

            var dialog = new RenameDialog(Entry.FileName)
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;

            // Preserve original extension from the display name if user omits it.
            string originalExt = Path.GetExtension(
                string.IsNullOrEmpty(Entry.DisplayName) ? Entry.FilePath : Entry.DisplayName);
            if (!Path.HasExtension(newName))
                newName += originalExt;

            // Update the display name in memory and notify MainWindow to persist to DB.
            Entry.DisplayName = newName;
            SetEntry(Entry);
            RecordingRenamed?.Invoke(Entry, newName);
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null) return;
            StopPlayback();
            // Temp file cleanup and DB deletion are handled by MainWindow via DeleteRequested.
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (Entry == null || !File.Exists(Entry.FilePath)) return;

            string ext = Path.GetExtension(Entry.FilePath);
            string defaultName = string.IsNullOrEmpty(Entry.DisplayName)
                ? Path.GetFileName(Entry.FilePath)
                : Path.ChangeExtension(Entry.DisplayName, ext);

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export recording",
                FileName = defaultName,
                DefaultExt = ext,
                Filter = $"Audio (*{ext})|*{ext}|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() != true) return;
            File.Copy(Entry.FilePath, dlg.FileName, overwrite: true);
        }
    }
}
