using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PaDDY.Helpers;
using Whisper.net.LibraryLoader;

namespace PaDDY.Services
{
    /// <summary>
    /// Manages the on-demand download, verification, extraction, and runtime registration
    /// of the NVIDIA CUDA Whisper acceleration pack (~670 MB uncompressed, ~270 MB download).
    /// </summary>
    public static class CudaManager
    {
        // ── Constants & URLs ───────────────────────────────────────────────────
        private const string CublasArchiveName = "libcublas-windows-x86_64-13.0.0.19-archive";
        private const string CudartArchiveName = "cuda_cudart-windows-x86_64-13.0.48-archive";

        private const string CublasZipUrl =
            $"https://developer.download.nvidia.com/compute/cuda/redist/libcublas/windows-x86_64/{CublasArchiveName}.zip";
        private const string CudartZipUrl =
            $"https://developer.download.nvidia.com/compute/cuda/redist/cuda_cudart/windows-x86_64/{CudartArchiveName}.zip";
        private const string WhisperCudaNupkgUrl =
            "https://www.nuget.org/api/v2/package/Whisper.net.Runtime.Cuda.Windows/1.9.1";

        private const string CublasZipSha256 =
            "c8fb12715b9639f51983315cc4b195e272128c675aa3766b13c4f470b892e6c8";
        private const string CudartZipSha256 =
            "82fb29001895810ce02e88b180cbaa90607b852a7f67a63f18f202079eaa2966";

        // ── Paths ─────────────────────────────────────────────────────────────
        public static string CudaBaseDir => Path.Combine(AppDataPaths.AppDataRoot, "cuda");
        public static string CudaWinX64Dir => Path.Combine(CudaBaseDir, "runtimes", "cuda", "win-x64");
        public static string AppBundledCudaDir => Path.Combine(AppContext.BaseDirectory, "runtimes", "cuda", "win-x64");

        // ── State ─────────────────────────────────────────────────────────────
        public static bool IsDownloading { get; private set; }
        public static double ActiveDownloadPercent { get; private set; } = -1;
        public static string ActiveStatusText { get; private set; } = string.Empty;
        public static event Action<double, string>? DownloadProgressUpdated;

