using System;
using System.IO;
using System.Text.Json;

namespace Paddy
{
    public class AppSettings
    {
        public int InputDeviceIndex { get; set; } = 0;
        public int OutputDeviceIndex { get; set; } = 0;
        public double Sensitivity { get; set; } = 30.0;        // RMS threshold 0-100
        public double SilenceTimeoutMs { get; set; } = 700.0;  // ms of silence before stopping
        public string SaveFolder { get; set; } = string.Empty;

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
