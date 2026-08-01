using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;

namespace PaDDY.Controls
{
    /// <summary>
    /// Simple styled PaDDY informational dialog with a single OK button.
    /// </summary>
    internal class InfoDialog : Window
    {
        public InfoDialog(string title, string message)
        {
            Title = title;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = ResolveBrush("WindowGlowBrush", new SolidColorBrush(Color.FromRgb(0x1A, 0x20, 0x30)));
            Foreground = ResolveBrush("PrimaryTextBrush", Brushes.WhiteSmoke);
            ShowInTaskbar = false;
            WindowStyle = WindowStyle.None;

            var chrome = new System.Windows.Shell.WindowChrome
            {
                CaptionHeight = 36,
                GlassFrameThickness = new Thickness(5),
                ResizeBorderThickness = new Thickness(10),
                UseAeroCaptionButtons = false
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = root;

            var titleBar = new Border
            {
                Background = ResolveBrush("TitleBarGradient", new SolidColorBrush(Color.FromRgb(0x1A, 0x20, 0x30))),
                BorderBrush = ResolveBrush("DividerBrush", new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(12, 12, 0, 0)
            };
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);

            var titleGrid = new Grid();
            titleBar.Child = titleGrid;
            titleGrid.Children.Add(new TextBlock
            {
                Text = title,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResolveBrush("PrimaryTextBrush", Brushes.White)
            });

            var closeBtn = new Button
            {
                Style = (Style)(FindResource("ChromeCloseBtn") ?? new Style(typeof(Button))),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                ToolTip = "Close"
            };
            closeBtn.Click += (_, _) => DialogResult = false;
            titleGrid.Children.Add(closeBtn);

            var body = new Border
            {
                Margin = new Thickness(10, 0, 10, 10),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(0, 0, 12, 12),
                Background = ResolveBrush("SecondaryWindowBackgroundBrush", new SolidColorBrush(Color.FromRgb(0x17, 0x18, 0x27))),
                BorderBrush = ResolveBrush("WindowEdgeBrush", new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(1),
                Effect = ResolveEffect("SecondaryWindowShadow")
            };
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var panel = new StackPanel();
            body.Child = panel;

            panel.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = ResolveBrush("PrimaryTextBrush", Brushes.White),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var btnStyle = new Style(typeof(Button));
            btnStyle.Setters.Add(new Setter(Button.BackgroundProperty, ResolveBrush("CardBgBrush", new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x2C)))));
            btnStyle.Setters.Add(new Setter(Button.ForegroundProperty, ResolveBrush("PrimaryTextBrush", Brushes.White)));
            btnStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(16, 6, 16, 6)));
            btnStyle.Setters.Add(new Setter(Button.BorderBrushProperty, ResolveBrush("InputBorderBrush", new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)))));
            btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            btnStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));

            var okBtn = new Button
            {
                Content = "OK",
                Style = btnStyle,
                IsDefault = true,
                IsCancel = true,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            okBtn.Click += (_, _) => DialogResult = false;
            panel.Children.Add(okBtn);
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

        public bool? ShowDialog(Window owner)
        {
            Owner = owner;
            return ShowDialog();
        }
    }
}
