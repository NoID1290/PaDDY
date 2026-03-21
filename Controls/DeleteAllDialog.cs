using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace Paddy.Controls
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
            Width = 340;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            Foreground = Brushes.White;

            var panel = new StackPanel { Margin = new Thickness(20) };
            Content = panel;

            panel.Children.Add(new TextBlock
            {
                Text = "Delete all recordings from disk?",
                Foreground = Brushes.White,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });

            var btnStyle = new Style(typeof(Button));
            btnStyle.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x3C))));
            btnStyle.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            btnStyle.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(10, 6, 10, 6)));
            btnStyle.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
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
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B))
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
    }
}
