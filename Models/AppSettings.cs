using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;
using PaDDY.Helpers;

namespace PaDDY
{
    public class AppSettings
    {
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

        // Output codec for new recordings: "wav", "mp3", "opus", "ogg"
        public string RecordCodec { get; set; } = "wav";

        // Sort order for the recordings pad panel
        // 0 = Newest first, 1 = Oldest first, 2 = Name A→Z, 3 = Name Z→A, 4 = Longest, 5 = Shortest
        public int PadSortOrder { get; set; } = 0;

        // Favorites section UI state
        public bool FavoritesPanelCollapsed { get; set; } = false;

        // Volume controls (0–100 range)
        public double InputVolume { get; set; } = 80.0;
        public double OutputVolume { get; set; } = 100.0;
        public double PadListenVolume { get; set; } = 100.0;

        // UI font variant: "regular", "bold", "condensed", "condensed-bold", "display", "condensed-display"
        public string AppFontVariant { get; set; } = "condensed-display";

        // New pad naming
        public string DefaultPadTitleTemplate { get; set; } = "Recording {timestamp}";
        public bool UseFocusedAppForPadTitle { get; set; } = false;

        // Trim Editor output device (0 = default, 1..N = devices 0..N-1)
        public int TrimEditorOutputDeviceIndex { get; set; } = 0;
        public bool NewRecordingsNonDestructive { get; set; } = false;

        // ---- Overlay ----
        public bool OverlayEnabled { get; set; } = false;
        public int OverlayFrameRateCap { get; set; } = 60;
        public double OverlayOpacity { get; set; } = 0.9;

        // ---- Appearance ----
        // Overall theme: "dark", "light", "dark-green", "dark-blue", "sepia", "dark-pink", "dark-sepia", "cyberpunk", "nordic-frost", "sunset", "deep-teal", "dracula"
        public string Theme { get; set; } = "dark";
        // Audio meter skin: "default", "8bit", "70s", "neon", "grayscale", "inferno", "aurora", "cyber-sunset", "forest", "toxic"
        public string MeterSkin { get; set; } = "default";
        public bool MeterDigitalDots { get; set; } = false;
        // Performance mode: CPU-only rendering, limited animations
        public bool PerformanceMode { get; set; } = false;

        // ---- System tray / startup ----
        public bool RunOnWindowsStartup { get; set; } = false;
        public bool StartMinimizedInTray { get; set; } = false;
        public bool MinimizeToTray { get; set; } = false;
        public bool CloseToTray { get; set; } = false;

        // ---- Detection ----
        // 0 = RMS threshold (classic), 1 = Adaptive/spectral VAD (noise-floor calibrating)
        public int DetectionAlgorithm { get; set; } = 0;

        // ---- Speech-to-text auto-rename ----
        public bool AutoRenameWithSpeech { get; set; } = false;
        // Whisper model size: "tiny", "base", "small", "medium", "large"
        public string SpeechModel { get; set; } = "base";
        // Language code (e.g. "en", "auto")
        public string SpeechLanguage { get; set; } = "Auto";
        // Use CUDA GPU acceleration for Whisper (requires NVIDIA GPU)
        public bool UseCudaForSpeech { get; set; } = false;

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
                    if (s != null) return s;
                }

                if (AppDataPaths.TryMigrateLegacyFile(AppDataPaths.LegacySettingsPath, AppDataPaths.SettingsPath) &&
                    File.Exists(AppDataPaths.SettingsPath))
                {
                    var bytes = File.ReadAllBytes(AppDataPaths.SettingsPath);
                    var s = MessagePackSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                    if (s != null) return s;
                }

                // Migrate from old %LocalAppData%\PaDDY location.
                if (AppDataPaths.TryMigrateLegacyFile(AppDataPaths.LegacyAppDataSettingsPath, AppDataPaths.SettingsPath) &&
                    File.Exists(AppDataPaths.SettingsPath))
                {
                    var bytes = File.ReadAllBytes(AppDataPaths.SettingsPath);
                    var s = MessagePackSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                    if (s != null) return s;
                }

                // Migrate once from legacy JSON settings if present.
                if (File.Exists(AppDataPaths.LegacyJsonSettingsPath))
                {
                    var json = File.ReadAllText(AppDataPaths.LegacyJsonSettingsPath);
                    var migrated = JsonSerializer.Deserialize<AppSettings>(json);
                    if (migrated != null)
                    {
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
    }
}