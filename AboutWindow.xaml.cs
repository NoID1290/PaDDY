using System.Reflection;
using System.Windows;

namespace PaDDY
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                VersionLabel.Text = ver != null
                    ? $"Version {ver.Major}.{ver.Minor}.{ver.Build}"
                    : "Version —";
                CopyrightLabel.Text = $"© {System.DateTime.Now.Year} NoID Softwork. All rights reserved.";
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
