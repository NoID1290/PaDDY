using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;

namespace PaDDY
{
    public partial class CreditsWindow : Window
    {
        private sealed class DependencyAttribution
        {
            public string Name { get; init; } = string.Empty;
            public string License { get; init; } = string.Empty;
            public string Usage { get; init; } = string.Empty;
            public string LicenseFile { get; init; } = string.Empty;
        }

        private static readonly DependencyAttribution[] Dependencies =
        {
            new() { Name = "NAudio", License = "MIT", Usage = "Audio capture, playback, and device I/O", LicenseFile = "NAudio-LICENSE.txt" },
            new() { Name = "NAudio.Lame", License = "MIT", Usage = "Managed wrapper used for MP3 encoding", LicenseFile = "NAudio.Lame-LICENSE.txt" },
            new() { Name = "NAudio.Vorbis", License = "MIT", Usage = "Vorbis support used by audio codec pipeline", LicenseFile = "NAudio.Vorbis-LICENSE.txt" },
            new() { Name = "NVorbis", License = "MIT", Usage = "Vorbis decode/container support (transitive)", LicenseFile = "NVorbis-LICENSE.txt" },
            new() { Name = "Concentus", License = "BSD-3-Clause / Opus notices", Usage = "Opus codec implementation", LicenseFile = "Concentus-LICENSE.txt" },
            new() { Name = "Concentus (opus-fix)", License = "BSD", Usage = "Additional Opus licensing notices", LicenseFile = "Concentus-opus-fix-COPYING.txt" },
            new() { Name = "Concentus.OggFile", License = "MIT", Usage = "Ogg/Opus stream container support", LicenseFile = "Concentus.OggFile-LICENSE.txt" },
            new() { Name = "OggVorbisEncoder", License = "MIT", Usage = "Ogg Vorbis encoding", LicenseFile = "OggVorbisEncoder-LICENSE.txt" }
        };

        public CreditsWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => CreditsTextBox.Text = BuildCreditsText();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenLicensesFolder_Click(object sender, RoutedEventArgs e)
        {
            string licensesDir = ResolveLicensesDirectory();
            if (Directory.Exists(licensesDir))
            {
                Process.Start("explorer.exe", licensesDir);
            }
            else
            {
                System.Windows.MessageBox.Show(this,
                    "Unable to locate the licenses folder in this build.",
                    "Credits and Licenses",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private static string BuildCreditsText()
        {
            var sb = new StringBuilder();
            string licensesDir = ResolveLicensesDirectory();

            sb.AppendLine("PaDDY uses the following third-party components:");
            sb.AppendLine();

            foreach (var dependency in Dependencies)
            {
                sb.AppendLine($"- {dependency.Name}");
                sb.AppendLine($"  License: {dependency.License}");
                sb.AppendLine($"  Usage: {dependency.Usage}");
            }

            sb.AppendLine();
            sb.AppendLine("Native runtime dependency:");
            sb.AppendLine("- libmp3lame.32.dll and libmp3lame.64.dll (used by NAudio.Lame at runtime)");
            sb.AppendLine("  License context: LGPL (see upstream LAME project licensing)");
            sb.AppendLine();

            foreach (var dependency in Dependencies)
            {
                sb.AppendLine(new string('=', 72));
                sb.AppendLine($"{dependency.Name} - {dependency.License}");
                sb.AppendLine(new string('-', 72));

                string path = Path.Combine(licensesDir, dependency.LicenseFile);
                if (File.Exists(path))
                {
                    sb.AppendLine(File.ReadAllText(path));
                }
                else
                {
                    sb.AppendLine($"License file not found: {dependency.LicenseFile}");
                }

                sb.AppendLine();
            }

            if (!Directory.Exists(licensesDir))
            {
                sb.AppendLine("Licenses directory was not found.");
                sb.AppendLine("Expected location: " + licensesDir);
            }

            return sb.ToString();
        }

        private static string ResolveLicensesDirectory()
        {
            string direct = Path.Combine(AppContext.BaseDirectory, "licenses");
            if (Directory.Exists(direct))
                return direct;

            // Dev fallback when running from build output under bin/<Configuration>/<TFM>/
            string sourceTree = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..",
                "NoIDSoftwork.AudioProcessor",
                "vendors",
                "licenses"));

            return sourceTree;
        }
    }
}
