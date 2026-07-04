using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PaDDY.Controls
{
    public sealed class DeleteAllDialog : ContentDialog
    {
        public bool KeepFavorites { get; private set; }

        public DeleteAllDialog()
        {
            Title = "Delete All Recordings";
            Content = "Delete all recordings from disk?";
            PrimaryButtonText = "Delete All";
            SecondaryButtonText = "Keep Favorites";
            CloseButtonText = "Cancel";

            PrimaryButtonClick += (s, e) => { KeepFavorites = false; };
            SecondaryButtonClick += (s, e) => { KeepFavorites = true; };
        }
    }
}
