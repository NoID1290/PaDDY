using System;
using System.IO;

namespace PaDDY.Helpers;

internal static class AppDataPaths
{
    private const string AppFolderName = "PaDDY";

    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string SettingsPath => Path.Combine(AppDataRoot, "usrcfg.bin");

    public static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "usrcfg.bin");

    public static string LegacyJsonSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string RecordingStorePath => Path.Combine(AppDataRoot, "recordings.dat");

    public static string LegacyRecordingStorePath => Path.Combine(AppContext.BaseDirectory, "recordings.dat");

    public static string InternalRecordingTempDir => Path.Combine(AppDataRoot, ".rec_tmp");

    public static string PlaybackTempDir => Path.Combine(Path.GetTempPath(), "paddy-tmp");

    public static void EnsureAppDataRoot()
    {
        Directory.CreateDirectory(AppDataRoot);
    }

    public static bool TryMigrateLegacyFile(string legacyPath, string targetPath)
    {
        try
        {
            if (File.Exists(targetPath) || !File.Exists(legacyPath))
                return false;

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(legacyPath, targetPath, overwrite: false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}