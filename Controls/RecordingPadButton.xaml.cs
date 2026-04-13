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

        private WaveOutEvent? _player;
        private IUnifiedAudioReader? _reader;
        private WaveOutEvent? _listenPlayer;
        private IUnifiedAudioReader? _listenReader;
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
                _player = new WaveOutEvent { DeviceNumber = OutputDeviceIndex };
                _player.Init(_reader.AsWaveProvider());
                _player.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _player.Play();

                if (ListenDeviceIndex >= -1)
                {
                    _listenReader = AudioReaderFactory.Open(Entry.FilePath);
                    _listenPlayer = new WaveOutEvent { DeviceNumber = ListenDeviceIndex };
                    _listenPlayer.Init(_listenReader.AsWaveProvider());
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
                _listenPlayer = new WaveOutEvent { DeviceNumber = ListenDeviceIndex };
                _listenPlayer.Init(_listenReader.AsWaveProvider());
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
            _reader?.Dispose();
            _reader = null;
            _listenPlayer?.Stop();
            _listenPlayer?.Dispose();
            _listenPlayer = null;
            _listenReader?.Dispose();
            _listenReader = null;
            SetPlayingVisual(false);
        }

        private void SetPlayingVisual(bool playing)
        {
            _isPlaying = playing;
            IconText.Text = playing ? "⏹" : "🎤";

            TileBorder.BorderBrush = playing
                ? BrushPlaying
                : (_isFavorite ? BrushFavorite : BrushNormal);
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
