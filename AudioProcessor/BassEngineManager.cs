using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ManagedBass;

namespace NoIDSoftwork.AudioProcessor
{
    /// <summary>
    /// Manages native BASS library loading, device initialization, and lifecycle.
    /// </summary>
    public static class BassEngineManager
    {
        private static readonly object _lock = new();
        private static readonly HashSet<int> _initializedDevices = new();
        private static bool _isLoaded;
        private static bool _loadFailed;
        private static Exception? _loadException;

        static BassEngineManager()
        {
            EnsureNativeLibraryLoaded();
        }

        /// <summary>
        /// Returns true if the native BASS library was loaded successfully.
        /// </summary>
        public static bool IsAvailable
        {
            get
            {
                EnsureNativeLibraryLoaded();
                return _isLoaded;
            }
        }

        /// <summary>
        /// Gets the last exception encountered during native library loading, if any.
        /// </summary>
        public static Exception? LoadException => _loadException;

        /// <summary>
        /// Ensures the native bass.dll is present on disk and loaded into the process.
        /// </summary>
        public static void EnsureNativeLibraryLoaded()
        {
            if (_isLoaded || _loadFailed) return;

            lock (_lock)
            {
                if (_isLoaded || _loadFailed) return;

                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string targetDllPath = Path.Combine(baseDir, "bass.dll");

                    // If not on disk, attempt to extract from embedded resources
                    if (!File.Exists(targetDllPath))
                    {
                        var assembly = Assembly.GetExecutingAssembly();
                        using var stream = assembly.GetManifestResourceStream("bass.dll");
                        if (stream != null)
                        {
                            using var fs = new FileStream(targetDllPath, FileMode.Create, FileAccess.Write, FileShare.None);
                            stream.CopyTo(fs);
                        }
                    }

                    // Explicitly pre-load into process if target DLL exists
                    if (File.Exists(targetDllPath))
                    {
                        NativeLibrary.TryLoad(targetDllPath, out _);
                    }

                    // Configure DllImportResolver for ManagedBass assembly if supported
                    try
                    {
                        var bassAssembly = typeof(Bass).Assembly;
                        NativeLibrary.SetDllImportResolver(bassAssembly, (libraryName, asm, searchPath) =>
                        {
                            if (libraryName.Equals("bass", StringComparison.OrdinalIgnoreCase) ||
                                libraryName.Equals("bass.dll", StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(targetDllPath) && NativeLibrary.TryLoad(targetDllPath, out var handle))
                                {
                                    return handle;
                                }
                            }
                            return IntPtr.Zero;
                        });
                    }
                    catch { /* Resolver might already be set or not supported */ }

                    // Test call to verify BASS API functions
                    var version = Bass.Version;
                    if (version != null && version.Major > 0)
                    {
                        _isLoaded = true;

                        // Configure BASS for low-latency playback and high-frequency (10ms) meter updates
                        try
                        {
                            Bass.UpdatePeriod = 10;
                            Bass.PlaybackBufferLength = 100;
                            Bass.DeviceBufferLength = 10;
                            
                            // Initialize device 0 (no sound / decoding device) immediately
                            EnsureDeviceInitialized(0);
                        }
                        catch { }
                    }
                    else
                    {
                        _loadFailed = true;
                    }
                }
                catch (Exception ex)
                {
                    _loadFailed = true;
                    _loadException = ex;
                }
            }
        }

