using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using System;
using System.IO;
using System.Reflection;
using Microsoft.UI.Xaml;
using PaDDY.Helpers;
using PaDDY.Services;
using WinRT.Interop;
using Windows.Storage.Pickers;

namespace PaDDY
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            var appWindow = this.AppWindow;
            var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsResizable = false;
            }

            // Using Window.Content.Loaded instead of Window.Loaded in WinUI 3
            if (Content is FrameworkElement fe)
            {
                fe.Loaded += (_, _) =>
                {
                    var asm = Assembly.GetExecutingAssembly();
                    var ver = asm.GetName().Version;
                    var infoVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

                    string displayVersion;
                    if (ver != null)
                    {
                        displayVersion = $"Version {ver.Major}.{ver.Minor}.{ver.Build}";
                        if (infoVersion != null)
                        {
                            var plusIdx = infoVersion.IndexOf('+');
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
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void CreditsButton_Click(object sender, RoutedEventArgs e)
        {
            var win = new CreditsWindow();
            win.Activate();
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/NoID1290/PaDDY",
                UseShellExecute = true
            });
        }

        private async void ExportData_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PaDDY Backup", new[] { ".PADBACK" });
            picker.SuggestedFileName = $"PaDDY_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.PADBACK";

            var file = await picker.PickSaveFileAsync();
            if (file != null)
            {
                var backupService = new BackupService();
                if (backupService.CreateBackup(file.Path))
                {
                    await DialogHelper.ShowMessageAsync(Content.XamlRoot, "Success", "Backup created successfully.");
                }
                else
                {
                    await DialogHelper.ShowMessageAsync(Content.XamlRoot, "Error", "Failed to create backup. Please ensure your data files are intact.");
                }
            }
        }

        private async void ImportData_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".PADBACK");

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                var backupService = new BackupService();
                // We cannot easily get Owner in WinUI 3 Window, but we can do a global reload if needed
                // For now we'll just advise a restart since MainWindow handles its own load
                if (backupService.RestoreBackup(file.Path))
                {
                    await DialogHelper.ShowMessageAsync(Content.XamlRoot, "Success", "Backup restored successfully. Please restart the application to apply changes.");
                }
                else
                {
                    await DialogHelper.ShowMessageAsync(Content.XamlRoot, "Error", "Failed to restore backup. Please ensure the file is a valid PaDDY backup.");
                }
            }
        }
    }
}
