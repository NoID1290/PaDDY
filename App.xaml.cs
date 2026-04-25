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
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load the embedded custom font and register it as the app-wide AppFont resource.
        // DynamicResource references throughout the XAML will pick this up automatically.
        var fontUri = new Uri("pack://application:,,,/Themes/Fonts/");
        var appFont = Fonts.GetFontFamilies(fontUri).FirstOrDefault();
        if (appFont != null)
            Resources["AppFont"] = appFont;
    }
}

