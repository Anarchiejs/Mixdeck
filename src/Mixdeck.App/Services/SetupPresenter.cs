using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Localization;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Settings;
using Mixdeck.Core.Setup;

namespace Mixdeck.App.Services;

/// <summary>Assistant affiché une fois, tant que SetupCompleted est faux.</summary>
internal static class SetupPresenter
{
    public static async Task OfferAsync(Window window, ISettingsStore settings, IMixer mixer)
    {
        if (settings.Current.SetupCompleted || window.Content is not FrameworkElement root || root.XamlRoot is null)
            return;
        var connected = mixer.State == MixerConnectionState.Connected;
        var body = new TextBlock
        {
            Text = Strings.Get("Setup_Body") + " " + (connected ? Strings.Get("Setup_VoicemeeterOn") : Strings.Get("Setup_VoicemeeterOff")),
            TextWrapping = TextWrapping.Wrap,
        };
        var dialog = new ContentDialog
        {
            Title = Strings.Get("Setup_Title"),
            Content = body,
            PrimaryButtonText = Strings.Get("Setup_Copy"),
            CloseButtonText = Strings.Get("Setup_Done"),
            XamlRoot = root.XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                var script = FlScriptInstall.FindBundledScript(AppContext.BaseDirectory);
                if (script is null)
                    body.Text = Strings.Get("Setup_Missing");
                else
                {
                    var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    FlScriptInstall.CopyToDocuments(script, documents);
                    body.Text = Strings.Get("Setup_Copied");
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                body.Text = Strings.Get("Setup_Missing");
            }
        };
        await dialog.ShowAsync();
        await settings.UpdateAsync(current => current with { SetupCompleted = true });
    }
}
