using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;

namespace PaDDY;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : WpfApplication
{
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

        var fontUri = new Uri($"pack://application:,,,/Themes/Fonts/{entry.FileName}");
        var appFont = Fonts.GetFontFamilies(fontUri).FirstOrDefault();
        if (appFont != null)
            Current.Resources["AppFont"] = appFont;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyFont(AppSettings.Load().AppFontVariant);
    }
}

