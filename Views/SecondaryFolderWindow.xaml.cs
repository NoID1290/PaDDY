using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PaDDY.Controls;
using PaDDY.Models;
using PaDDY.Services;
using NoIDSoftwork.AudioProcessor;

namespace PaDDY.Views
{
    [SupportedOSPlatform("windows")]
    public partial class SecondaryFolderWindow : Window
    {
        public PadPage Page { get; }
        private readonly RecordingStore _store;
        private readonly AppSettings _settings;
        private readonly int _outputDeviceIndex;
        private readonly int _listenDeviceIndex;
        private readonly float _outputVolume;
        private readonly float _listenVolume;
        private readonly Action? _onDataChanged;

        public SecondaryFolderWindow(
            PadPage page,
            RecordingStore store,
            AppSettings settings,
            int outputDeviceIndex,
            int listenDeviceIndex,
            float outputVolume,
            float listenVolume,
            Action? onDataChanged = null)
        {
            InitializeComponent();
            Page = page ?? throw new ArgumentNullException(nameof(page));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _outputDeviceIndex = outputDeviceIndex;
            _listenDeviceIndex = listenDeviceIndex;
            _outputVolume = outputVolume;
            _listenVolume = listenVolume;
            _onDataChanged = onDataChanged;

            RefreshPageTitle();
            FolderIcon.Text = Page.IsFavorites ? "★" : "📁";

            RefreshPads();
        }

        public void RefreshPageTitle()
        {
            Title = Page.IsFavorites ? "★ " + Page.Name : Page.Name;
            TitleText.Text = Page.IsFavorites ? "★ " + Page.Name : Page.Name;
        }

        public void RefreshPads()
        {
            FolderPadsPanel.Children.Clear();

            var allRecords = _store.GetAll();
            var allEntries = new List<RecordingEntry>();

            foreach (var rec in allRecords)
            {
                try
                {
                    string tempPath = _store.MaterializeToTemp(rec.Id, rec.Codec);
                    var entry = new RecordingEntry
                    {
                        RecordingId = rec.Id,
                        FilePath = tempPath,
                        DisplayName = rec.DisplayName,
                        Duration = TimeSpan.FromMilliseconds(rec.DurationMs),
                        CreatedAt = rec.CreatedAt,
                        IsFavorite = rec.IsFavorite,
                        PadPage = rec.PadPage,
                        SortOrder = rec.SortOrder,
                        IsNonDestructive = rec.IsNonDestructive,
                        TrimStartMs = rec.TrimStartMs,
                        TrimEndMs = rec.TrimEndMs,
                        GainDb = rec.GainDb,
                        PadColor = rec.PadColor
                    };
                    allEntries.Add(entry);
                }
                catch { }
            }

            List<RecordingEntry> matchingEntries;

            if (Page.IsFavorites)
            {
                matchingEntries = allEntries.Where(e => e.IsFavorite && (string.IsNullOrEmpty(e.PadPage) || e.PadPage == Page.Id)).ToList();
            }
            else
            {
                matchingEntries = allEntries.Where(e => e.PadPage == Page.Id).ToList();
            }

            string filter = SearchTextBox?.Text?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!string.IsNullOrEmpty(filter))
            {
                matchingEntries = matchingEntries
                    .Where(e => (e.DisplayName ?? string.Empty).ToLowerInvariant().Contains(filter))
                    .ToList();
            }

            ItemCountLabel.Text = $"— {matchingEntries.Count} items";

            RecordingPadButton.SuppressEntranceAnimation++;
            foreach (var entry in matchingEntries)
            {
                var btn = CreatePadButton(entry);
                FolderPadsPanel.Children.Add(btn);
            }
            RecordingPadButton.SuppressEntranceAnimation--;
        }

