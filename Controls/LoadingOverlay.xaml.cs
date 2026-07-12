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
        
        private static readonly string[] ReadyMessages = new string[]
        {
            "Ready :)",
            "All set!",
            "Let's go!",
            "Good to go!",
            "Fully loaded!",
            "Standing by!",
            "System online!",
            "Tabarnak!",
            "Beep boop",
            "One step ahead!",
            "Spoonnngee Bob SquarePants!",
            "It's free real estate!",
            "Do you smell what The Rock is cooking?!",
            "It's-a me, Mario!",
            "WAKANDA FOREVER!",
            "IKEA?",
            "This is sparta!",
            "Nice!",
            "Say hello to my little friend!",
            "Hold on to your butts!",
            "My precioussss",
            "Winter is coming",
            "What's in the box?!",
            "Great Scott!",
            "To infinity and beyond!",
            "Not all those who wander are lost",
            "Resistance is futile",
            "I'll be back",
            "You can't handle the truth!",
            "Elementary, my dear Watson",
            "Fasten your seatbelts, it's going to be a bumpy night!",
            "Here's looking at you, kid",
            "May the Force be with you",
            "Houston, we have a problem",
            "Show me the money!",
            "I'm king of the world!",
            "I see dead people",
            "We're gonna need a bigger boat",
            "Hasta la vista, baby",
            "Carpe diem! Seize the day, boys!",
            "Just keep swimming",
            "Sacre Bleu!",
            "Surprise, motherfucker!",
            "Yesser miller!",
            "Visit my MySpace page!",
            "Bitch better have my money!",
            "All your base are belong to us",
            "Omae wa mou shindeiru",
            "Nani?!",
            "Yare yare daze",
            "Chocolatine",
            "Oh Canada!",
            "Don't trump yourself",
            "WeeeWooooWeeeWoooo",
            "Hi there, how are you?",
            "Les calipers ses pas garanti?",
            "BÉÉÉÉTONNNN"          
        };
        private static Random _random = new Random();

        public LoadingOverlay()
        {
            InitializeComponent();
            _animation = (Storyboard)Resources["LoadingAnimation"];
        }

        public void Show(string message = "Processing...")
        {
            _hideToken++; // Cancel any pending hides
            LoadingText.Text = message;
            
            if (Visibility != Visibility.Visible)
            {
                Visibility = Visibility.Visible;
                _animation?.Begin(this, true);
            }
        }

        public async void Hide()
        {
            HideProgress();
            LoadingText.Text = ReadyMessages[_random.Next(ReadyMessages.Length)];
            int currentToken = ++_hideToken;
            
            await Task.Delay(2000);
            
            if (currentToken == _hideToken)
            {
                _animation?.Stop(this);
                Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Shows the download progress bar with the given fraction (0.0 – 1.0).
        /// </summary>
        public void ShowProgress(double fraction)
        {
            fraction = Math.Clamp(fraction, 0.0, 1.0);
            ProgressBarContainer.Visibility = Visibility.Visible;
            ProgressBarFill.Width = fraction * 200.0;
            ProgressPercentText.Text = $"{(int)(fraction * 100)}%";
        }

        /// <summary>
        /// Hides the download progress bar.
        /// </summary>
        public void HideProgress()
        {
            ProgressBarContainer.Visibility = Visibility.Collapsed;
            ProgressBarFill.Width = 0;
            ProgressPercentText.Text = "0%";
        }
    }
}
