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
using Paddy.Models;

namespace Paddy.Controls
{
    [SupportedOSPlatform("windows")]
    public partial class RecordingPadButton : WpfControl
    {
        private static readonly SolidColorBrush BrushNormal =
            new(Color.FromRgb(0x55, 0x55, 0x55));
        private static readonly SolidColorBrush BrushFavorite =
            new(Color.FromRgb(0xFF, 0xC1, 0x07));  // amber / gold

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
        private AudioFileReader? _reader;
        private WaveOutEvent? _listenPlayer;
        private AudioFileReader? _listenReader;
        private bool _isPlaying;

        /// <summary>Fired when the user clicks the inline delete (âœ•) or menu Delete.</summary>
        public event EventHandler? DeleteRequested;

        /// <summary>Fired when the user toggles the favorite (â­) button.</summary>
        public event EventHandler? FavoriteToggled;

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
            MenuRename_Click(sender, e);
        }

        private void TrimBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (Entry == null || !File.Exists(Entry.FilePath)) return;
            StopPlayback();

            var editor = new AudioEditorWindow(Entry.FilePath)
            {
                Owner = Window.GetWindow(this)
            };
            if (editor.ShowDialog() == true)
            {
                // Re-read duration from the trimmed file
                try
                {
                    using var reader = new AudioFileReader(Entry.FilePath);
                    Entry.Duration = reader.TotalTime;
                }
                catch { }
                SetEntry(Entry);
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
                _reader = new AudioFileReader(Entry.FilePath);
                _player = new WaveOutEvent { DeviceNumber = OutputDeviceIndex };
                _player.Init(_reader);
                _player.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _player.Play();

                if (ListenDeviceIndex >= -1)
                {
                    _listenReader = new AudioFileReader(Entry.FilePath);
                    _listenPlayer = new WaveOutEvent { DeviceNumber = ListenDeviceIndex };
                    _listenPlayer.Init(_listenReader);
                    _listenPlayer.Play();
                }

                SetPlayingVisual(true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "Paddy",
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
                _listenReader = new AudioFileReader(Entry.FilePath);
                _listenPlayer = new WaveOutEvent { DeviceNumber = ListenDeviceIndex };
                _listenPlayer.Init(_listenReader);
                _listenPlayer.PlaybackStopped += (_, _) => Dispatcher.Invoke(StopPlayback);
                _listenPlayer.Play();
                SetPlayingVisual(true);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "Paddy",
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

            try
            {
                var anim = (Storyboard)FindResource("PlayingAnimation");
                if (playing)
                    anim.Begin(TileBorder);
                else
                    anim.Stop(TileBorder);
            }
            catch { /* storyboard may not be running */ }

            if (!playing)
                TileBorder.BorderBrush = _isFavorite ? BrushFavorite : BrushNormal;
        }

        // â”€â”€ Context menu handlers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        private void MenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null) return;

            var dialog = new RenameDialog(Path.GetFileNameWithoutExtension(Entry.FilePath))
            {
                Owner = Window.GetWindow(this)
            };
            if (dialog.ShowDialog() != true) return;

            string newName = dialog.NewName.Trim();
            if (string.IsNullOrWhiteSpace(newName)) return;
            if (!newName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                newName += ".wav";

            string newPath = Path.Combine(Path.GetDirectoryName(Entry.FilePath)!, newName);
            try
            {
                StopPlayback();
                File.Move(Entry.FilePath, newPath);
                Entry.FilePath = newPath;
                SetEntry(Entry);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Rename failed:\n{ex.Message}", "Paddy",
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