        private static bool _dllDirectoriesConfigured;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr AddDllDirectory(string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDefaultDllDirectories(uint DirectoryFlags);

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

        const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        const uint LOAD_LIBRARY_SEARCH_USER_DIRS = 0x00000400;

        /// <summary>
        /// Checks if all necessary CUDA binaries are installed (either in AppData or bundled).
        /// </summary>
        public static bool IsCudaPackInstalled()
        {
            // Check AppData directory
            if (File.Exists(Path.Combine(CudaWinX64Dir, "ggml-cuda-whisper.dll")) &&
                File.Exists(Path.Combine(CudaWinX64Dir, "cublas64_13.dll")) &&
                File.Exists(Path.Combine(CudaWinX64Dir, "cublasLt64_13.dll")) &&
                (File.Exists(Path.Combine(CudaWinX64Dir, "cudart64_13.dll")) || File.Exists(Path.Combine(CudaBaseDir, "cudart64_13.dll"))))
            {
                return true;
            }

            // Check bundled app directory
            if (File.Exists(Path.Combine(AppBundledCudaDir, "ggml-cuda-whisper.dll")) &&
                File.Exists(Path.Combine(AppBundledCudaDir, "cublas64_13.dll")) &&
                File.Exists(Path.Combine(AppBundledCudaDir, "cublasLt64_13.dll")) &&
                (File.Exists(Path.Combine(AppBundledCudaDir, "cudart64_13.dll")) || File.Exists(Path.Combine(AppContext.BaseDirectory, "cudart64_13.dll"))))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Computes total size of the installed CUDA acceleration pack in bytes.
        /// </summary>
        public static long GetInstalledCudaSizeBytes()
        {
            if (!Directory.Exists(CudaBaseDir)) return 0;
            try
            {
                long total = 0;
                var dirInfo = new DirectoryInfo(CudaBaseDir);
                foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
                {
                    total += file.Length;
                }
                return total;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Deletes the downloaded CUDA acceleration pack to reclaim disk space (~670 MB).
        /// </summary>
        public static bool DeleteCudaPack()
        {
            if (!Directory.Exists(CudaBaseDir)) return true;
            try
            {
                Directory.Delete(CudaBaseDir, recursive: true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Registers CUDA runtime directory with Windows and Whisper.net runtime options.
        /// </summary>
        public static void InitializeCudaRuntimeEnvironment()
        {
            if (_dllDirectoriesConfigured) return;

            string targetDir = Directory.Exists(CudaWinX64Dir) ? CudaWinX64Dir : AppBundledCudaDir;
            if (Directory.Exists(targetDir))
            {
                try
                {
                    SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_USER_DIRS);
                    AddDllDirectory(targetDir);
                    if (Directory.Exists(CudaBaseDir))
                    {
                        AddDllDirectory(CudaBaseDir);
                    }
                }
                catch { }

                // Configure Whisper.net LibraryPath if installed in AppData
                if (Directory.Exists(CudaWinX64Dir))
                {
                    // Whisper.net's NativeLibraryLoader uses Path.GetDirectoryName(RuntimeOptions.LibraryPath)
                    RuntimeOptions.LibraryPath = Path.Combine(CudaBaseDir, "whisper.dll");
                }

                // Explicitly pre-load cudart64_13.dll into process space so Whisper and cuBLAS resolve it immediately
                string cudartPath = File.Exists(Path.Combine(CudaWinX64Dir, "cudart64_13.dll"))
                    ? Path.Combine(CudaWinX64Dir, "cudart64_13.dll")
                    : (File.Exists(Path.Combine(CudaBaseDir, "cudart64_13.dll"))
                        ? Path.Combine(CudaBaseDir, "cudart64_13.dll")
                        : Path.Combine(AppContext.BaseDirectory, "cudart64_13.dll"));

                if (File.Exists(cudartPath))
                {
                    LoadLibraryExW(cudartPath, IntPtr.Zero, 0);
                }

                _dllDirectoriesConfigured = true;
            }
        }

        /// <summary>
        /// Downloads, validates, and extracts the full CUDA pack on-demand.
        /// </summary>
        public static async Task DownloadCudaPackAsync(
            IProgress<(double Percent, string StatusText)>? progress = null,
            CancellationToken ct = default)
        {
            if (IsDownloading) return;

            IsDownloading = true;
            ActiveDownloadPercent = 0.0;
            ActiveStatusText = "Preparing CUDA download...";
            ReportProgress(progress, 0.0, ActiveStatusText);

            string tempDir = Path.Combine(Path.GetTempPath(), $"paddy-cuda-temp-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(CudaWinX64Dir);

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("PaDDY-Soundboard/2.3 (+https://github.com/NoID1290/Paddy)");

                // ── 1. Whisper.net.Runtime.Cuda.Windows (~45 MB nupkg / zip) ─────────
                string whisperNupkgPath = Path.Combine(tempDir, "whisper_cuda.zip");
                await DownloadFileWithProgressAsync(
                    httpClient,
                    WhisperCudaNupkgUrl,
                    whisperNupkgPath,
                    "Whisper CUDA Kernels (1/3)",
                    0.0,
                    0.25,
                    expectedSha256: null,
                    progress,
                    ct).ConfigureAwait(false);

                ReportProgress(progress, 0.25, "Extracting Whisper CUDA runtime...");
                ExtractWhisperCudaFiles(whisperNupkgPath, CudaWinX64Dir);
                try { File.Delete(whisperNupkgPath); } catch { }

                // ── 2. NVIDIA CUDA Runtime - cudart (~1.5 MB zip) ─────────────────────
                string cudartZipPath = Path.Combine(tempDir, "cudart.zip");
                await DownloadFileWithProgressAsync(
                    httpClient,
                    CudartZipUrl,
                    cudartZipPath,
                    "NVIDIA cuDART (2/3)",
                    0.25,
                    0.30,
                    expectedSha256: CudartZipSha256,
                    progress,
                    ct).ConfigureAwait(false);

                ReportProgress(progress, 0.30, "Extracting NVIDIA cuDART...");
                ExtractCudartFiles(cudartZipPath, CudaWinX64Dir, CudaBaseDir);
                try { File.Delete(cudartZipPath); } catch { }

                // ── 3. NVIDIA CUDA Linear Algebra - cuBLAS (~220 MB zip) ──────────────
                string cublasZipPath = Path.Combine(tempDir, "cublas.zip");
                await DownloadFileWithProgressAsync(
                    httpClient,
                    CublasZipUrl,
                    cublasZipPath,
                    "NVIDIA cuBLAS Acceleration (3/3)",
                    0.30,
                    0.95,
                    expectedSha256: CublasZipSha256,
                    progress,
                    ct).ConfigureAwait(false);

                ReportProgress(progress, 0.95, "Extracting NVIDIA cuBLAS libraries (this may take a few seconds)...");
                ExtractCublasFiles(cublasZipPath, CudaWinX64Dir, CudaBaseDir);
                try { File.Delete(cublasZipPath); } catch { }

                // Initialize runtime paths for newly downloaded libraries
                InitializeCudaRuntimeEnvironment();

                ReportProgress(progress, 1.0, "CUDA Acceleration Pack installed successfully!");
            }
            finally
            {
                IsDownloading = false;
                ActiveDownloadPercent = -1;
                ActiveStatusText = string.Empty;
                DownloadProgressUpdated?.Invoke(-1, string.Empty);

                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, recursive: true);
                    }
                }
                catch { }
            }
        }

        private static void ReportProgress(IProgress<(double Percent, string StatusText)>? progress, double percent, string text)
        {
            ActiveDownloadPercent = percent;
            ActiveStatusText = text;
            progress?.Report((percent, text));
            DownloadProgressUpdated?.Invoke(percent, text);
        }

        private static async Task DownloadFileWithProgressAsync(
            HttpClient client,
            string url,
            string destinationPath,
            string label,
            double startFraction,
            double endFraction,
            string? expectedSha256,
            IProgress<(double Percent, string StatusText)>? progress,
            CancellationToken ct)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var fileStream = File.Create(destinationPath);

            byte[] buffer = new byte[131072]; // 128 KB buffer
            int bytesRead;
            long totalRead = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            long lastReportTime = 0;
            long lastReportBytes = 0;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct).ConfigureAwait(false);
                totalRead += bytesRead;

                if (sw.ElapsedMilliseconds - lastReportTime > 200)
                {
                    double fileProgress = totalBytes.HasValue && totalBytes.Value > 0
                        ? (double)totalRead / totalBytes.Value
                        : 0.5;

                    double overallProgress = startFraction + (fileProgress * (endFraction - startFraction));

                    double elapsedSec = (sw.ElapsedMilliseconds - lastReportTime) / 1000.0;
                    double bytesPerSec = elapsedSec > 0 ? (totalRead - lastReportBytes) / elapsedSec : 0;
                    string speed = bytesPerSec > 1048576
                        ? $"{(bytesPerSec / 1048576.0):F1} MB/s"
                        : $"{(bytesPerSec / 1024.0):F0} KB/s";

                    string sizeInfo = totalBytes.HasValue
                        ? $"{(totalRead / 1048576.0):F1} / {(totalBytes.Value / 1048576.0):F1} MB"
                        : $"{(totalRead / 1048576.0):F1} MB";

                    string status = $"Downloading {label}... {sizeInfo} ({speed})";
                    ReportProgress(progress, overallProgress, status);

                    lastReportTime = sw.ElapsedMilliseconds;
                    lastReportBytes = totalRead;
                }
            }

            fileStream.Flush();
            fileStream.Close();

            // Hash verification if required
            if (!string.IsNullOrEmpty(expectedSha256))
            {
                ReportProgress(progress, endFraction, $"Verifying {label} integrity...");
                using var fs = File.OpenRead(destinationPath);
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(fs);
                string computedHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(computedHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"SHA-256 mismatch for {label}. Expected {expectedSha256}, got {computedHash}");
                }
            }
        }

