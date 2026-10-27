using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Mixdeck.App.Views.AppVolume;

/// <summary>Intercepte la molette avant le traitement natif de Slider pour garantir un pas de 2 %.</summary>
public sealed class AppVolumeSlider : Slider
{
    public AppVolumeSlider() => DefaultStyleKey = typeof(Slider);

    protected override void OnPointerWheelChanged(PointerRoutedEventArgs args)
    {
        if (!IsEnabled) return;
        var properties = args.GetCurrentPoint(this).Properties;
        if (properties.IsHorizontalMouseWheel) return;
        Value = Math.Clamp(Value + 2 * properties.MouseWheelDelta / 120d, Minimum, Maximum);
        args.Handled = true;
    }
}
