using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using WpfControl = System.Windows.Controls.UserControl;
using System.Windows.Media.Animation;
using NAudio.Wave;
using Paddy.Models;

namespace Paddy.Controls
{
    public partial class RecordingPadButton : WpfControl
    {
        public RecordingEntry? Entry { get; private set; }

        // Output device index injected from MainWindow
        public int OutputDeviceIndex { get; set; } = 0;

        // Listen device index: -2 = disabled, -1 = default, 0..N = specific device
        public int ListenDeviceIndex { get; set; } = -2;

        private WaveOutEvent? _player;
        private AudioFileReader? _reader;
        private WaveOutEvent? _listenPlayer;
        private AudioFileReader? _listenReader;
        private bool _isPlaying;

        public event EventHandler? DeleteRequested;

        public RecordingPadButton()
        {
            InitializeComponent();
            MouseLeftButtonUp += (_, _) => TogglePlay();
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

        // ── Playback ──────────────────────────────────────────────────────────
        public void TogglePlay()
        {
            if (_isPlaying)
                StopPlayback();
            else
                StartPlayback();
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
                _player.PlaybackStopped += (_, _) =>
                {
                    Dispatcher.Invoke(StopPlayback);
                };
                _player.Play();

                // Also play on the listen device if enabled
                if (ListenDeviceIndex >= -1)
                {
                    _listenReader = new AudioFileReader(Entry.FilePath);
                    _listenPlayer = new WaveOutEvent { DeviceNumber = ListenDeviceIndex };
                    _listenPlayer.Init(_listenReader);
                    _listenPlayer.Play();
                }

                _isPlaying = true;
                IconText.Text = "⏹";

                var anim = (Storyboard)FindResource("PlayingAnimation");
                anim.Begin(TileBorder);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Playback error:\n{ex.Message}", "Paddy",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                StopPlayback();
            }
        }

        private void StopPlayback()
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
            _isPlaying = false;
            IconText.Text = "🎤";

            try
            {
                var anim = (Storyboard)FindResource("PlayingAnimation");
                anim.Stop(TileBorder);
            }
            catch { /* storyboard may not be running */ }

            // Reset border colour
            TileBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
        }

        // ── Context menu handlers ─────────────────────────────────────────────
        private void MenuPlay_Click(object sender, RoutedEventArgs e) => StartPlayback();
        private void MenuStop_Click(object sender, RoutedEventArgs e) => StopPlayback();

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

        private void MenuOpenExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null) return;
            if (File.Exists(Entry.FilePath))
                Process.Start("explorer.exe", $"/select,\"{Entry.FilePath}\"");
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (Entry == null) return;
            var result = System.Windows.MessageBox.Show(
                $"Delete \"{Path.GetFileName(Entry.FilePath)}\"?",
                "Paddy", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes) return;

            StopPlayback();
            try { File.Delete(Entry.FilePath); } catch { }
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
