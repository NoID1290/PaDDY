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
                var asm = Assembly.GetExecutingAssembly();
                var ver = asm.GetName().Version;
                var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                string displayVersion;
                if (ver != null)
                {
                    displayVersion = $"Version {ver.Major}.{ver.Minor}.{ver.Build}";
                    // Append pre-release suffix if present in the informational version
                    if (infoVersion != null)
                    {
                        var plusIdx = infoVersion.IndexOf('+'); // strip build metadata if any
                        var infoBase = plusIdx >= 0 ? infoVersion[..plusIdx] : infoVersion;
                        var dashIdx = infoBase.IndexOf('-');
                        if (dashIdx >= 0)
                            displayVersion += " " + infoBase[dashIdx..];
                    }
                }
                else
                {
                    displayVersion = "Version —";
                }

                VersionLabel.Text = displayVersion;
                CopyrightLabel.Text = $"© {System.DateTime.Now.Year} NoID Softwork. All rights reserved.";
            };
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CreditsButton_Click(object sender, RoutedEventArgs e)
        {
            new CreditsWindow { Owner = this }.ShowDialog();
        }
    }
}
