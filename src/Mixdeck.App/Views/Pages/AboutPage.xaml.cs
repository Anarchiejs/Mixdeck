using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Localization;
using Mixdeck.App.ViewModels.Pages;
using Windows.ApplicationModel.DataTransfer;

namespace Mixdeck.App.Views.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        ViewModel = App.GetService<AboutPageViewModel>();
        InitializeComponent();
    }

    public AboutPageViewModel ViewModel { get; }

    private void OnCopyCrash(object sender, RoutedEventArgs e)
    {
        var report = ViewModel.RedactedCrashReport();
        if (string.IsNullOrEmpty(report))
        {
            ViewModel.Status = Strings.Get("About_NoCrash");
            return;
        }
        var package = new DataPackage();
        package.SetText(report);
        Clipboard.SetContent(package);
        ViewModel.Status = Strings.Get("About_CrashCopied");
    }
}