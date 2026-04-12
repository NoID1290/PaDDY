using System.Windows;

namespace PaDDY.Controls
{
    public sealed class RenameDialog : Window
    {
        private readonly System.Windows.Controls.TextBox _textBox;
        public string NewName => _textBox.Text;

        public RenameDialog(string currentName)
        {
            Title = "Rename Recording";
            Width = 340; Height = 130;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x2D, 0x2D, 0x2D));

            _textBox = new System.Windows.Controls.TextBox
            {
                Text = currentName,
                Margin = new Thickness(12, 12, 12, 8),
                Padding = new Thickness(4),
                FontSize = 13
            };
            _textBox.SelectAll();
            Loaded += (_, _) => { _textBox.Focus(); _textBox.SelectAll(); };

            var ok = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 75,
                Height = 28,
                Margin = new Thickness(12, 0, 6, 12),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                IsDefault = true
            };
            ok.Click += (_, _) => { DialogResult = true; };

            var cancel = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 28,
                Margin = new Thickness(0, 0, 12, 12),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                IsCancel = true
            };

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };
            btnPanel.Children.Add(ok);
            btnPanel.Children.Add(cancel);

            var root = new System.Windows.Controls.StackPanel();
            root.Children.Add(_textBox);
            root.Children.Add(btnPanel);

            Content = root;
        }
    }
}
