using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PaDDY.Helpers;

namespace PaDDY.Services
{
    /// <summary>
    /// Encapsulates the full auto-update workflow: check → download → backup → install → restore.
    /// </summary>
    public class UpdateService
    {
        // ── Paths ────────────────────────────────────────────────────────────────
        private static string AutoBackupPath =>
            Path.Combine(AppDataPaths.AppDataRoot, ".update_backup.PADBACK");

        private static string InstallerDownloadPath =>
            Path.Combine(Path.GetTempPath(), "PaDDY_Update_Installer.exe");

        // ── GitHub API ───────────────────────────────────────────────────────────
        private const string ReleasesApiEndpoint =
            "https://api.github.com/repos/NoID1290/PaDDY/releases/latest";

        // ── Events ───────────────────────────────────────────────────────────────
        /// <summary>Raised when the status message changes (e.g. "Checking for updates...").</summary>
        public event Action<string>? StatusChanged;

        /// <summary>Raised during download with a fraction from 0.0 to 1.0.</summary>
        public event Action<double>? DownloadProgressChanged;

        // ── Data ─────────────────────────────────────────────────────────────────
        public record UpdateCheckResult(
            Version LatestVersion,
            string InstallerDownloadUrl,
            long AssetSizeBytes);

        // ── Check ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Queries the GitHub releases API for the latest version.
        /// Returns null if the app is already up-to-date or no installer asset is available.
        /// </summary>
        public async Task<UpdateCheckResult?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                StatusChanged?.Invoke("Checking for updates...");

                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PaDDY-UpdateCheck/1.0");

                using var response = await http.GetAsync(ReleasesApiEndpoint, ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                // Parse version from tag_name
                if (!document.RootElement.TryGetProperty("tag_name", out var tagProp))
                    return null;

                string tagName = tagProp.GetString() ?? string.Empty;
                if (!TryParseTagVersion(tagName, out var latestVersion))
                    return null;

                var currentVersion = Assembly.GetExecutingAssembly().GetName().Version
                                     ?? new Version(0, 0, 0, 0);

                if (latestVersion <= currentVersion)
                    return null;

                // Find installer asset: look for *Installer*.exe in assets[]
                if (!document.RootElement.TryGetProperty("assets", out var assetsProp))
                    return null;

                string? downloadUrl = null;
                long assetSize = 0;

                foreach (var asset in assetsProp.EnumerateArray())
                {
                    string? name = asset.GetProperty("name").GetString();
                    if (name == null) continue;

                    if (name.Contains("Installer", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        if (asset.TryGetProperty("size", out var sizeProp))
                            assetSize = sizeProp.GetInt64();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                    return null;

                return new UpdateCheckResult(latestVersion, downloadUrl, assetSize);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Check failed: {ex.Message}");
                return null;
            }
        }

        // ── Download ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Downloads the installer .exe to a temp location, reporting progress.
        /// Returns the path to the downloaded file, or null on failure.
        /// </summary>
        public async Task<string?> DownloadInstallerAsync(
            string downloadUrl,
            long expectedSize,
            CancellationToken ct = default)
        {
            try
            {
                StatusChanged?.Invoke("Downloading update...");
                DownloadProgressChanged?.Invoke(0.0);

                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd("PaDDY-UpdateCheck/1.0");

                using var response = await http.GetAsync(downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? expectedSize;
                if (totalBytes <= 0) totalBytes = expectedSize;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(InstallerDownloadPath,
                    FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                long bytesRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        double fraction = (double)bytesRead / totalBytes;
                        DownloadProgressChanged?.Invoke(Math.Min(fraction, 1.0));
                    }
                }

                DownloadProgressChanged?.Invoke(1.0);
                Console.WriteLine($"[UpdateService] Downloaded installer: {InstallerDownloadPath}");
                return InstallerDownloadPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Download failed: {ex.Message}");
                TryDeleteFile(InstallerDownloadPath);
                return null;
            }
        }

        // ── Backup ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates an automatic backup before updating.
        /// </summary>
        public bool CreatePreUpdateBackup()
        {
            try
            {
                StatusChanged?.Invoke("Backing up your data...");
                var backupService = new BackupService();
                return backupService.CreateBackup(AutoBackupPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Backup failed: {ex.Message}");
                return false;
            }
        }

        // ── Install ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Launches the INNO Setup installer in silent mode and shuts down the app.
        /// The installer will restart PaDDY with --restore-update after completion.
        /// </summary>
        public void LaunchInstallerAndExit(string installerPath)
        {
            StatusChanged?.Invoke("Installing update... PaDDY will restart.");

            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                UseShellExecute = true // Required for UAC elevation
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Failed to launch installer: {ex.Message}");
                return; // Don't shutdown if installer failed to start
            }

            // Give the installer a moment to start, then shut down
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                System.Windows.Application.Current.Shutdown();
            });
        }

        // ── Restore (post-update) ────────────────────────────────────────────────

        /// <summary>
        /// Checks whether the app was launched with --restore-update AND a backup exists.
        /// </summary>
        public static bool HasPendingRestore()
        {
            return App.PendingUpdateRestore && File.Exists(AutoBackupPath);
        }

        /// <summary>
        /// Restores the auto-backup created before the update.
        /// </summary>
        public bool RestorePostUpdateBackup()
        {
            try
            {
                StatusChanged?.Invoke("Restoring your data...");
                var backupService = new BackupService();
                bool result = backupService.RestoreBackup(AutoBackupPath);

                if (result)
                {
                    StatusChanged?.Invoke("Data restored successfully!");
                    CleanupBackup();
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] Restore failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deletes the auto-backup file after successful restore.
        /// </summary>
        public static void CleanupBackup()
        {
            TryDeleteFile(AutoBackupPath);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool TryParseTagVersion(string tagName, out Version version)
        {
            version = new Version(0, 0, 0, 0);
            if (string.IsNullOrWhiteSpace(tagName)) return false;

            string normalized = tagName.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[1..];

            // Strip pre-release suffixes (e.g. "1.8.0.0712-Pre-release_1" → "1.8.0.0712")
            int dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
                normalized = normalized[..dashIndex];

            if (!Version.TryParse(normalized, out var parsed))
                return false;

            version = parsed;
            return true;
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }
}
