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

                if (Helpers.ThemeManager.PerformanceMode)
                {
                    // No animations — set everything to a static visible state
                    LoadingText.Opacity = 1.0;
                    if (LogoGlow != null) LogoGlow.Opacity = 0.5;
                }
                else
                {
                    _animation?.Begin(this, true);
                }
            }
        }

        public async void Hide(bool instantly = false)
        {
            HideProgress();
            LoadingText.Text = ReadyMessages[_random.Next(ReadyMessages.Length)];
            int currentToken = ++_hideToken;
            
            if (!instantly)
            {
                await Task.Delay(2000);
            }
            
            if (instantly || currentToken == _hideToken)
            {
                if (!Helpers.ThemeManager.PerformanceMode)
                {
                    _animation?.Stop(this);
                }
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

        /// <summary>
        /// Dynamically styles the loading overlay using accent, secondary, and text colors.
        /// </summary>
        public void ApplyThemeColors(System.Windows.Media.Color accent, System.Windows.Media.Color secondary, System.Windows.Media.Color textColor)
        {
            // 1. Outer Ring Gradient and Glow
            if (OuterRing.Stroke is System.Windows.Media.LinearGradientBrush outerBrush)
            {
                if (outerBrush.IsFrozen) OuterRing.Stroke = outerBrush = outerBrush.Clone();
                outerBrush.GradientStops[0].Color = System.Windows.Media.Color.FromArgb(0, accent.R, accent.G, accent.B);
                outerBrush.GradientStops[1].Color = accent;
                outerBrush.GradientStops[2].Color = BlendColors(accent, System.Windows.Media.Colors.White, 0.3);
            }
            if (OuterRing.Effect is System.Windows.Media.Effects.DropShadowEffect outerGlow)
            {
                if (outerGlow.IsFrozen) OuterRing.Effect = outerGlow = outerGlow.Clone();
                outerGlow.Color = accent;
            }

            // 2. Inner Ring Gradient and Glow
            if (InnerRing.Stroke is System.Windows.Media.LinearGradientBrush innerBrush)
            {
                if (innerBrush.IsFrozen) InnerRing.Stroke = innerBrush = innerBrush.Clone();
                innerBrush.GradientStops[0].Color = System.Windows.Media.Color.FromArgb(0, secondary.R, secondary.G, secondary.B);
                innerBrush.GradientStops[1].Color = secondary;
                innerBrush.GradientStops[2].Color = BlendColors(secondary, System.Windows.Media.Colors.White, 0.4);
            }
            if (InnerRing.Effect is System.Windows.Media.Effects.DropShadowEffect innerGlow)
            {
                if (innerGlow.IsFrozen) InnerRing.Effect = innerGlow = innerGlow.Clone();
                innerGlow.Color = secondary;
            }

            // 3. Logo Glow
            if (LogoGlow != null)
            {
                LogoGlow.Color = accent;
            }

            // 4. Logo Text
            if (LogoTextPa.Foreground is System.Windows.Media.LinearGradientBrush paBrush)
            {
                if (paBrush.IsFrozen) LogoTextPa.Foreground = paBrush = paBrush.Clone();
                paBrush.GradientStops[0].Color = BlendColors(secondary, System.Windows.Media.Colors.White, 0.4);
                paBrush.GradientStops[1].Color = secondary;
            }
            if (LogoTextDdy.Foreground is System.Windows.Media.LinearGradientBrush ddyBrush)
            {
                if (ddyBrush.IsFrozen) LogoTextDdy.Foreground = ddyBrush = ddyBrush.Clone();
                ddyBrush.GradientStops[0].Color = BlendColors(accent, System.Windows.Media.Colors.White, 0.3);
                ddyBrush.GradientStops[1].Color = accent;
            }

            // 5. Status Text
            LoadingText.Foreground = new System.Windows.Media.SolidColorBrush(textColor);

            // 6. Progress Bar Gradient and Glow
            if (ProgressBarFill.Background is System.Windows.Media.LinearGradientBrush progressBrush)
            {
                if (progressBrush.IsFrozen) ProgressBarFill.Background = progressBrush = progressBrush.Clone();
                progressBrush.GradientStops[0].Color = accent;
                progressBrush.GradientStops[1].Color = BlendColors(accent, System.Windows.Media.Colors.White, 0.3);
            }
            if (ProgressBarFill.Effect is System.Windows.Media.Effects.DropShadowEffect progressGlow)
            {
                if (progressGlow.IsFrozen) ProgressBarFill.Effect = progressGlow = progressGlow.Clone();
                progressGlow.Color = accent;
            }
        }

        private System.Windows.Media.Color BlendColors(System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
        {
            byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
            return System.Windows.Media.Color.FromArgb(Lerp(a.A, b.A), Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
        }
    }
}
