using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Mixdeck.App.Controls;

public sealed class RouteButton : ToggleButton
{
    public static readonly DependencyProperty BusProperty = DependencyProperty.Register(nameof(Bus), typeof(string), typeof(RouteButton), new PropertyMetadata("A1", (d, e) => { var button = (RouteButton)d; button.Content = e.NewValue; button.UpdateBus(); }));
    public string Bus { get => (string)GetValue(BusProperty); set => SetValue(BusProperty, value); }
    public RouteButton()
    {
        DefaultStyleKey = typeof(RouteButton);
        ControlResources.Load(this);
        Content = Bus;
        RegisterPropertyChangedCallback(ContentProperty, (_, _) => UpdateBus());
        Loaded += (_, _) => UpdateBus();
        UpdateBus();
    }
    private void UpdateBus()
    {
        string label = Content as string ?? Bus;
        AutomationProperties.SetName(this, "Envoyer vers " + label);
        VisualStateManager.GoToState(this, label.StartsWith('B') ? "VirtualBus" : "PhysicalBus", false);
    }
    protected override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateBus(); }
}
