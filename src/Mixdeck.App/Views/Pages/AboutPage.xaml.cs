using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        ViewModel = App.GetService<AboutPageViewModel>();
        InitializeComponent();
    }

    public AboutPageViewModel ViewModel { get; }
}