using System;
using System.IO;

namespace PaDDY.Helpers;

internal static class AppDataPaths
{
    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    // Current data root: %LocalAppData%\NoID Softwork\PaDDY
    public static string AppDataRoot =>
        Path.Combine(LocalAppData, "NoID Softwork", "PaDDY");

    // Legacy data root from previous release: %LocalAppData%\PaDDY
    private static string LegacyAppDataRoot =>
        Path.Combine(LocalAppData, "PaDDY");

    public static string SettingsPath => Path.Combine(AppDataRoot, "usrcfg.bin");

    // Legacy: settings stored alongside the exe (very old installs)
    public static string LegacySettingsPath => Path.Combine(AppContext.BaseDirectory, "usrcfg.bin");

    // Legacy: settings stored in the old %LocalAppData%\PaDDY location
    public static string LegacyAppDataSettingsPath => Path.Combine(LegacyAppDataRoot, "usrcfg.bin");

    public static string LegacyJsonSettingsPath => Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static string RecordingStorePath => Path.Combine(AppDataRoot, "recordings.dat");

    // Legacy: recordings stored alongside the exe (very old installs)
    public static string LegacyRecordingStorePath => Path.Combine(AppContext.BaseDirectory, "recordings.dat");

    // Legacy: recordings stored in the old %LocalAppData%\PaDDY location
    public static string LegacyAppDataRecordingStorePath => Path.Combine(LegacyAppDataRoot, "recordings.dat");

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