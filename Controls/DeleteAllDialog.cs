using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace PaDDY.Controls
{
    /// <summary>
    /// Modal dialog asking whether to delete all recordings.
    /// </summary>
    internal class DeleteAllDialog : Window
    {
        public bool KeepFavorites { get; private set; } = false;

        public DeleteAllDialog()
        {
            Title = "Delete All Recordings";
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ResolveBrush("WindowGlowBrush", new SolidColorBrush(Color.FromRgb(0x1A, 0x20, 0x30)));
            Foreground = ResolveBrush("PrimaryTextBrush", Brushes.WhiteSmoke);
            ShowInTaskbar = false;

            var frame = new Border
            {
                Margin = new Thickness(10),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(12),
                Background = ResolveBrush("SecondaryWindowBackgroundBrush", new SolidColorBrush(Color.FromRgb(0x17, 0x18, 0x27))),
                BorderBrush = ResolveBrush("WindowEdgeBrush", new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(1),
                Effect = ResolveEffect("SecondaryWindowShadow")
            };

            var panel = new StackPanel();
            frame.Child = panel;
            Content = frame;

            panel.Children.Add(new TextBlock
            {
                Text = "Delete All Recordings",
                Foreground = ResolveBrush("SecondaryTextBrush", new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xCC))),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 10)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "Delete all recordings from disk?",
                Foreground = ResolveBrush("PrimaryTextBrush", Brushes.White),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var btnStyle = new Style(typeof(Button));
            btnStyle.Setters.Add(new Setter(Button.BackgroundProperty, ResolveBrush("CardBgBrush", new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x2C)))));
            btnStyle.Setters.Add(new Setter(Button.ForegroundProperty, ResolveBrush("PrimaryTextBrush", Brushes.White)));
            btnStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 6, 10, 6)));
            btnStyle.Setters.Add(new Setter(Button.BorderBrushProperty, ResolveBrush("InputBorderBrush", new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)))));
            btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            btnStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            panel.Children.Add(row);

            var keepBtn = new Button
            {
                Content = "Keep Favorites",
                Style = btnStyle,
                Margin = new Thickness(0, 0, 8, 0)
            };
            keepBtn.Click += (_, _) => { KeepFavorites = true; DialogResult = true; };

            var deleteBtn = new Button
            {
                Content = "Delete All",
                Style = btnStyle,
                Margin = new Thickness(0, 0, 8, 0),
                Foreground = ResolveBrush("AccentRedBrush", new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)))
            };
            deleteBtn.Click += (_, _) => { KeepFavorites = false; DialogResult = true; };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Style = btnStyle,
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => { DialogResult = false; };

            row.Children.Add(keepBtn);
            row.Children.Add(deleteBtn);
            row.Children.Add(cancelBtn);
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
