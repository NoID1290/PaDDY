using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using Discord;

namespace PaDDY.Services
{
    public sealed class DiscordService : IDisposable
    {
        private static readonly Lazy<DiscordService> _instance = new(() => new DiscordService());
        public static DiscordService Instance => _instance.Value;

        /// <summary>
        /// False when the native discord_game_sdk DLL could not be extracted or loaded.
        /// All public entry points silently no-op when this is false.
        /// </summary>
        private static readonly bool _nativeAvailable;

        static DiscordService()
        {
            _nativeAvailable = false;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("discord_game_sdk.dll");
                if (stream == null)
                {
                    Debug.WriteLine("[Discord Service] Embedded discord_game_sdk.dll resource not found. Discord disabled.");
                    return;
                }

                string extractDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NoIDSoftwork", "PaDDY", "native");
                Directory.CreateDirectory(extractDir);
                string extractPath = Path.Combine(extractDir, "discord_game_sdk.dll");

                // Only re-extract if missing or size changed (updated build).
                bool needsExtract = true;
                if (File.Exists(extractPath))
                {
                    var fi = new FileInfo(extractPath);
                    needsExtract = fi.Length != stream.Length;
                }

                if (needsExtract)
                {
                    using var fs = File.Create(extractPath);
                    stream.CopyTo(fs);
                }

                // Register a resolver so DllImport("discord_game_sdk") finds our extracted copy.
                NativeLibrary.SetDllImportResolver(asm, (name, assembly, searchPath) =>
                {
                    if (name == "discord_game_sdk")
                    {
                        if (NativeLibrary.TryLoad(extractPath, out IntPtr handle))
                            return handle;
                    }
                    return IntPtr.Zero;
                });

                _nativeAvailable = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discord Service] Failed to extract native DLL, Discord disabled: {ex.Message}");
            }
        }

        private Discord.Discord? _discord;
        private DispatcherTimer? _callbackTimer;
        private bool _isConnecting;
        private bool _enabled;
        private long _clientId = 461618159171141643; // Default test client ID (Discord Game SDK app)
        private DateTime? _startTimestamp;

        private string? _currentDetails;
        private string? _currentState;
        private bool _currentHasTimestamp;
        private int _retryTicks = 0;
        private bool _disposed;

        private DiscordService()
        {
            _enabled = false;
        }

        public void Initialize(bool enabled, long clientId)
        {
            if (!_nativeAvailable) return;

            bool wasEnabled = _enabled;
            long oldClientId = _clientId;

            _enabled = enabled;
            _clientId = clientId;

            if (_enabled)
            {
                if (!wasEnabled || oldClientId != _clientId)
                {
                    Stop();
                    Start();
                }
            }
            else
            {
                Stop();
            }
        }

        public void Start()
        {
            if (!_nativeAvailable) return;
            if (_discord != null || _isConnecting) return;

            _isConnecting = true;
            _startTimestamp = DateTime.UtcNow;
            _retryTicks = 0;

            // Start a timer to poll callbacks and attempt connection if needed
            if (_callbackTimer == null)
            {
                _callbackTimer = new DispatcherTimer(DispatcherPriority.Background);
                _callbackTimer.Interval = TimeSpan.FromMilliseconds(200);
                _callbackTimer.Tick += CallbackTimer_Tick;
            }
            _callbackTimer.Start();

            // Run initial connection attempt in a background task
            Task.Run(() =>
            {
                TryConnect();
            });
        }

        public void Stop()
        {
            _callbackTimer?.Stop();
            
            if (_discord != null)
            {
                try
                {
                    _discord.GetActivityManager().ClearActivity((result) => { });
                    // Let callbacks run one final time to flush the clear activity call
                    _discord.RunCallbacks();
                }
                catch { }

                try
                {
                    _discord.Dispose();
                }
                catch { }
                _discord = null;
            }

            _isConnecting = false;
            _startTimestamp = null;
        }

        private void TryConnect()
        {
            if (!_enabled)
            {
                _isConnecting = false;
                return;
            }

            try
            {
                // Note: NoRequireDiscord prevents Discord from restarting our application if Discord is closed/not running.
                var newDiscord = new Discord.Discord(_clientId, (ulong)CreateFlags.NoRequireDiscord);
                
                newDiscord.SetLogHook(LogLevel.Debug, (level, message) =>
                {
                    Debug.WriteLine($"[Discord SDK] {level}: {message}");
                });

                _discord = newDiscord;
                _isConnecting = false;

                // Set initial activity
                UpdateActivityInternal();
            }
            catch (ResultException ex)
            {
                Debug.WriteLine($"[Discord Service] Failed to initialize: {ex.Result}");
                _discord = null;
                _isConnecting = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discord Service] Unexpected error: {ex.Message}");
                _discord = null;
                _isConnecting = false;
            }
        }

        private void CallbackTimer_Tick(object? sender, EventArgs e)
        {
            if (_discord != null)
            {
                try
                {
                    _discord.RunCallbacks();
                }
                catch (ResultException ex)
                {
                    Debug.WriteLine($"[Discord Service] Error during RunCallbacks: {ex.Result}. Reconnecting...");
                    Stop();
                    Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Discord Service] RunCallbacks exception: {ex.Message}");
                    Stop();
                    Start();
                }
            }
            else if (!_isConnecting && _enabled)
            {
                // Periodically retry connecting every 10 seconds (50 ticks of 200ms)
                _retryTicks++;
                if (_retryTicks >= 50)
                {
                    _retryTicks = 0;
                    _isConnecting = true;
                    Task.Run(() => TryConnect());
                }
            }
        }

        public void UpdateActivity(string details, string state, bool showTimestamp = false)
        {
            _currentDetails = details;
            _currentState = state;
            _currentHasTimestamp = showTimestamp;

            if (_discord != null)
            {
                UpdateActivityInternal();
            }
        }

        private void UpdateActivityInternal()
        {
            if (_discord == null) return;

            try
            {
                var activity = new Discord.Activity
                {
                    Details = _currentDetails ?? "Managing audio clips",
                    State = _currentState ?? "Idle",
                    Assets = new ActivityAssets
                    {
                        LargeImage = "logo", 
                        LargeText = "PaDDY"
                    }
                };

                if (_currentHasTimestamp && _startTimestamp.HasValue)
                {
                    var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    activity.Timestamps = new ActivityTimestamps
                    {
                        Start = (long)(_startTimestamp.Value - epoch).TotalSeconds
                    };
                }

                _discord.GetActivityManager().UpdateActivity(activity, (result) =>
                {
                    if (result != Result.Ok)
                    {
                        Debug.WriteLine($"[Discord Service] UpdateActivity failed: {result}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Discord Service] Exception updating activity: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
