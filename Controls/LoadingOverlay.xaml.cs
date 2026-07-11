using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace PaDDY.Controls
{
    public partial class LoadingOverlay : System.Windows.Controls.UserControl
    {
        private Storyboard _animation;

        public LoadingOverlay()
        {
            InitializeComponent();
            _animation = (Storyboard)Resources["LoadingAnimation"];
        }

        public void Show(string message = "Processing...")
        {
            LoadingText.Text = message;
            Visibility = Visibility.Visible;
            _animation?.Begin(this, true);
        }

        public void Hide()
        {
            _animation?.Stop(this);
            Visibility = Visibility.Collapsed;
        }
    }
}
