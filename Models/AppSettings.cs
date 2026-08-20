using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using NoIDSoftwork.AudioProcessor;
using PaDDY.Helpers;

namespace PaDDY
{
    public class AppSettings
    {
        public AudioEngineType AudioEngine { get; set; } = AudioEngineType.NAudio;
        public int InputDeviceIndex { get; set; } = 0;
        public int CaptureSourceMode { get; set; } = 0; // 0 = microphone, 1 = output loopback, 2 = app loopback
        public string LoopbackDeviceId { get; set; } = string.Empty;
        public uint AppLoopbackProcessId { get; set; } = 0;
        public int OutputDeviceIndex { get; set; } = 0;
        public bool ListenOutputEnabled { get; set; } = false;
        public int ListenOutputDeviceIndex { get; set; } = 0; // 0 = default, 1..N = devices 0..N-1
        public double Sensitivity { get; set; } = 30.0;        // RMS threshold 0-100
        public double SilenceTimeoutMs { get; set; } = 700.0;  // ms of silence before stopping
        public string SaveFolder { get; set; } = string.Empty;

        // Recording format (used for microphone capture; loopback uses OS-provided format)
        public int RecordSampleRate { get; set; } = 48000;
        public int RecordBitDepth { get; set; } = 16;
        public int RecordChannels { get; set; } = 2;

        // Buffer / KeyBuffer recording
        public int PastBufferDurationMs { get; set; } = 10000;
        public int RecordingMode { get; set; } = 0; // 0 = AutoVAD, 1 = KeyBuffer

        // Global hotkey for buffer capture (default: Ctrl+F9)
        public uint BufferHotKeyModifiers { get; set; } = 2;   // MOD_CONTROL
        public uint BufferHotKeyVk { get; set; } = 0x78;       // VK_F9

        // Persisted favorites (list of absolute file paths)
        public List<string> FavoriteFilePaths { get; set; } = new();

        // Max recordings before auto-cleanup (0 = unlimited). Favorites are exempt.
        public int MaxRecords { get; set; } = 0;

        public string RecordCodec { get; set; } = "wav";

        // VST Plugin Integration
        public string VstPluginPath { get; set; } = string.Empty;
        public string Vst3PluginPath { get; set; } = string.Empty;
        public List<string> UserVstPluginPaths { get; set; } = new();
        public bool AutoScanVstFolders { get; set; } = false;
        public List<string> VstScanFolders { get; set; } = new();
        public List<string> DisabledVstPluginPaths { get; set; } = new();
        public List<string> PendingDeletedVstPluginPaths { get; set; } = new();

        // Sort order for the recordings pad panel
        // 0 = Newest first, 1 = Oldest first, 2 = Name A→Z, 3 = Name Z→A, 4 = Longest, 5 = Shortest
        public int PadSortOrder { get; set; } = 0;

        // Favorites & Audio panel UI state
        public bool FavoritesPanelCollapsed { get; set; } = false;
        public bool RecordingsPanelCollapsed { get; set; } = true;
        public bool AudioPanelVisible { get; set; } = false;

        // Volume controls (0–100 range)
        public double InputVolume { get; set; } = 80.0;
        public double OutputVolume { get; set; } = 100.0;
        public double PadListenVolume { get; set; } = 100.0;

        // UI font variant: "normal", "condensed"
        public string AppFontVariant { get; set; } = "condensed";

        // New pad naming
        public string DefaultPadTitleTemplate { get; set; } = "Recording {timestamp}";
        public bool UseFocusedAppForPadTitle { get; set; } = false;

        // Trim Editor output device (0 = default, 1..N = devices 0..N-1)
        public int TrimEditorOutputDeviceIndex { get; set; } = 0;
        public bool NewRecordingsNonDestructive { get; set; } = false;

