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
        private readonly bool _includePrerelease;

        public UpdateService(bool includePrerelease = false)
        {
            _includePrerelease = includePrerelease;
        }

        // ── Paths ────────────────────────────────────────────────────────────────
        private static string AutoBackupPath =>
            Path.Combine(AppDataPaths.AppDataRoot, ".update_backup.PADBACK");

        private static string InstallerDownloadPath =>
            Path.Combine(Path.GetTempPath(), "PaDDY_Update_Installer.exe");

        // ── GitHub API ───────────────────────────────────────────────────────────
        public static readonly Uri ReleasesPageUri = new("https://github.com/NoID1290/PaDDY/releases");

        private const string LatestReleaseApiEndpoint =
            "https://api.github.com/repos/NoID1290/PaDDY/releases/latest";

        private const string AllReleasesApiEndpoint =
            "https://api.github.com/repos/NoID1290/PaDDY/releases";

        // ── Events ───────────────────────────────────────────────────────────────
        /// <summary>Raised when the status message changes (e.g. "Checking for updates...").</summary>
        public event Action<string>? StatusChanged;

        /// <summary>Raised during download with a fraction from 0.0 to 1.0.</summary>
        public event Action<double>? DownloadProgressChanged;

        // ── Data ─────────────────────────────────────────────────────────────────
        public record UpdateCheckResult(
            SemanticTagVersion LatestVersion,
            string InstallerDownloadUrl,
            long AssetSizeBytes);

        // ── Check ────────────────────────────────────────────────────────────────

        private UpdateCheckResult? ParseReleaseElement(JsonElement element)
        {
            try
            {
                // Parse version from tag_name
                if (!element.TryGetProperty("tag_name", out var tagProp))
                    return null;

                string tagName = tagProp.GetString() ?? string.Empty;
                if (!TryParseTagVersion(tagName, out var latestVersion))
                    return null;

                var currentVersion = GetCurrentAppVersion();

                if (latestVersion <= currentVersion)
                    return null;

                // Find installer asset: look for *Installer*.exe in assets[]
                if (!element.TryGetProperty("assets", out var assetsProp))
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
            catch
            {
                return null;
            }
        }

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

                string endpoint = _includePrerelease ? AllReleasesApiEndpoint : LatestReleaseApiEndpoint;

                using var response = await http.GetAsync(endpoint, ct);
                if (!response.IsSuccessStatusCode)
                    return null;

                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                if (_includePrerelease)
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var releaseElement in document.RootElement.EnumerateArray())
                        {
                            var result = ParseReleaseElement(releaseElement);
                            if (result != null)
                                return result;
                        }
                    }
                }
                else
                {
                    return ParseReleaseElement(document.RootElement);
                }

                return null;
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

        // ── Stream Deck Plugin Cleanup ──────────────────────────────────────────

        private static string StreamDeckPluginPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Elgato", "StreamDeck", "Plugins", "com.paddy.sdPlugin");

        /// <summary>
        /// Uninstalls the Stream Deck plugin (if present) before closing any running Stream Deck processes.
        /// </summary>
        public void UninstallPluginAndCloseStreamDeck()
        {
            try
            {
                string pluginPath = StreamDeckPluginPath;

                // 1. Attempt to uninstall the plugin directory before closing Stream Deck
                if (Directory.Exists(pluginPath))
                {
                    StatusChanged?.Invoke("Uninstalling Stream Deck plugin...");
                    try
                    {
                        Directory.Delete(pluginPath, true);
                        Console.WriteLine("[UpdateService] Stream Deck plugin uninstalled successfully.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpdateService] Plugin folder deletion before closing Stream Deck failed (will retry after): {ex.Message}");
                    }
                }

                // 2. Close Stream Deck if running
                var streamDeckProcesses = Process.GetProcessesByName("StreamDeck");
                if (streamDeckProcesses.Length > 0)
                {
                    StatusChanged?.Invoke("Closing Stream Deck...");
                    foreach (var process in streamDeckProcesses)
                    {
                        try
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[UpdateService] Error closing Stream Deck process: {ex.Message}");
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }

                    Thread.Sleep(500);
                }

                // 3. If plugin folder still exists (e.g. was locked while Stream Deck was running), delete it now
                if (Directory.Exists(pluginPath))
                {
                    try
                    {
                        Directory.Delete(pluginPath, true);
                        Console.WriteLine("[UpdateService] Stream Deck plugin deleted after closing Stream Deck.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[UpdateService] Failed to delete plugin folder after closing Stream Deck: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UpdateService] UninstallPluginAndCloseStreamDeck error: {ex.Message}");
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
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
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

    public struct SemanticTagVersion : IComparable<SemanticTagVersion>
    {
        public Version BaseVersion { get; set; }
        public int PreReleaseNumber { get; set; }
        public string RawTag { get; set; }

        public int CompareTo(SemanticTagVersion other)
        {
            int cmp = BaseVersion.CompareTo(other.BaseVersion);
            if (cmp != 0) return cmp;
            return PreReleaseNumber.CompareTo(other.PreReleaseNumber);
        }

        public static bool operator >(SemanticTagVersion a, SemanticTagVersion b) => a.CompareTo(b) > 0;
        public static bool operator <(SemanticTagVersion a, SemanticTagVersion b) => a.CompareTo(b) < 0;
        public static bool operator >=(SemanticTagVersion a, SemanticTagVersion b) => a.CompareTo(b) >= 0;
        public static bool operator <=(SemanticTagVersion a, SemanticTagVersion b) => a.CompareTo(b) <= 0;

        public override string ToString() => RawTag ?? BaseVersion.ToString();
    }

    private static bool TryParseTagVersion(string tagName, out SemanticTagVersion semVer)
        {
            semVer = new SemanticTagVersion { BaseVersion = new Version(0, 0, 0, 0), PreReleaseNumber = 0, RawTag = tagName ?? string.Empty };
            if (string.IsNullOrWhiteSpace(tagName)) return false;

            string normalized = tagName.Trim();
            if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized[1..];

            string basePart = normalized;
            int preReleaseNum = 99999; // Default for official releases without pre-release suffix

            int dashIndex = normalized.IndexOf('-');
            if (dashIndex > 0)
            {
                basePart = normalized[..dashIndex];
                string suffix = normalized[(dashIndex + 1)..];

                var match = System.Text.RegularExpressions.Regex.Match(suffix, @"\d+");
                if (match.Success && int.TryParse(match.Value, out int parsedNum))
                {
                    preReleaseNum = parsedNum;
                }
                else
                {
                    preReleaseNum = 1;
                }
            }

            if (!Version.TryParse(basePart, out var parsedVersion))
                return false;

            semVer = new SemanticTagVersion
            {
                BaseVersion = parsedVersion,
                PreReleaseNumber = preReleaseNum,
                RawTag = tagName
            };
            return true;
        }

        public static SemanticTagVersion GetCurrentAppVersion()
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version ?? new Version(1, 0, 0, 0);

            var infoVerAttr = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string raw = infoVerAttr?.InformationalVersion ?? ver.ToString();

            if (TryParseTagVersion(raw, out var semVer))
            {
                return semVer;
            }

            return new SemanticTagVersion
            {
                BaseVersion = ver,
                PreReleaseNumber = 99999,
                RawTag = ver.ToString()
            };
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
