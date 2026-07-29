using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PaDDY.Views
{
    public class PadColorOption
    {
        public string Name { get; set; } = string.Empty;
        public string HexColor { get; set; } = string.Empty;
        public System.Windows.Media.Color Color => (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(HexColor);
    }

    public partial class PadColorPickerDialog : System.Windows.Window
    {
        public static readonly List<PadColorOption> DefaultPalette = new()
        {
            new PadColorOption { Name = "Default (Theme)", HexColor = "" },
            
            // Vibrant Neon & Synthwave
            new PadColorOption { Name = "Electric Blue", HexColor = "#FF00D2FF" },
            new PadColorOption { Name = "Neon Purple", HexColor = "#FFB000FF" },
            new PadColorOption { Name = "Hot Pink", HexColor = "#FFFF007F" },
            new PadColorOption { Name = "Cyber Red", HexColor = "#FFFF2A2A" },
            new PadColorOption { Name = "Acid Orange", HexColor = "#FFFF8800" },
            new PadColorOption { Name = "Solar Yellow", HexColor = "#FFFFD700" },
            new PadColorOption { Name = "Emerald Green", HexColor = "#FF00E676" },
            new PadColorOption { Name = "Mint Cyan", HexColor = "#FF00E5FF" },

            // Deep / Studio Tones
            new PadColorOption { Name = "Midnight Navy", HexColor = "#FF1A237E" },
            new PadColorOption { Name = "Deep Violet", HexColor = "#FF4A148C" },
            new PadColorOption { Name = "Crimson Velvet", HexColor = "#FF880E4F" },
            new PadColorOption { Name = "Warm Amber", HexColor = "#FFE65100" },
            new PadColorOption { Name = "Forest Shade", HexColor = "#FF1B5E20" },
            new PadColorOption { Name = "Teal Ocean", HexColor = "#FF004D40" },
            new PadColorOption { Name = "Slate Gray", HexColor = "#FF37474F" },
            new PadColorOption { Name = "Rich Indigo", HexColor = "#FF311B92" },

            // Pastel / Soft Beats
            new PadColorOption { Name = "Soft Lavender", HexColor = "#FFB388FF" },
            new PadColorOption { Name = "Peach Glow", HexColor = "#FFFFAB91" },
            new PadColorOption { Name = "Pastel Mint", HexColor = "#FFA7F3D0" },
            new PadColorOption { Name = "Sky Blue", HexColor = "#FF80D8FF" },
            new PadColorOption { Name = "Rose Gold", HexColor = "#FFFF80AB" },
            new PadColorOption { Name = "Lemon Chiffon", HexColor = "#FFFFF59D" }
        };

        public string SelectedHexColor { get; private set; } = string.Empty;

        public PadColorPickerDialog(string initialHexColor, string padName)
        {
            InitializeComponent();
            PadNameTitle.Text = string.IsNullOrWhiteSpace(padName) ? "Pad Color Customizer" : $"Color for \"{padName}\"";
            SelectedHexColor = initialHexColor ?? string.Empty;

            BuildPaletteGrid();
            CustomHexTextBox.Text = SelectedHexColor;
            UpdatePreview(SelectedHexColor);
        }

        private void InitializeComponent()
        {
            Title = "Choose Pad Color";
            Width = 440; // Default = 440
            Height = 450; // Default = 490
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = false; // default to true, this is causing problem with some themes, need to fix in future
            Background = System.Windows.Media.Brushes.Transparent;

            // Main Border Frame
            var outerBorder = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("WindowBgBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush"),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(20)
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Swatches
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Custom Hex input + Preview
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons

            // Header
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            PadNameTitle = new TextBlock
            {
                Text = "Pad Color Customizer",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(PadNameTitle, 0);

            var closeBtn = new System.Windows.Controls.Button
            {
                Content = new TextBlock
                {
                    Text = "\uE8BB", // Segoe MDL2 Assets close icon
                    FontSize = 10,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                },
                Width = 32,
                Height = 32,
                Cursor = System.Windows.Input.Cursors.Hand
            };

            if (TryFindResource("ChromeCloseBtn") is Style chromeCloseStyle)
            {
                closeBtn.Style = chromeCloseStyle;
            }
            else
            {
                closeBtn.Foreground = (System.Windows.Media.Brush)FindResource("SubtleTextBrush");
                closeBtn.Background = System.Windows.Media.Brushes.Transparent;
                closeBtn.BorderThickness = new Thickness(0);
            }

            closeBtn.Click += (s, e) => DialogResult = false;
            Grid.SetColumn(closeBtn, 1);

            headerGrid.Children.Add(PadNameTitle);
            headerGrid.Children.Add(closeBtn);
            Grid.SetRow(headerGrid, 0);
            mainGrid.Children.Add(headerGrid);

            // Swatches WrapPanel inside ScrollViewer
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 16)
            };

            SwatchesPanel = new WrapPanel
            {
                ItemWidth = 44,
                ItemHeight = 44,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            scrollViewer.Content = SwatchesPanel;
            Grid.SetRow(scrollViewer, 1);
            mainGrid.Children.Add(scrollViewer);

            // Custom Hex + Preview section
            var customSection = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("CardBgBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("DividerBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 16)
            };

            var customGrid = new Grid();
            customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Swatch Preview
            customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Textbox
            customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Apply button

            PreviewBorder = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1.5),
                BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush"),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            Grid.SetColumn(PreviewBorder, 0);

            var hexStack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var hexLabel = new TextBlock
            {
                Text = "CUSTOM HEX COLOR",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("SubtleTextBrush"),
                Margin = new Thickness(0, 0, 0, 2)
            };
            CustomHexTextBox = new System.Windows.Controls.TextBox
            {
                Height = 26,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Background = (System.Windows.Media.Brush)FindResource("InputBgBrush"),
                Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("InputBorderBrush"),
                Padding = new Thickness(6, 2, 6, 2),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center
            };
            CustomHexTextBox.TextChanged += CustomHexTextBox_TextChanged;
            hexStack.Children.Add(hexLabel);
            hexStack.Children.Add(CustomHexTextBox);
            Grid.SetColumn(hexStack, 1);

            customGrid.Children.Add(PreviewBorder);
            customGrid.Children.Add(hexStack);
            customSection.Child = customGrid;
            Grid.SetRow(customSection, 2);
            mainGrid.Children.Add(customSection);

            // Bottom Actions (Clear / Cancel / Save)
            var actionGrid = new Grid();
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var resetBtn = new System.Windows.Controls.Button
            {
                Content = "Reset to Default",
                Height = 32,
                Padding = new Thickness(12, 0, 12, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12
            };
            resetBtn.Click += (s, e) =>
            {
                SelectedHexColor = string.Empty;
                CustomHexTextBox.Text = string.Empty;
                UpdatePreview(string.Empty);
            };
            Grid.SetColumn(resetBtn, 0);

            var rightStack = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = (System.Windows.Media.Brush)FindResource("ButtonBgBrush"),
                Foreground = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush"),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12
            };
            cancelBtn.Click += (s, e) => DialogResult = false;

            var saveBtn = new System.Windows.Controls.Button
            {
                Content = "Apply",
                Width = 95,
                Height = 32,
                Background = (System.Windows.Media.Brush)FindResource("AccentGreenBrush"),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 12
            };
            saveBtn.Click += (s, e) =>
            {
                DialogResult = true;
            };

            rightStack.Children.Add(cancelBtn);
            rightStack.Children.Add(saveBtn);
            Grid.SetColumn(rightStack, 2);

            actionGrid.Children.Add(resetBtn);
            actionGrid.Children.Add(rightStack);
            Grid.SetRow(actionGrid, 3);
            mainGrid.Children.Add(actionGrid);

            outerBorder.Child = mainGrid;
            Content = outerBorder;

            // Make window draggable via header
            headerGrid.MouseLeftButtonDown += (s, e) => DragMove();
        }

        private TextBlock PadNameTitle = null!;
        private WrapPanel SwatchesPanel = null!;
        private Border PreviewBorder = null!;
        private System.Windows.Controls.TextBox CustomHexTextBox = null!;

        private void BuildPaletteGrid()
        {
            SwatchesPanel.Children.Clear();
            foreach (var opt in DefaultPalette)
            {
                var swatchBorder = new Border
                {
                    Width = 36,
                    Height = 36,
                    Margin = new Thickness(4),
                    ToolTip = opt.Name,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    CornerRadius = new CornerRadius(18), // Circular
                    BorderThickness = new Thickness(2),
                    DataContext = opt
                };

                if (string.IsNullOrEmpty(opt.HexColor))
                {
                    // Default tile design
                    swatchBorder.Background = (System.Windows.Media.Brush)FindResource("CardBgBrush");
                    swatchBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush");
                    swatchBorder.Child = new TextBlock
                    {
                        Text = "🚫",
                        FontSize = 14,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };
                }
                else
                {
                    var color = opt.Color;
                    swatchBorder.Background = new SolidColorBrush(color);
                    swatchBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 255, 255, 255));
                }

                string hex = opt.HexColor;
                swatchBorder.MouseLeftButtonDown += (s, e) =>
                {
                    SelectedHexColor = hex;
                    CustomHexTextBox.Text = hex;
                    UpdatePreview(hex);
                    HighlightSwatch(swatchBorder);
                };

                SwatchesPanel.Children.Add(swatchBorder);
            }
        }

        private void HighlightSwatch(Border selected)
        {
            foreach (UIElement child in SwatchesPanel.Children)
            {
                if (child is Border b)
                {
                    if (b == selected)
                        b.BorderBrush = (System.Windows.Media.Brush)FindResource("PrimaryTextBrush");
                    else
                    {
                        var opt = b.DataContext as PadColorOption;
                        if (opt != null && !string.IsNullOrEmpty(opt.HexColor))
                            b.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, 255, 255, 255));
                        else
                            b.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush");
                    }
                }
            }
        }

        private void CustomHexTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string hex = CustomHexTextBox.Text.Trim();
            if (hex.Length > 0 && !hex.StartsWith("#"))
                hex = "#" + hex;

            SelectedHexColor = hex;
            UpdatePreview(hex);
        }

        private void UpdatePreview(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
            {
                PreviewBorder.Background = (System.Windows.Media.Brush)FindResource("CardBgBrush");
                PreviewBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("CardBorderBrush");
            }
            else
            {
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    PreviewBorder.Background = new SolidColorBrush(col);
                    PreviewBorder.BorderBrush = System.Windows.Media.Brushes.White;
                }
                catch
                {
                    PreviewBorder.Background = System.Windows.Media.Brushes.Transparent;
                    PreviewBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentRedBrush");
                }
            }
        }
    }
}
