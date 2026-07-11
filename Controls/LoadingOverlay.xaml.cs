using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Threading.Tasks;

namespace PaDDY.Controls
{
    public partial class LoadingOverlay : System.Windows.Controls.UserControl
    {
        private Storyboard _animation;
        private int _hideToken = 0;

        public LoadingOverlay()
        {
            InitializeComponent();
            _animation = (Storyboard)Resources["LoadingAnimation"];
        }

        public void Show(string message = "Processing...")
        {
            _hideToken++; // Cancel any pending hides
            LoadingText.Text = message;
            Visibility = Visibility.Visible;
            _animation?.Begin(this, true);
        }

        public async void Hide()
        {
            LoadingText.Text = " Ready :)";
            int currentToken = ++_hideToken;
            
            await Task.Delay(2000);
            
            if (currentToken == _hideToken)
            {
                _animation?.Stop(this);
                Visibility = Visibility.Collapsed;
            }
        }
    }
}
