using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Mixdeck.App.Controls;

public sealed class Fader : Slider
{
    private Border? zeroMark;
    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(Fader), new PropertyMetadata(false, (d, _) => ((Fader)d).UpdateAppearance()));
    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    public Fader()
    {
        DefaultStyleKey = typeof(Fader);
        ControlResources.Load(this);
        Minimum = -60; Maximum = 12; StepFrequency = .1; SmallChange = .1; LargeChange = 1;
        Value = 0;
        IsThumbToolTipEnabled = false;
        ValueChanged += (_, _) => UpdateAppearance();
        DoubleTapped += (_, args) => { Value = 0; args.Handled = true; };
        SizeChanged += (_, _) => PositionMark();
        RegisterPropertyChangedCallback(OrientationProperty, (_, _) => PositionMark());
        RegisterPropertyChangedCallback(MinimumProperty, (_, _) => PositionMark());
        RegisterPropertyChangedCallback(MaximumProperty, (_, _) => PositionMark());
        ActualThemeChanged += (_, _) => UpdateAppearance();
    }
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        // Le modèle natif conserve le clavier, RangeValue et toutes les animations Fluent.
        if (GetTemplateChild("SliderContainer") is Grid root)
        {
            zeroMark = new Border { Width = 8, Height = 2, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            root.Children.Add(zeroMark);
        }
        UpdateAppearance();
    }
    protected override void OnPointerWheelChanged(PointerRoutedEventArgs e)
    {
        // Seul un fader cliqué au préalable suit la molette : en passant dessus, la page défile sans changer de gain.
        if (!IsEnabled || FocusState == FocusState.Unfocused) return;
        bool shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        Value = Math.Clamp(Math.Round(Value + e.GetCurrentPoint(this).Properties.MouseWheelDelta / 120d * (shift ? .1 : .5), 1), Minimum, Maximum);
        e.Handled = true;
    }
    private void UpdateAppearance()
    {
        string text = ControlResources.Db(Value);
        ToolTipService.SetToolTip(this, text);
        AutomationProperties.SetHelpText(this, text);
        Foreground = (Brush)ControlResources.Get(this, IsMuted ? "TextFillColorDisabledBrush" : "AccentFillColorDefaultBrush");
        PositionMark();
    }
    private void PositionMark()
    {
        if (zeroMark is null) return;
        zeroMark.Background = (Brush)ControlResources.Get(this, "TextFillColorPrimaryBrush");
        double ratio = Maximum > Minimum ? Math.Clamp(-Minimum / (Maximum - Minimum), 0, 1) : 0;
        bool vertical = Orientation == Orientation.Vertical;
        zeroMark.Width = vertical ? 8 : 2;
        zeroMark.Height = vertical ? 2 : 8;
        zeroMark.Margin = vertical ? new Thickness(0, Math.Max(0, 10 + (ActualHeight - 20) * (1 - ratio)), 0, 0)
            : new Thickness(Math.Max(0, 10 + (ActualWidth - 20) * ratio), 0, 0, 0);
    }
}