        // ---- Appearance ----
        // UI scaling factor: 0.50 (50%) to 2.00 (200%), default 1.0 (100%)
        public double UiScale { get; set; } = 1.0;
        // Language code: "en", "fr"
        public string Language { get; set; } = "en";
        // Overall theme: "dark", "light", "dark-green", "dark-blue", "sepia", "dark-pink", "dark-sepia", "cyberpunk", "nordic-frost", "sunset", "deep-teal", "dracula", "vista-aero", "windows-xp", "windows-98", "midnight-oled", "emerald-matrix", "amethyst-night", "tokyo-neon", "solarized-dark", "rose-gold", "ocean-abyss", "crimson-ember", "pastel-dream", "mocha-latte", "acid-cyber", "monochrome-slate", "synthwave-80s", "bioluminescence", "arctic-ice"
        public string Theme { get; set; } = "dark";
        // Audio meter skin: "default", "8bit", "70s", "neon", "grayscale", "inferno", "aurora", "cyber-sunset", "forest", "toxic", "vaporwave", "plasma", "matrix", "solar-flare", "ocean-wave", "sunset-strip", "vintage-led", "acid-lime", "blood-moon", "rainbow"
        public string MeterSkin { get; set; } = "default";
        public bool MeterDigitalDots { get; set; } = false;
        // Performance mode: CPU-only rendering, limited animations
        public bool PerformanceMode { get; set; } = false;
        // Pause all decorative animation rendering when PaDDY is not the active window
        public bool PauseAnimationsWhenUnfocused { get; set; } = false;
        // Preload and cache all audio clips in RAM/temp files on startup (default: true). Disable for low RAM PCs.
        public bool PreloadAudioCache { get; set; } = true;

        // ---- System tray / startup ----
        public bool RunOnWindowsStartup { get; set; } = false;
        public bool StartMinimizedInTray { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool CloseToTray { get; set; } = false;

        // ---- Window Position & State ----
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        public double WindowWidth { get; set; } = 1380.0;
        public double WindowHeight { get; set; } = 740.0;
        public int WindowState { get; set; } = 0; // 0 = Normal, 2 = Maximized

        // ---- Detection ----
        // 0 = RMS threshold (classic), 1 = Adaptive/spectral VAD (noise-floor calibrating)
        public int DetectionAlgorithm { get; set; } = 0;

        // Whisper speech auto-rename setting
        public bool AutoRenameWithSpeech { get; set; } = false;
        // If enabled alongside AutoRenameWithSpeech, cancel/discard recording if Whisper detects no spoken voice
        public bool CancelRecordingIfNoVoice { get; set; } = false;
        // Whisper model size: "tiny", "base", "small", "medium", "large"
        public string SpeechModel { get; set; } = "tiny";
        // Language code (e.g. "en", "auto")
        public string SpeechLanguage { get; set; } = "Auto";
        // Use CUDA GPU acceleration for Whisper (requires NVIDIA GPU)
        public bool UseCudaForSpeech { get; set; } = false;

        // ---- LUFS Normalization ----
        public bool AutoNormalizeOnCapture { get; set; } = false;
        public double TargetLoudnessLufs { get; set; } = -14.0;

        // ---- Global Effects (non-destructive, applied to ALL audio playback) ----
        /// <summary>Enable the Auto Fade In/Out global effect for all pad playback.</summary>
        public bool GlobalFadeEnabled { get; set; } = false;
        /// <summary>Global fade-in duration in milliseconds (applied at start of each clip).</summary>
        public double GlobalFadeInDurationMs { get; set; } = 500.0;
        /// <summary>Global fade-out duration in milliseconds (applied at end of each clip).</summary>
        public double GlobalFadeOutDurationMs { get; set; } = 500.0;

        /// <summary>Allow playing more than one pad simultaneously (polyphonic mode).</summary>
        public bool AllowMultiPadPlayback { get; set; } = true;

        // ---- Live Mic Modulator & Dual-Bus Routing ----
        public bool LiveMicEnabled { get; set; } = false;
        public int LiveMicDeviceIndex { get; set; } = 0;
        public int LiveMicOutputDeviceIndex { get; set; } = 0;
        public bool LiveMicFxEnabled { get; set; } = false;
        public double LiveMicGain { get; set; } = 1.0;
        public bool DualOutputEnabled { get; set; } = false;
        public int SecondaryOutputDeviceIndex { get; set; } = 0;

        // ---- AI Speech Auto Indexing ----
        public bool AutoSpeechIndexingEnabled { get; set; } = true;

        // ---- Custom pad pages ----
        // Ordered list of user pad pages. The first page is the default ("Favorites" semantics).
        public List<PadPage> PadPages { get; set; } = new();
        // Id of the currently active pad page (empty = first/all view).
        public string ActivePadPageId { get; set; } = string.Empty;

        // ---- Discord Rich Presence ----
        public bool DiscordRichPresenceEnabled { get; set; } = false;
        public long DiscordClientId { get; set; } = 461618159171141643;

        // ---- Auto-update ----
        public bool AutoInstallUpdates { get; set; } = false;
        public bool DownloadBetaUpdates { get; set; } = false;

        private static readonly MessagePackSerializerOptions SerializerOptions =
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        public static AppSettings Load()
        {
            try
            {
                AppDataPaths.EnsureAppDataRoot();

                if (File.Exists(AppDataPaths.SettingsPath))
                {
                    var bytes = File.ReadAllBytes(AppDataPaths.SettingsPath);
                    var s = MessagePackSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                    if (s != null)
                    {
                        s.MigrateLegacyVstPaths();
                        return s;
                    }
                }

                if (AppDataPaths.TryMigrateLegacyFile(AppDataPaths.LegacySettingsPath, AppDataPaths.SettingsPath) &&
                    File.Exists(AppDataPaths.SettingsPath))
                {
                    var bytes = File.ReadAllBytes(AppDataPaths.SettingsPath);
                    var s = MessagePackSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                    if (s != null)
                    {
                        s.MigrateLegacyVstPaths();
                        return s;
                    }
                }

                // Migrate from old %LocalAppData%\PaDDY location.
                if (AppDataPaths.TryMigrateLegacyFile(AppDataPaths.LegacyAppDataSettingsPath, AppDataPaths.SettingsPath) &&
                    File.Exists(AppDataPaths.SettingsPath))
                {
                    var bytes = File.ReadAllBytes(AppDataPaths.SettingsPath);
                    var s = MessagePackSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                    if (s != null)
                    {
                        s.MigrateLegacyVstPaths();
                        return s;
                    }
                }

                // Migrate once from legacy JSON settings if present.
                if (File.Exists(AppDataPaths.LegacyJsonSettingsPath))
                {
                    var json = File.ReadAllText(AppDataPaths.LegacyJsonSettingsPath);
                    var migrated = JsonSerializer.Deserialize<AppSettings>(json);
                    if (migrated != null)
                    {
                        migrated.MigrateLegacyVstPaths();
                        migrated.Save();
                        return migrated;
                    }
                }
            }
            catch { /* fall through to defaults */ }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                AppDataPaths.EnsureAppDataRoot();
                var bytes = MessagePackSerializer.Serialize(this, SerializerOptions);
                File.WriteAllBytes(AppDataPaths.SettingsPath, bytes);
            }
            catch { /* non-critical */ }
        }

