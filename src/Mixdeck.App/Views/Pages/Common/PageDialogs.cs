using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mixdeck.App.Views.Pages.Common;

public static class PageDialogs
{
    public static async Task<string?> AskNameAsync(FrameworkElement owner, string title, string action, string initial = "")
    {
        var input = new TextBox { Header = "Nom du profil", Text = initial, MaxLength = 100, PlaceholderText = "Ex. Musique, Réunion, Jeu…" };
        var dialog = new ContentDialog
        {
            XamlRoot = owner.XamlRoot, RequestedTheme = owner.ActualTheme,
            Title = title, Content = input, PrimaryButtonText = action, CloseButtonText = "Annuler",
            DefaultButton = ContentDialogButton.Primary, IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(initial),
        };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(input.Text);
        dialog.Opened += (_, _) => { input.Focus(FocusState.Programmatic); input.SelectAll(); };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }
}
