using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;

namespace PaDDY.Controls
{
    public sealed class RenameDialog : Window
    {
        private readonly System.Windows.Controls.TextBox _textBox;
        public string NewName => _textBox.Text;

        public RenameDialog(string currentName)
        {
            Title = "Rename Recording";
            Width = 360;
            Height = 158;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Icon = BitmapFrame.Create(new Uri("pack://application:,,,/PaDDY.ico", UriKind.Absolute));
            Background = ResolveBrush("WindowGlowBrush", new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x20, 0x30)));
            Foreground = ResolveBrush("PrimaryTextBrush", WpfBrushes.WhiteSmoke);
            ShowInTaskbar = false;

            var frame = new Border
            {
                Margin = new Thickness(10),
                Padding = new Thickness(14),
                CornerRadius = new CornerRadius(12),
                Background = ResolveBrush("SecondaryWindowBackgroundBrush", new SolidColorBrush(WpfColor.FromRgb(0x17, 0x18, 0x27))),
                BorderBrush = ResolveBrush("WindowEdgeBrush", new SolidColorBrush(WpfColor.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(1),
                Effect = ResolveEffect("SecondaryWindowShadow")
            };

            var titleText = new TextBlock
            {
                Text = "Rename Recording",
                Foreground = ResolveBrush("SecondaryTextBrush", new SolidColorBrush(WpfColor.FromRgb(0xB0, 0xB0, 0xCC))),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _textBox = new TextBox
            {
                Text = currentName,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(8, 5, 8, 5),
                Background = ResolveBrush("InputBgBrush", new SolidColorBrush(WpfColor.FromRgb(0x14, 0x14, 0x20))),
                Foreground = ResolveBrush("PrimaryTextBrush", WpfBrushes.White),
                BorderBrush = ResolveBrush("InputBorderBrush", new SolidColorBrush(WpfColor.FromArgb(0x2A, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(1),
                FontSize = 13
            };
            _textBox.SelectAll();
            Loaded += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };

            var ok = new Button
            {
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Background = new SolidColorBrush(WpfColor.FromRgb(0x1A, 0x2E, 0x1A)),
                BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x40, 0x4C, 0xAF, 0x50)),
                Foreground = new SolidColorBrush(WpfColor.FromRgb(0xAA, 0xD4, 0xAA)),
                IsDefault = true
            };
            ok.Click += (_, _) => { DialogResult = true; };

            var cancel = new Button
            {
                Content = "Cancel",
                Width = 75,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                IsCancel = true
            };

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);

            var root = new StackPanel();
            root.Children.Add(titleText);
            root.Children.Add(_textBox);
            root.Children.Add(btnPanel);
            frame.Child = root;

            Content = frame;
        }

        private System.Windows.Media.Brush ResolveBrush(string key, System.Windows.Media.Brush fallback)
            => TryFindResource(key) as System.Windows.Media.Brush ?? fallback;

        private Effect ResolveEffect(string key)
        {
            if (TryFindResource(key) is Effect effect)
                return effect.Clone();

            return new DropShadowEffect
            {
                ShadowDepth = 0,
                BlurRadius = 24,
                Color = Colors.Black,
                Opacity = 0.5
            };
        }
    }
}
