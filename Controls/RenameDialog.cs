using System.Windows;

namespace PaDDY.Controls
{
    public sealed partial class RenameDialog : Window
    {
        public string NewName => NameTextBox.Text;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            NameTextBox.Text = currentName;
            Loaded += (_, _) => { NameTextBox.Focus(); NameTextBox.SelectAll(); };
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
        private void ChromeClose_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

