using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace PaDDY;

/// <summary>
/// Interaction logic for App.xaml — WinUI 3 version.
/// </summary>
public partial class App : Application
{
    private Mutex? _instanceMutex;
    private Window? _mainWindow;

    /// <summary>
    /// When the app is launched via a .PADBACK file association, the file path
    /// is stored here so the MainWindow can prompt for restore after loading.
    /// </summary>
    internal static string? PendingRestoreFilePath { get; private set; }

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

        // WinUI 3: Use ms-appx:/// URI scheme for packaged, or relative path for unpackaged.
        // For unpackaged apps, fonts deployed as Content items are in the output folder.
        var appFont = new FontFamily($"Themes/Fonts/{entry.FileName}#PaDDY Font");
        Current.Resources["AppFont"] = appFont;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // WinUI 3 doesn't pass CLI args via LaunchActivatedEventArgs for unpackaged apps.
        // Use Environment.GetCommandLineArgs() instead.
        var cliArgs = System.Environment.GetCommandLineArgs().Skip(1).ToArray();

        if (cliArgs.Length >= 2 && cliArgs[0] == "--download-models")
        {
            string targetDir = cliArgs[1];
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
            // WinUI 3 doesn't have MessageBox; use Win32 MessageBox via P/Invoke.
            NativeMessageBox("PaDDY is already running.", "PaDDY");
            System.Environment.Exit(0);
            return;
        }

        // Check if the app was launched by opening a .PADBACK file (file association).
        // The OS passes the file path as the first (non-flag) argument.
        var padbackArg = cliArgs.FirstOrDefault(a =>
            a.EndsWith(".PADBACK", System.StringComparison.OrdinalIgnoreCase) &&
            System.IO.File.Exists(a));
        if (padbackArg != null)
            PendingRestoreFilePath = padbackArg;

        var settings = AppSettings.Load();
        ApplyFont(settings.AppFontVariant);
        Helpers.ThemeManager.ApplyTheme(settings.Theme);
        Helpers.ThemeManager.ApplyMeterSkin(settings.MeterSkin, settings.MeterDigitalDots);
        Helpers.ThemeManager.ApplyPerformanceMode(settings.PerformanceMode);

        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }

    /// <summary>
    /// Simple Win32 MessageBox for cases where no XAML window is available yet.
    /// </summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBoxW(System.IntPtr hWnd, string text, string caption, uint type);

    private static void NativeMessageBox(string text, string caption)
    {
        const uint MB_OK = 0x00000000;
        const uint MB_ICONINFORMATION = 0x00000040;
        MessageBoxW(System.IntPtr.Zero, text, caption, MB_OK | MB_ICONINFORMATION);
    }
}
