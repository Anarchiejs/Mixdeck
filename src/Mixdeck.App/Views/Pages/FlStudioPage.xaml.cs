using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class FlStudioPage : Page
{
    public FlStudioPageViewModel ViewModel { get; } = App.GetService<FlStudioPageViewModel>();
    public FlStudioPage() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel.Activate();
    private void OnUnloaded(object sender, RoutedEventArgs e) => ViewModel.Deactivate();
}