        private static void ExtractWhisperCudaFiles(string zipPath, string targetDir)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                // Nupkg puts win-x64 dlls under build/win-x64/
                string fullName = entry.FullName.Replace('\\', '/');
                if (fullName.StartsWith("build/win-x64/", StringComparison.OrdinalIgnoreCase) &&
                    fullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = Path.GetFileName(fullName);
                    string dest = Path.Combine(targetDir, fileName);
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
        }

        private static void ExtractCudartFiles(string zipPath, string winX64Dir, string baseDir)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string name = Path.GetFileName(entry.FullName);
                if (string.Equals(name, "cudart64_13.dll", StringComparison.OrdinalIgnoreCase))
                {
                    entry.ExtractToFile(Path.Combine(winX64Dir, name), overwrite: true);
                    entry.ExtractToFile(Path.Combine(baseDir, name), overwrite: true);
                }
            }
        }

        private static void ExtractCublasFiles(string zipPath, string winX64Dir, string baseDir)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                string name = Path.GetFileName(entry.FullName);
                if (string.Equals(name, "cublas64_13.dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "cublasLt64_13.dll", StringComparison.OrdinalIgnoreCase))
                {
                    entry.ExtractToFile(Path.Combine(winX64Dir, name), overwrite: true);
                }
                else if (string.Equals(name, "LICENSE", StringComparison.OrdinalIgnoreCase))
                {
                    string licDest = Path.Combine(baseDir, "NVIDIA-CUDA-LICENSE.txt");
                    entry.ExtractToFile(licDest, overwrite: true);
                }
            }
        }
    }
}
