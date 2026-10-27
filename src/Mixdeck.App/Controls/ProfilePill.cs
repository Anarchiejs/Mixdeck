using Mixdeck.App.Localization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Mixdeck.App.Controls;

public sealed class ProfilePill : ToggleButton
{
    private static DependencyProperty Property(string name, Type type, object? value) => DependencyProperty.Register(name, type, typeof(ProfilePill), new PropertyMetadata(value, (d, _) => ((ProfilePill)d).Update()));
    public static readonly DependencyProperty ProfileNameProperty = Property(nameof(ProfileName), typeof(string), Strings.Get("CS_004"));
    public static readonly DependencyProperty IconProperty = Property(nameof(Icon), typeof(string), "\uE8A5");
    public static readonly DependencyProperty ColorProperty = Property(nameof(Color), typeof(string), null);
    public static readonly DependencyProperty IsActiveProperty = Property(nameof(IsActive), typeof(bool), false);
    public string ProfileName { get => (string)GetValue(ProfileNameProperty); set => SetValue(ProfileNameProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Color { get => (string?)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }
    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }
    public ProfilePill()
    {
        DefaultStyleKey = typeof(ProfilePill);
        ControlResources.Load(this);
    }
    protected override void OnApplyTemplate() { base.OnApplyTemplate(); Update(); }
    private void Update()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, ProfileName);
        VisualStateManager.GoToState(this, IsActive ? "ActiveProfile" : "InactiveProfile", false);
        if (GetTemplateChild("PART_Color") is Border dot)
        {
            var brush = (Brush)ControlResources.Get(this, "AccentFillColorDefaultBrush");
            if (Color is { Length: 7 } color && color[0] == '#' && uint.TryParse(color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint rgb))
                brush = new SolidColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            dot.Background = brush;
        }
    }
}
