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
            new() { Name = "OggVorbisEncoder", License = "MIT", Usage = "Ogg Vorbis encoding", LicenseFile = "OggVorbisEncoder-LICENSE.txt" },
            new() { Name = "NAudio.Flac", License = "Unlicense", Usage = "FLAC decoding for playback and editing", LicenseFile = "NAudio.Flac.Unlicense.txt" },
            new() { Name = "CUETools.Flake (libFLAC)", License = "BSD-3-Clause", Usage = "Lossless FLAC encoding", LicenseFile = "libFLAC.BSD.txt" },
            new() { Name = "MessagePack-CSharp", License = "MIT", Usage = "Binary serialization of application settings", LicenseFile = "MessagePack-LICENSE.txt" },
            new() { Name = "Microsoft.Data.Sqlite", License = "MIT", Usage = "Recordings metadata database", LicenseFile = "Microsoft.Data.Sqlite-LICENSE.txt" },
            new() { Name = "SQLitePCLRaw", License = "Apache-2.0", Usage = "Native SQLite provider for the recordings database", LicenseFile = "SQLitePCLRaw-LICENSE.txt" },
            new() { Name = "Whisper.net", License = "MIT", Usage = "On-device speech-to-text for auto-renaming", LicenseFile = "Whisper.net-LICENSE.txt" },
            new() { Name = "Whisper.net.Runtime.Cuda", License = "MIT", Usage = "CUDA GPU acceleration for Whisper speech-to-text", LicenseFile = "Whisper.net-LICENSE.txt" },
            new() { Name = "AudioPlugSharp", License = "MIT", Usage = "VST3 plugin hosting support", LicenseFile = "AudioPlugSharp-LICENSE.txt" },
            new() { Name = "VST.NET (vstnet)", License = "MIT", Usage = "VST2 plugin hosting support", LicenseFile = "VST.NET-LICENSE.txt" },
        };

        public CreditsWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => CreditsTextBox.Text = BuildCreditsText();
        }

        private void ChromeClose_Click(object sender, RoutedEventArgs e) => Close();

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
            sb.AppendLine("Trademark Notices:");
            sb.AppendLine("- VST is a registered trademark of Steinberg Media Technologies GmbH.");
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
                "AudioProcessor",
                "vendors",
                "licenses"));

            return sourceTree;
        }
    }
}
