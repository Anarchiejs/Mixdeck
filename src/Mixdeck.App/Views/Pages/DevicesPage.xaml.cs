using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class DevicesPage : Page
{
    public DevicesPage()
    {
        ViewModel = App.GetService<DevicesPageViewModel>();
        InitializeComponent();
    }

    public DevicesPageViewModel ViewModel { get; }
}
