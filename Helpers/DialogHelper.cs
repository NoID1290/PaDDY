using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PaDDY.Helpers
{
    public static class DialogHelper
    {
        public static async Task ShowMessageAsync(XamlRoot xamlRoot, string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OK",
                XamlRoot = xamlRoot
            };
            await dialog.ShowAsync();
        }

        public static async Task<bool> ShowConfirmAsync(XamlRoot xamlRoot, string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = "Yes",
                CloseButtonText = "No",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = xamlRoot
            };
            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }

        public static Task ActivateAsync(this Window window)
        {
            var tcs = new TaskCompletionSource();
            window.Closed += (s, e) => tcs.TrySetResult();
            window.Activate();
            return tcs.Task;
        }
    }
}
