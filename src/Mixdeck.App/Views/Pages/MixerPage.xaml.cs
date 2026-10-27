using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class MixerPage : Page
{
    public MixerPage()
    {
        ViewModel = App.GetService<MixerPageViewModel>();
        InitializeComponent();
    }

    public MixerPageViewModel ViewModel { get; }
}