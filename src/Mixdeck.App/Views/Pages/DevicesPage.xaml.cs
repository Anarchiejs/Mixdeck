using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class DevicesPage : Page
{
    private bool dialogOpen;
    public DevicesPage()
    {
        ViewModel = App.GetService<DevicesPageViewModel>();
        InitializeComponent();
    }
    public DevicesPageViewModel ViewModel { get; }
    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel.Activate();
    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();
    private async void OnRestartEngine(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = Strings.Get("CS_083"),
                Content = Strings.Get("CS_084"),
                PrimaryButtonText = Strings.Get("CS_085"),
                CloseButtonText = Strings.Get("CS_086"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) ViewModel.RestartEngine();
        }
        catch (Exception) { ViewModel.Report(Strings.Get("CS_087"), true); }
        finally { dialogOpen = false; }
    }
}
