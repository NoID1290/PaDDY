using System.Reflection;
using System.Windows;
using System.IO;
using PaDDY.Services;
using PaDDY.Helpers;
using System;
using Microsoft.Win32;
using MessagingToolkit = System.Windows.MessageBox;

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
            };
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CreditsButton_Click(object sender, RoutedEventArgs e)
        {
            new CreditsWindow { Owner = this }.ShowDialog();
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/NoID1290/PaDDY",
                UseShellExecute = true
            });
        }

        private void ExportData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PaDDY Backup (*.PADBACK)|*.PADBACK",
                FileName = $"PaDDY_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.PADBACK"
            };

            if (dlg.ShowDialog() == true)
            {
                var backupService = new BackupService();
                if (backupService.CreateBackup(dlg.FileName))
                {
                    MessagingToolkit.Show(this, "Backup created successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessagingToolkit.Show(this, "Failed to create backup. Please ensure your data files are intact.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PaDDY Backup (*.PADBACK)|*.PADBACK"
            };

            if (dlg.ShowDialog() == true)
            {
                var backupService = new BackupService();
                var mainWindow = Owner as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.PrepareRecordingDataRestore();
                }

                if (backupService.RestoreBackup(dlg.FileName))
                {
                    if (mainWindow != null)
                    {
                        mainWindow.ReloadRecordingDataFromDisk();
                        MessagingToolkit.Show(this, "Backup restored successfully and recordings have been reloaded.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessagingToolkit.Show(this, "Backup restored successfully. Please restart the application to apply changes.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    MessagingToolkit.Show(this, "Failed to restore backup. Please ensure the file is a valid PaDDY backup.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
