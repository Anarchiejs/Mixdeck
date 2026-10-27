using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Controls;
using Mixdeck.App.ViewModels.Shared;

namespace Mixdeck.App.Views.Pages.Common;

/// <summary>La case observe et modifie directement la route partagée, par liaison compilée.</summary>
public sealed partial class RoutingCell : UserControl
{
    public static readonly DependencyProperty RouteProperty = DependencyProperty.Register(nameof(Route), typeof(RouteViewModel), typeof(RoutingCell),
        new PropertyMetadata(null, (d, _) => ((RoutingCell)d).Bindings.Update()));
    public RouteViewModel Route { get => (RouteViewModel)GetValue(RouteProperty); set => SetValue(RouteProperty, value); }
    public RouteButton Button => Cell;
    public RoutingCell() { InitializeComponent(); }
}