        /// <summary>
        /// Moves the legacy single VST2 path into <see cref="UserVstPluginPaths"/>
        /// so the settings dialog can manage multiple plugins.
        /// </summary>
        private void MigrateLegacyVstPaths()
        {
            UserVstPluginPaths ??= new List<string>();

            if (!string.IsNullOrWhiteSpace(VstPluginPath))
            {
                if (!UserVstPluginPaths.Contains(VstPluginPath, StringComparer.OrdinalIgnoreCase))
                    UserVstPluginPaths.Add(VstPluginPath);
                VstPluginPath = string.Empty;
            }

            UserVstPluginPaths.RemoveAll(string.IsNullOrWhiteSpace);
        }

        /// <summary>
        /// Guarantees a default "Favorites" pad page exists and that
        /// <see cref="ActivePadPageId"/> points at a real page. Returns the default page.
        /// </summary>
        public PadPage EnsurePadPages()
        {
            PadPages ??= new List<PadPage>();

            PadPage? favorites = PadPages.Find(p => p.IsFavorites);
            if (favorites == null)
            {
                favorites = new PadPage
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Favorites",
                    Order = 0,
                    IsFavorites = true
                };
                PadPages.Insert(0, favorites);
            }

            if (string.IsNullOrEmpty(ActivePadPageId) ||
                PadPages.Find(p => p.Id == ActivePadPageId) == null)
            {
                ActivePadPageId = favorites.Id;
            }

            return favorites;
        }

        public void ResetToDefaults()
        {
            var fresh = new AppSettings();
            var props = typeof(AppSettings).GetProperties();
            foreach (var prop in props)
            {
                if (prop.CanWrite)
                {
                    prop.SetValue(this, prop.GetValue(fresh));
                }
            }
        }
    }
}