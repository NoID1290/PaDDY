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

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, "PaDDY_SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show("PaDDY is already running.", "PaDDY", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var settings = AppSettings.Load();
        ApplyFont(settings.AppFontVariant);
        Helpers.ThemeManager.ApplyTheme(settings.Theme);
        Helpers.ThemeManager.ApplyMeterSkin(settings.MeterSkin);
        Helpers.ThemeManager.ApplyPerformanceMode(settings.PerformanceMode);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}

