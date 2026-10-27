using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class ProfilesPage : Page
{
    public ProfilesPage()
    {
        ViewModel = App.GetService<ProfilesPageViewModel>();
        InitializeComponent();
    }

    public ProfilesPageViewModel ViewModel { get; }
}