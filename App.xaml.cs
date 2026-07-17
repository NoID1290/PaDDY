using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace PaDDY;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : WpfApplication
{
    private Mutex? _instanceMutex;

    /// <summary>
    /// When the app is launched via a .PADBACK file association, the file path
    /// is stored here so the MainWindow can prompt for restore after loading.
    /// </summary>
    internal static string? PendingRestoreFilePath { get; private set; }

    /// <summary>
    /// True when the app was relaunched by the INNO installer after a silent update,
    /// indicating that we should restore the auto-backup.
    /// </summary>
    internal static bool PendingUpdateRestore { get; private set; }

    /// <summary>Maps variant key → (embedded file name, display name).</summary>
    internal static readonly IReadOnlyList<(string Key, string FileName, string DisplayName)> FontVariants =
    [
        ("regular",           "ari-w9500.ttf",                  "Regular"),
        ("bold",              "ari-w9500-bold.ttf",             "Bold"),
        ("condensed",         "ari-w9500-condensed.ttf",        "Condensed"),
        ("condensed-bold",    "ari-w9500-condensed-bold.ttf",   "Condensed Bold"),
        ("display",           "ari-w9500-display.ttf",          "Display"),
        ("condensed-display", "ari-w9500-condensed-display.ttf","Condensed Display"),
    ];

    /// <summary>Loads the font for <paramref name="variantKey"/> and sets the app-wide AppFont resource.</summary>
    public static void ApplyFont(string variantKey)
    {
        var entry = FontVariants.FirstOrDefault(v => v.Key == variantKey);
        if (entry == default) entry = FontVariants.First(v => v.Key == "condensed-display");

        // Two-argument overload correctly enumerates families from the specific embedded file.
        var appFont = Fonts.GetFontFamilies(
            new Uri("pack://application:,,,/"),
            $"/Themes/Fonts/{entry.FileName}"
        ).FirstOrDefault();

        if (appFont != null)
            Current.Resources["AppFont"] = appFont;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 2 && e.Args[0] == "--download-models")
        {
            string targetDir = e.Args[1];
            System.IO.Directory.CreateDirectory(targetDir);
            
            var types = new[] { Whisper.net.Ggml.GgmlType.Tiny, Whisper.net.Ggml.GgmlType.Base, Whisper.net.Ggml.GgmlType.Small };
            foreach (var type in types)
            {
                string fileName = $"ggml-{type.ToString().ToLowerInvariant()}.bin";
                string destPath = System.IO.Path.Combine(targetDir, fileName);
                if (!System.IO.File.Exists(destPath))
                {
                    using var stream = await Whisper.net.Ggml.WhisperGgmlDownloader.Default.GetGgmlModelAsync(type);
                    using var fileStream = System.IO.File.Create(destPath);
                    await stream.CopyToAsync(fileStream);
                }
            }
            System.Environment.Exit(0);
            return;
        }

        _instanceMutex = new Mutex(true, "PaDDY_SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show("PaDDY is already running.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);

        // Check if the app was launched by opening a .PADBACK file (file association).
        // The OS passes the file path as the first (non-flag) argument.
        var padbackArg = e.Args.FirstOrDefault(a =>
            a.EndsWith(".PADBACK", System.StringComparison.OrdinalIgnoreCase) &&
            System.IO.File.Exists(a));
        if (padbackArg != null)
            PendingRestoreFilePath = padbackArg;

        // Check if this is a post-update restart (launched by INNO with --restore-update).
        if (e.Args.Contains("--restore-update"))
            PendingUpdateRestore = true;

        var settings = AppSettings.Load();
        ApplyFont(settings.AppFontVariant);
        Helpers.ThemeManager.ApplyTheme(settings.Theme);
        Helpers.ThemeManager.ApplyMeterSkin(settings.MeterSkin, settings.MeterDigitalDots);
        Helpers.ThemeManager.ApplyPerformanceMode(settings.PerformanceMode);

        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
        {
            var currentSettings = AppSettings.Load();
            if (currentSettings.Theme == "system")
            {
                Dispatcher?.Invoke(() =>
                {
                    Helpers.ThemeManager.ApplyTheme("system");
                });
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

