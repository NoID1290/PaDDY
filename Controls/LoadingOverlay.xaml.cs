using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace PaDDY.Controls
{
    public partial class LoadingOverlay : Microsoft.UI.Xaml.Controls.UserControl
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
            _animation?.Begin();
        }

        public void Hide()
        {
            _animation?.Stop();
            Visibility = Visibility.Collapsed;
        }
    }
}