        private RecordingPadButton CreatePadButton(RecordingEntry entry)
        {
            var btn = new RecordingPadButton
            {
                Margin = new Thickness(6),
                OutputDeviceIndex = _outputDeviceIndex,
                ListenDeviceIndex = _listenDeviceIndex,
                TrimEditorOutputDeviceIndex = _settings.TrimEditorOutputDeviceIndex,
                OutputVolume = _outputVolume,
                ListenVolume = _listenVolume,
                GlobalFadeEnabled = _settings.GlobalFadeEnabled,
                GlobalFadeInDurationMs = _settings.GlobalFadeInDurationMs,
                GlobalFadeOutDurationMs = _settings.GlobalFadeOutDurationMs
            };
            btn.SetEntry(entry);
            btn.IsFavorite = entry.IsFavorite;

            btn.DeleteRequested += (s, e) =>
            {
                if (s is RecordingPadButton b && b.Entry != null && !string.IsNullOrEmpty(b.Entry.RecordingId))
                {
                    _store.Delete(b.Entry.RecordingId);
                    FolderPadsPanel.Children.Remove(b);
                    ItemCountLabel.Text = $"— {FolderPadsPanel.Children.Count} items";
                    _onDataChanged?.Invoke();
                }
            };

            btn.RecordingRenamed += (e, newDisplayName) =>
            {
                if (string.IsNullOrEmpty(e.RecordingId)) return;
                _store.SetDisplayName(e.RecordingId, newDisplayName);
                _onDataChanged?.Invoke();
            };

            btn.PadColorChanged += (e, newHexColor) =>
            {
                if (string.IsNullOrEmpty(e.RecordingId)) return;
                _store.SetPadColor(e.RecordingId, newHexColor);
                _onDataChanged?.Invoke();
            };

            btn.FavoriteToggled += (s, _) =>
            {
                if (s is not RecordingPadButton b || b.Entry == null || string.IsNullOrEmpty(b.Entry.RecordingId)) return;
                bool newFav = b.IsFavorite;
                _store.SetFavorite(b.Entry.RecordingId, newFav);
                if (!newFav)
                {
                    b.Entry.IsFavorite = false;
                    b.Entry.PadPage = string.Empty;
                    _store.SetPadPage(b.Entry.RecordingId, string.Empty);
                    FolderPadsPanel.Children.Remove(b);
                    ItemCountLabel.Text = $"— {FolderPadsPanel.Children.Count} items";
                }
                _onDataChanged?.Invoke();
            };

            btn.RecordingEdited += (e) =>
            {
                if (string.IsNullOrEmpty(e.RecordingId) || !File.Exists(e.FilePath)) return;
                try
                {
                    if (e.IsNonDestructive)
                    {
                        _store.UpdateNonDestructiveSettings(
                            e.RecordingId,
                            true,
                            e.TrimStartMs,
                            e.TrimEndMs,
                            e.GainDb,
                            (long)e.Duration.TotalMilliseconds
                        );
                    }
                    else
                    {
                        byte[] updated = File.ReadAllBytes(e.FilePath);
                        _store.UpdateAudioData(e.RecordingId, updated);
                        _store.UpdateNonDestructiveSettings(
                            e.RecordingId,
                            false,
                            0,
                            0,
                            0.0,
                            (long)e.Duration.TotalMilliseconds
                        );
                    }
                }
                catch { }
                _onDataChanged?.Invoke();
            };

            btn.RecordingCopied += (copyPath, asFav) =>
            {
                if (!File.Exists(copyPath)) return;
                try
                {
                    TimeSpan duration;
                    using (var reader = AudioReaderFactory.Open(copyPath))
                        duration = reader.TotalTime;

                    byte[] audioBytes = File.ReadAllBytes(copyPath);
                    try { File.Delete(copyPath); } catch { }

                    string codec = Path.GetExtension(copyPath).TrimStart('.');
                    string displayName = RecordingNameGenerator.BuildDisplayName(_settings, DateTime.Now, codec);
                    string targetPage = Page.IsFavorites ? string.Empty : Page.Id;
                    var newEntry = new RecordingEntry
                    {
                        DisplayName = displayName,
                        Duration = duration,
                        CreatedAt = DateTime.Now,
                        IsFavorite = true,
                        PadPage = targetPage
                    };
                    string id = _store.Add(displayName, codec, newEntry.Duration, newEntry.CreatedAt, audioBytes);
                    newEntry.RecordingId = id;
                    newEntry.FilePath = _store.MaterializeToTemp(id, codec);
                    _store.SetFavorite(id, true);
                    _store.SetPadPage(id, targetPage);

                    RefreshPads();
                    _onDataChanged?.Invoke();
                }
                catch { }
            };

            return btn;
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void ChromeMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(SearchTextBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            ClearSearchBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            RefreshPads();
        }

        private void ClearSearchBtn_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = string.Empty;
        }

        private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            if (RecordingPadButton.GetDraggedPad(e) != null || e.Data.GetDataPresent(RecordingPadButton.PadDragFormat))
            {
                e.Effects = System.Windows.DragDropEffects.Move;
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
                {
                    if (files.Any(f => AudioImportService.IsSupportedExtensionOrDirectory(f)))
                    {
                        e.Effects = System.Windows.DragDropEffects.Copy;
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
        {
            var pad = RecordingPadButton.GetDraggedPad(e);
            if (pad != null && pad.Entry != null)
            {
                string targetPage = Page.IsFavorites ? string.Empty : Page.Id;
                _store.SetFavorite(pad.Entry.RecordingId, true);
                _store.SetPadPage(pad.Entry.RecordingId, targetPage);

                pad.Entry.IsFavorite = true;
                pad.IsFavorite = true;
                pad.Entry.PadPage = targetPage;

                // Remove pad from its current visual parent panel if present
                (pad.Parent as System.Windows.Controls.Panel)?.Children.Remove(pad);

                _onDataChanged?.Invoke();
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    var supportedFiles = AudioImportService.ExpandAudioFiles(files);
                    if (supportedFiles.Count > 0)
                    {
                        e.Handled = true;

                        var importWindow = new AudioImportWindow(supportedFiles)
                        {
                            Owner = this
                        };

                        bool? dialogResult = importWindow.ShowDialog();
                        if (dialogResult == true && importWindow.ConvertedResults.Count > 0)
                        {
                            foreach (var result in importWindow.ConvertedResults)
                            {
                                if (result.Success && result.AudioData.Length > 0)
                                {
                                    string id = _store.Add(result.DisplayName, result.Codec, result.Duration, DateTime.Now, result.AudioData);
                                    if (!string.IsNullOrEmpty(result.PadColor))
                                    {
                                        _store.SetPadColor(id, result.PadColor);
                                    }
                                    string targetPage = Page.IsFavorites ? string.Empty : Page.Id;
                                    _store.SetFavorite(id, true);
                                    _store.SetPadPage(id, targetPage);
                                }
                            }
                            _onDataChanged?.Invoke();
                            RefreshPads();
                        }
                    }
                }
            }
        }
    }
}