        /// <summary>
        /// Gets the index of the default audio output device in BASS (1..N), or 1 as fallback.
        /// </summary>
        public static int GetDefaultDeviceIndex()
        {
            if (!IsAvailable) return -1;
            try
            {
                int count = Bass.DeviceCount;
                for (int i = 1; i < count; i++)
                {
                    var info = Bass.GetDeviceInfo(i);
                    if (info.IsDefault && info.IsEnabled)
                    {
                        return i;
                    }
                }
                return count > 1 ? 1 : -1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Ensures the specified BASS device is initialized.
        /// </summary>
        /// <param name="bassDeviceIndex">The BASS device index (0 for decode-only, -1 for default, 1..N for specific device).</param>
        /// <param name="sampleRate">Sample rate (default 48000).</param>
        /// <returns>True if initialized successfully.</returns>
        public static bool EnsureDeviceInitialized(int bassDeviceIndex = -1, int sampleRate = 48000)
        {
            if (!IsAvailable) return false;

            lock (_lock)
            {
                if (bassDeviceIndex == 0)
                {
                    if (_initializedDevices.Contains(0)) return true;
                    bool success0 = Bass.Init(0, sampleRate, DeviceInitFlags.Default, IntPtr.Zero);
                    if (success0 || Bass.LastError == Errors.Already)
                    {
                        _initializedDevices.Add(0);
                        return true;
                    }
                    return false;
                }

                int resolvedDevice = bassDeviceIndex == -1 ? GetDefaultDeviceIndex() : bassDeviceIndex;

                if (resolvedDevice > 0 && _initializedDevices.Contains(resolvedDevice))
                {
                    return true;
                }
                if (bassDeviceIndex == -1 && _initializedDevices.Contains(-1))
                {
                    return true;
                }

                if (resolvedDevice > 0 && resolvedDevice < Bass.DeviceCount)
                {
                    var info = Bass.GetDeviceInfo(resolvedDevice);
                    if (info.IsInitialized)
                    {
                        _initializedDevices.Add(resolvedDevice);
                        if (bassDeviceIndex == -1) _initializedDevices.Add(-1);
                        return true;
                    }
                }

                int devToInit = resolvedDevice > 0 ? resolvedDevice : -1;
                bool success = Bass.Init(devToInit, sampleRate, DeviceInitFlags.Default, IntPtr.Zero);
                if (!success)
                {
                    var error = Bass.LastError;
                    // Errors.Already indicates already initialized on this device
                    if (error == Errors.Already)
                    {
                        if (resolvedDevice > 0) _initializedDevices.Add(resolvedDevice);
                        if (bassDeviceIndex == -1) _initializedDevices.Add(-1);
                        return true;
                    }
                    return false;
                }

                if (resolvedDevice > 0) _initializedDevices.Add(resolvedDevice);
                if (bassDeviceIndex == -1) _initializedDevices.Add(-1);
                return true;
            }
        }

        /// <summary>
        /// Resolves a PaDDY device index (-1 for default, 0..N-1 for specific device)
        /// to a BASS device index (-1 for default, 1..N for specific device).
        /// </summary>
        /// <param name="deviceIndex">The PaDDY device index (-1 for default, 0..N-1 for specific device).</param>
        /// <param name="targetFriendlyName">Optional friendly name of the target device for precision matching.</param>
        public static int ResolveBassDeviceIndex(int deviceIndex, string? targetFriendlyName = null)
        {
            if (deviceIndex < 0)
            {
                return -1; // BASS default device
            }

            if (!IsAvailable)
            {
                return -1;
            }

            // Attempt exact or fuzzy match by friendly name if provided
            if (!string.IsNullOrWhiteSpace(targetFriendlyName))
            {
                int count = Bass.DeviceCount;
                for (int i = 1; i < count; i++)
                {
                    var info = Bass.GetDeviceInfo(i);
                    if (info.IsEnabled && !string.IsNullOrEmpty(info.Name))
                    {
                        if (string.Equals(info.Name, targetFriendlyName, StringComparison.OrdinalIgnoreCase) ||
                            targetFriendlyName.Contains(info.Name, StringComparison.OrdinalIgnoreCase) ||
                            info.Name.Contains(targetFriendlyName, StringComparison.OrdinalIgnoreCase))
                        {
                            return i;
                        }
                    }
                }
            }

            // Fallback: 0-indexed device maps to 1-indexed in BASS
            int candidate = deviceIndex + 1;
            if (candidate < Bass.DeviceCount)
            {
                return candidate;
            }

            return -1;
        }

        /// <summary>
        /// Frees all initialized BASS devices.
        /// </summary>
        public static void FreeAll()
        {
            lock (_lock)
            {
                foreach (int dev in _initializedDevices)
                {
                    try
                    {
                        Bass.CurrentDevice = dev;
                        Bass.Free();
                    }
                    catch { }
                }
                _initializedDevices.Clear();
            }
        }
    }
}
