using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MessagePack;
using MessagePack.Resolvers;

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

        // Volume controls (0–100 range)
        public double InputVolume { get; set; } = 80.0;
        public double OutputVolume { get; set; } = 100.0;
        public double PadListenVolume { get; set; } = 100.0;

        // UI font variant: "regular", "bold", "condensed", "condensed-bold", "display", "condensed-display"
        public string AppFontVariant { get; set; } = "condensed-display";

        private static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "usrcfg.bin");

        private static readonly string LegacySettingsPath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        private static readonly MessagePackSerializerOptions SerializerOptions =
            MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var bytes = File.ReadAllBytes(SettingsPath);
                    var s = MessagePackSerializer.Deserialize<AppSettings>(bytes, SerializerOptions);
                    if (s != null) return s;
                }

                // Migrate once from legacy JSON settings if present.
                if (File.Exists(LegacySettingsPath))
                {
                    var json = File.ReadAllText(LegacySettingsPath);
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
                var bytes = MessagePackSerializer.Serialize(this, SerializerOptions);
                File.WriteAllBytes(SettingsPath, bytes);
            }
            catch { /* non-critical */ }
        }
    }
}
