using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PaDDY.Services;

using WpfButton = System.Windows.Controls.Button;
using WpfMessageBox = System.Windows.MessageBox;
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfCursors = System.Windows.Input.Cursors;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace PaDDY.Views
{
    public class ImportItemModel
    {
        public string FilePath { get; set; } = string.Empty;
        public AudioSourceInfo SourceInfo { get; set; } = new();
        public string DisplayName { get; set; } = string.Empty;
        public string TargetCodec { get; set; } = "wav";
        public string PadColor { get; set; } = string.Empty;
    }

    public partial class AudioImportWindow : Window
    {
        private readonly List<ImportItemModel> _items = new();
        private int _currentIndex = 0;
        private bool _isUpdatingUi = false;
        private CancellationTokenSource? _cts;
        private Border? _selectedSwatchBorder;

        public List<AudioImportResult> ConvertedResults { get; } = new();

        public AudioImportWindow(IEnumerable<string> filePaths)
        {
            InitializeComponent();

            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                var info = AudioImportService.GetSourceAudioInfo(path);
                
                string detectedExt = info.SourceExtension.ToLowerInvariant();
                string initialCodec = "wav";
                if (detectedExt == "mp3" || detectedExt == "flac" || detectedExt == "ogg" || detectedExt == "opus" || detectedExt == "aac" || detectedExt == "m4a")
                {
                    initialCodec = detectedExt == "m4a" ? "aac" : detectedExt;
                }

                _items.Add(new ImportItemModel
                {
                    FilePath = path,
                    SourceInfo = info,
                    DisplayName = info.SuggestedName,
                    TargetCodec = initialCodec,
                    PadColor = string.Empty
                });
            }

            if (_items.Count == 0)
            {
                Loaded += (s, e) => Close();
                return;
            }

            BuildPaletteSwatches();
            SetupMultiFileUi();
            LoadItemToUi(_currentIndex);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void SetupMultiFileUi()
        {
            if (_items.Count > 1)
            {
                MultiFileNavCard.Visibility = Visibility.Visible;
                FileCountBadge.Visibility = Visibility.Visible;
                ApplyAllCheckBox.Visibility = Visibility.Visible;
                UpdateNavButtons();
            }
            else
            {
                MultiFileNavCard.Visibility = Visibility.Collapsed;
                FileCountBadge.Visibility = Visibility.Collapsed;
                ApplyAllCheckBox.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateNavButtons()
        {
            FileCountBadge.Text = $"{_currentIndex + 1} of {_items.Count}";
            MultiFileStatusText.Text = $"File {_currentIndex + 1} of {_items.Count}";
            PrevFileButton.IsEnabled = _currentIndex > 0;
            NextFileButton.IsEnabled = _currentIndex < _items.Count - 1;
        }

        private void LoadItemToUi(int index)
        {
            if (index < 0 || index >= _items.Count) return;
            _isUpdatingUi = true;

            var item = _items[index];

            // Source Info
            SourceFileNameText.Text = item.SourceInfo.FileName;
            SourceFilePathText.Text = item.SourceInfo.FilePath;
            SourceFormatTag.Text = string.IsNullOrWhiteSpace(item.SourceInfo.DetectedFormat) ? "AUDIO" : item.SourceInfo.DetectedFormat;
            SourceDurationText.Text = item.SourceInfo.DurationFormatted;
            SourceChannelsText.Text = item.SourceInfo.Channels == 1 ? "Mono" : item.SourceInfo.Channels == 2 ? "Stereo" : $"{item.SourceInfo.Channels} ch";
            SourceFileSizeText.Text = item.SourceInfo.FileSizeFormatted;

            // Pad Name
            PadNameTextBox.Text = item.DisplayName;

            // Target Audio Format
            SelectComboBoxCodec(item.TargetCodec);

            // Pad Color
            CustomHexTextBox.Text = item.PadColor;
            UpdatePadPreview(item.PadColor);
            HighlightSwatchByHex(item.PadColor);

            UpdateNavButtons();
            _isUpdatingUi = false;
        }

        private void SaveCurrentUiToItem()
        {
            if (_currentIndex < 0 || _currentIndex >= _items.Count) return;
            var item = _items[_currentIndex];
            item.DisplayName = PadNameTextBox.Text.Trim();
            if (AudioFormatComboBox.SelectedItem is ComboBoxItem selectedCbi && selectedCbi.Tag is string codecTag)
            {
                item.TargetCodec = codecTag;
            }
            item.PadColor = CustomHexTextBox.Text.Trim();
        }

        private void SelectComboBoxCodec(string codec)
        {
            string target = codec.ToLowerInvariant();
            if (target == "m4a") target = "aac";

            foreach (var item in AudioFormatComboBox.Items)
            {
                if (item is ComboBoxItem cbi && cbi.Tag is string tag && tag.Equals(target, StringComparison.OrdinalIgnoreCase))
                {
                    AudioFormatComboBox.SelectedItem = cbi;
                    return;
                }
            }

            if (AudioFormatComboBox.Items.Count > 0)
            {
                AudioFormatComboBox.SelectedIndex = 0;
            }
        }

        private void BuildPaletteSwatches()
        {
            ColorSwatchesPanel.Children.Clear();

            foreach (var opt in PadColorPickerDialog.DefaultPalette)
            {
                var swatchBorder = new Border
                {
                    Width = 32,
                    Height = 32,
                    Margin = new Thickness(4),
                    ToolTip = opt.Name,
                    Cursor = WpfCursors.Hand,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(2),
                    Tag = opt.HexColor
                };

                if (string.IsNullOrEmpty(opt.HexColor))
                {
                    swatchBorder.Background = (WpfBrush)FindResource("CardBgBrush");
                    swatchBorder.BorderBrush = (WpfBrush)FindResource("CardBorderBrush");
                    swatchBorder.Child = new TextBlock
                    {
                        Text = "∅",
                        FontSize = 13,
                        FontWeight = FontWeights.Bold,
                        Foreground = (WpfBrush)FindResource("SecondaryTextBrush"),
                        HorizontalAlignment = WpfHorizontalAlignment.Center,
                        VerticalAlignment = WpfVerticalAlignment.Center
                    };
                }
                else
                {
                    try
                    {
                        var color = (WpfColor)WpfColorConverter.ConvertFromString(opt.HexColor);
                        swatchBorder.Background = new SolidColorBrush(color);
                        swatchBorder.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x50, 255, 255, 255));
                    }
                    catch
                    {
                        swatchBorder.Background = (WpfBrush)FindResource("CardBgBrush");
                        swatchBorder.BorderBrush = (WpfBrush)FindResource("CardBorderBrush");
                    }
                }

                string hex = opt.HexColor;
                swatchBorder.MouseLeftButtonDown += (s, e) =>
                {
                    CustomHexTextBox.Text = hex;
                    UpdatePadPreview(hex);
                    HighlightSwatch(swatchBorder);
                    SaveCurrentUiToItem();
                };

                ColorSwatchesPanel.Children.Add(swatchBorder);
            }
        }

        private void HighlightSwatch(Border? selected)
        {
            _selectedSwatchBorder = selected;
            foreach (UIElement child in ColorSwatchesPanel.Children)
            {
                if (child is Border b)
                {
                    if (b == selected)
                    {
                        b.BorderBrush = (WpfBrush)FindResource("PrimaryTextBrush");
                        b.BorderThickness = new Thickness(2.5);
                    }
                    else
                    {
                        string hex = b.Tag as string ?? string.Empty;
                        b.BorderBrush = string.IsNullOrEmpty(hex)
                            ? (WpfBrush)FindResource("CardBorderBrush")
                            : new SolidColorBrush(WpfColor.FromArgb(0x50, 255, 255, 255));
                        b.BorderThickness = new Thickness(2);
                    }
                }
            }
        }

        private void HighlightSwatchByHex(string hex)
        {
            Border? matched = null;
            foreach (UIElement child in ColorSwatchesPanel.Children)
            {
                if (child is Border b && string.Equals(b.Tag as string, hex, StringComparison.OrdinalIgnoreCase))
                {
                    matched = b;
                    break;
                }
            }
            HighlightSwatch(matched);
        }

        private void UpdatePadPreview(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                PadPreviewBorder.Background = (WpfBrush)FindResource("CardBgBrush");
                PadPreviewBorder.BorderBrush = (WpfBrush)FindResource("CardBorderBrush");
                PadPreviewLabel.Foreground = (WpfBrush)FindResource("PrimaryTextBrush");
                return;
            }

            try
            {
                var color = (WpfColor)WpfColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(color);
                PadPreviewBorder.Background = new SolidColorBrush(WpfColor.FromArgb(50, color.R, color.G, color.B));
                PadPreviewBorder.BorderBrush = brush;
                PadPreviewLabel.Foreground = brush;
            }
            catch
            {
                PadPreviewBorder.Background = (WpfBrush)FindResource("CardBgBrush");
                PadPreviewBorder.BorderBrush = (WpfBrush)FindResource("CardBorderBrush");
                PadPreviewLabel.Foreground = (WpfBrush)FindResource("PrimaryTextBrush");
            }
        }

        private void PadNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            SaveCurrentUiToItem();
        }

        private void CustomHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingUi) return;
            string hex = CustomHexTextBox.Text.Trim();
            UpdatePadPreview(hex);
            HighlightSwatchByHex(hex);
            SaveCurrentUiToItem();
        }

        private void AudioFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AudioFormatComboBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string codec)
            {
                FormatDescText.Text = codec switch
                {
                    "wav" => "Lossless uncompressed PCM standard for zero-latency instant playback",
                    "mp3" => "Standard compressed MP3 format with high compatibility",
                    "flac" => "Lossless compressed FLAC format for studio archiving",
                    "ogg" => "Ogg Vorbis compressed audio format",
                    "opus" => "Ultra low-latency speech and music codec",
                    "aac" => "AAC / M4A high-efficiency audio container",
                    _ => "Audio format"
                };

                if (!_isUpdatingUi)
                {
                    SaveCurrentUiToItem();
                }
            }
        }

        private void PrevFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex > 0)
            {
                SaveCurrentUiToItem();
                _currentIndex--;
                LoadItemToUi(_currentIndex);
            }
        }

        private void NextFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < _items.Count - 1)
            {
                SaveCurrentUiToItem();
                _currentIndex++;
                LoadItemToUi(_currentIndex);
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentUiToItem();

            if (ApplyAllCheckBox.IsChecked == true && _items.Count > 1)
            {
                var current = _items[_currentIndex];
                foreach (var item in _items)
                {
                    item.TargetCodec = current.TargetCodec;
                    item.PadColor = current.PadColor;
                }
            }

            // Lock UI controls during conversion
            SaveButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            if (MultiFileNavCard.Visibility == Visibility.Visible) MultiFileNavCard.IsEnabled = false;
            PadNameTextBox.IsEnabled = false;
            AudioFormatComboBox.IsEnabled = false;
            ColorSwatchesPanel.IsEnabled = false;
            CustomHexTextBox.IsEnabled = false;

            ProgressCard.Visibility = Visibility.Visible;
            _cts = new CancellationTokenSource();

            ConvertedResults.Clear();
            int total = _items.Count;
            int currentStep = 0;

            foreach (var item in _items)
            {
                currentStep++;
                string displayLabel = string.IsNullOrWhiteSpace(item.DisplayName)
                    ? Path.GetFileNameWithoutExtension(item.FilePath)
                    : item.DisplayName;

                ProgressStatusText.Text = total > 1
                    ? $"Converting ({currentStep}/{total}): {displayLabel} -> {item.TargetCodec.ToUpperInvariant()}..."
                    : $"Converting {displayLabel} -> {item.TargetCodec.ToUpperInvariant()}...";

                ConversionProgressBar.Value = 0;
                ProgressPercentText.Text = "0%";

                var progress = new Progress<double>(percent =>
                {
                    ConversionProgressBar.Value = percent;
                    ProgressPercentText.Text = $"{percent:F0}%";
                });

                var result = await AudioImportService.ConvertAndImportAsync(item.FilePath, item.TargetCodec, progress, _cts.Token);
                if (result.Success)
                {
                    result.DisplayName = displayLabel;
                    result.PadColor = item.PadColor;
                    ConvertedResults.Add(result);
                }
                else
                {
                    WpfMessageBox.Show(
                        $"Failed to import \"{displayLabel}\": {result.ErrorMessage}",
                        "Import Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            ProgressStatusText.Text = "Import complete!";
            ConversionProgressBar.Value = 100;
            ProgressPercentText.Text = "100%";

            await Task.Delay(200);

            DialogResult = ConvertedResults.Count > 0;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            DialogResult = false;
            Close();
        }
    }
}
