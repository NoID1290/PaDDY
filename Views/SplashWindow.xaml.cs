using System.Windows;

namespace PaDDY.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            SplashLoadingOverlay.Show("Loading data...");
        }

        public void UpdateMessage(string message)
        {
            Dispatcher.Invoke(() => SplashLoadingOverlay.Show(message));
        }
    }
}
