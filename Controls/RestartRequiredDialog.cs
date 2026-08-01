using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Orientation = System.Windows.Controls.Orientation;

namespace PaDDY.Controls
{
    /// <summary>
    /// PaDDY-styled dialog shown when a file is locked.
    /// Result: DeleteOnRestart, RestartNow, Cancel.
    /// </summary>
    internal class RestartRequiredDialog : Window
    {
        public enum RestartAction
        {
            DeleteOnRestart,
            RestartNow,
            Cancel
        }

        public RestartAction Action { get; private set; } = RestartAction.Cancel;

        public RestartRequiredDialog(string title, string message)
        {
            Title = title;
            Width = 400;
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

            var root = new Grid { Margin = new Thickness(8) };
            Content = root;

            var outerFrame = new Border
            {
                Background = ResolveBrush("SecondaryWindowBackgroundBrush", new SolidColorBrush(Color.FromRgb(0x17, 0x18, 0x27))),
                BorderBrush = ResolveBrush("WindowEdgeBrush", new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)(FindResource("SecondaryWindowCornerRadius") ?? new CornerRadius(12)),
                Effect = ResolveEffect("SecondaryWindowShadow"),
                ClipToBounds = true
            };
            root.Children.Add(outerFrame);

            var innerGrid = new Grid();
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            innerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            outerFrame.Child = innerGrid;

            var titleBar = new Border
            {
                Background = ResolveBrush("TitleBarGradient", new SolidColorBrush(Color.FromRgb(0x1A, 0x20, 0x30))),
                BorderBrush = ResolveBrush("DividerBrush", new SolidColorBrush(Color.FromArgb(0x2C, 0xFF, 0xFF, 0xFF))),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = (CornerRadius)(FindResource("TopWindowCornerRadius") ?? new CornerRadius(12, 12, 0, 0))
            };
            Grid.SetRow(titleBar, 0);
            innerGrid.Children.Add(titleBar);

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
            closeBtn.Click += (_, _) => { Action = RestartAction.Cancel; DialogResult = false; };
            titleGrid.Children.Add(closeBtn);

            var body = new Border
            {
                Padding = new Thickness(16),
                Background = Brushes.Transparent
            };
            Grid.SetRow(body, 1);
            innerGrid.Children.Add(body);

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
            btnStyle.Setters.Add(new Setter(Button.BackgroundProperty, ResolveBrush("ButtonBgBrush", new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x2C)))));
            btnStyle.Setters.Add(new Setter(Button.ForegroundProperty, ResolveBrush("ControlTextBrush", Brushes.White)));
            btnStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(12, 6, 12, 6)));
            btnStyle.Setters.Add(new Setter(Button.BorderBrushProperty, ResolveBrush("InputBorderBrush", new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)))));
            btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            btnStyle.Setters.Add(new Setter(Button.CursorProperty, System.Windows.Input.Cursors.Hand));

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panel.Children.Add(row);

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Style = btnStyle,
                IsCancel = true
            };
            cancelBtn.Click += (_, _) => { Action = RestartAction.Cancel; DialogResult = false; };
            Grid.SetColumn(cancelBtn, 0);

            var actionRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            Grid.SetColumn(actionRow, 2);

            var onRestartBtn = new Button
            {
                Content = "Delete after restart",
                Style = btnStyle,
                Margin = new Thickness(0, 0, 8, 0)
            };
            onRestartBtn.Click += (_, _) => { Action = RestartAction.DeleteOnRestart; DialogResult = true; };

            var restartBtn = new Button
            {
                Content = "Delete and restart",
                Style = btnStyle,
                Foreground = ResolveBrush("AccentRedBrush", new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)))
            };
            restartBtn.Click += (_, _) => { Action = RestartAction.RestartNow; DialogResult = true; };

            row.Children.Add(cancelBtn);
            row.Children.Add(actionRow);
            actionRow.Children.Add(onRestartBtn);
            actionRow.Children.Add(restartBtn);
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
