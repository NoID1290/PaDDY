using System;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PaDDY.Helpers
{
    /// <summary>
    /// Detects whether an NVIDIA GPU is present on the system by querying
    /// WMI. The result is cached because GPU hardware does not change at
    /// runtime.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class GpuHelper
    {
        private static bool? _isNvidiaAvailable;
        private static bool? _isCudaRuntimeAvailable;

        /// <summary>
        /// Returns <c>true</c> when at least one NVIDIA video adapter is
        /// reported by the OS.
        /// </summary>
        public static bool IsNvidiaGpuAvailable
        {
            get
            {
                _isNvidiaAvailable ??= DetectNvidiaGpu();
                return _isNvidiaAvailable.Value;
            }
        }

        /// <summary>
        /// Returns <c>true</c> when the Whisper CUDA native runtime can actually
        /// be loaded. ggml-cuda-whisper.dll links cublas64_13.dll (which needs
        /// cublasLt64_13.dll), and Whisper.net additionally probes cudart64_13.dll
        /// next to the exe to detect CUDA devices. These redistributables are
        /// shipped with the app; when any is missing, Whisper silently falls back
        /// to the CPU runtime even though an NVIDIA GPU is present.
        /// </summary>
        public static bool IsCudaRuntimeAvailable
        {
            get
            {
                _isCudaRuntimeAvailable ??= DetectCudaRuntime();
                return _isCudaRuntimeAvailable.Value;
            }
        }

        private static bool DetectCudaRuntime()
        {
            if (!IsNvidiaGpuAvailable)
                return false;

            try
            {
                // Whisper.net's CUDA gate: cudart64_13.dll resolvable by name.
                IntPtr cudart = LoadLibraryExW("cudart64_13.dll", IntPtr.Zero, 0);
                if (cudart == IntPtr.Zero)
                    return false;
                FreeLibrary(cudart);

                string dll = Path.Combine(
                    AppContext.BaseDirectory, "runtimes", "cuda", "win-x64", "ggml-cuda-whisper.dll");
                if (!File.Exists(dll))
                    return false;

                const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;
                const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
                IntPtr handle = LoadLibraryExW(
                    dll, IntPtr.Zero,
                    LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
                if (handle == IntPtr.Zero)
                    return false;

                FreeLibrary(handle);
                return true;
            }
            catch
            {
                return false;
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

        [DllImport("kernel32.dll", EntryPoint = "FreeLibrary", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeLibrary(IntPtr hLibModule);

        private static bool DetectNvidiaGpu()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_VideoController");

                foreach (ManagementObject obj in searcher.Get())
                {
                    string? name = obj["Name"]?.ToString();
                    if (!string.IsNullOrEmpty(name) &&
                        name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // WMI unavailable – assume no NVIDIA GPU.
            }

            return false;
        }
    }
}
