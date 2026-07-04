using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using Microsoft.UI.Xaml;

namespace PaDDY.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();

            // Configure window appearance (replaces WPF XAML attributes)
            Title = "Starting PaDDY...";
            var appWindow = this.AppWindow;
            appWindow.Resize(new Windows.Graphics.SizeInt32(250, 250));
            var presenter = appWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsResizable = false;
            }

            SplashLoadingOverlay.Show("Loading data...");
        }

        public void UpdateMessage(string message)
        {
            DispatcherQueue.TryEnqueue(() => SplashLoadingOverlay.Show(message));
        }
    }
}
