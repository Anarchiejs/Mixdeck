using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class RoutingPage : Page
{
    public RoutingPage()
    {
        ViewModel = App.GetService<RoutingPageViewModel>();
        InitializeComponent();
    }

    public RoutingPageViewModel ViewModel { get; }
}