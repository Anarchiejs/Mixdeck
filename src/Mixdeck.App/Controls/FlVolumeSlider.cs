using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Mixdeck.App.Controls;

/// <summary>Volume normalisé ; la molette agit seulement après un clic dans le curseur.</summary>
public sealed class FlVolumeSlider : Slider
{
    private bool clicked;
    public FlVolumeSlider()
    {
        Minimum = 0; Maximum = 1; StepFrequency = .01; SmallChange = .01; LargeChange = .1;
        IsThumbToolTipEnabled = false;
        AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => clicked = true), true);
        LostFocus += (_, _) => clicked = false;
    }
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        if (!IsEnabled || !clicked || FocusState == FocusState.Unfocused) return;
        Value = Math.Clamp(Value + e.GetCurrentPoint(this).Properties.MouseWheelDelta / 120d * .01, 0, 1);
        e.Handled = true;
    }
}
