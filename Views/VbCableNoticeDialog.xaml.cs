using System;
using System.Diagnostics;
using System.Windows;

namespace PaDDY
{
    /// <summary>
    /// Interaction logic for VbCableNoticeDialog.xaml.
    /// Informs the user about the on-demand download from VB-Audio Software servers,
    /// highlights donationware terms, and provides links to Vincent Burel / VB-Audio website.
    /// </summary>
    public partial class VbCableNoticeDialog : Window
    {
        public VbCableNoticeDialog()
        {
            InitializeComponent();
        }

        private void DownloadAndInstallBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OpenVbAudioWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://vb-audio.com") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Unable to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenLicensingPage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://vb-audio.com/Services/licensing.htm") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(this, $"Unable to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
