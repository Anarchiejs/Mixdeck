using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class RulesPage : Page
{
    public RulesPage()
    {
        ViewModel = App.GetService<RulesPageViewModel>();
        InitializeComponent();
    }

    public RulesPageViewModel ViewModel { get; }
}