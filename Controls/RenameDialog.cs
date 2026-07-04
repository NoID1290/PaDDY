using Microsoft.UI.Xaml.Controls;

namespace PaDDY.Controls
{
    public sealed partial class RenameDialog : ContentDialog
    {
        public string NewName => NameTextBox.Text;

        public RenameDialog(string currentName)
        {
            InitializeComponent();
            NameTextBox.Text = currentName;
            this.Opened += (s, e) => { NameTextBox.Focus(Microsoft.UI.Xaml.FocusState.Programmatic); NameTextBox.SelectAll(); };
        }
    }
}
