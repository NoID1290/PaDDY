using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Paddy
{
    public class AppSettings
    {
        public int InputDeviceIndex { get; set; } = 0;
        public int CaptureSourceMode { get; set; } = 0; // 0 = microphone, 1 = output loopback
        public string LoopbackDeviceId { get; set; } = string.Empty;
        public int OutputDeviceIndex { get; set; } = 0;
        public bool ListenOutputEnabled { get; set; } = false;
        public int ListenOutputDeviceIndex { get; set; } = 0; // 0 = default, 1..N = devices 0..N-1
        public double Sensitivity { get; set; } = 30.0;        // RMS threshold 0-100
        public double SilenceTimeoutMs { get; set; } = 700.0;  // ms of silence before stopping
        public string SaveFolder { get; set; } = string.Empty;

        // Recording format
        public int RecordSampleRate { get; set; } = 16000;
        public int RecordBitDepth { get; set; } = 16;
        public int RecordChannels { get; set; } = 1;

        // Buffer / KeyBuffer recording
        public int PastBufferDurationMs { get; set; } = 10000;
        public int RecordingMode { get; set; } = 0; // 0 = AutoVAD, 1 = KeyBuffer

        // Global hotkey for buffer capture (default: Ctrl+F9)
        public uint BufferHotKeyModifiers { get; set; } = 2;   // MOD_CONTROL
        public uint BufferHotKeyVk { get; set; } = 0x78;       // VK_F9

        // Persisted favorites (list of absolute file paths)
        public List<string> FavoriteFilePaths { get; set; } = new();

        private static readonly string SettingsPath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var s = JsonSerializer.Deserialize<AppSettings>(json);
                    if (s != null) return s;
                }
            }
            catch { /* fall through to defaults */ }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { /* non-critical */ }
        }
    }
}
