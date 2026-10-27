using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mixdeck.App.ViewModels.Shared;
using Windows.System;
using Windows.UI.Core;

namespace Mixdeck.App.Views.Flyout;

/// <summary>Une ligne du panneau rapide : icône, nom, gain, curseur, vumètre, muet.</summary>
public sealed partial class ChannelRow : UserControl
{
    private UIElement? _faderRoot;
    private int _wheelRemainder;
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
        nameof(Channel), typeof(ChannelViewModel), typeof(ChannelRow), new PropertyMetadata(null));

    public ChannelRow()
    {
        InitializeComponent();
        PointerWheelChanged += OnWheelChanged;
        VolumeFader.Loaded += OnFaderLoaded;
        VolumeFader.Unloaded += OnFaderUnloaded;
    }

    private void OnFaderLoaded(object sender, RoutedEventArgs args)
    {
        VolumeFader.ApplyTemplate();
        // Intercepter dans le modèle avant que le Fader partagé n'applique son propre pas.
        if (_faderRoot is null && VisualTreeHelper.GetChildrenCount(VolumeFader) > 0
            && VisualTreeHelper.GetChild(VolumeFader, 0) is UIElement root)
        {
            _faderRoot = root;
            root.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheelChanged), true);
        }
    }

    private void OnFaderUnloaded(object sender, RoutedEventArgs args)
    {
        _faderRoot?.RemoveHandler(PointerWheelChangedEvent, new PointerEventHandler(OnWheelChanged));
        _faderRoot = null;
        _wheelRemainder = 0;
    }

    private void OnWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled) return;
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsHorizontalMouseWheel) return;
        e.Handled = true; // Même à la borne, ne jamais faire défiler le panneau.
        if (Channel is null || !IsEnabled) return;
        _wheelRemainder += point.Properties.MouseWheelDelta;
        var detents = _wheelRemainder / 120;
        _wheelRemainder %= 120;
        if (detents == 0) return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        Channel.Gain = Math.Clamp(Channel.Gain + detents * (shift ? 3 : 1), -60, 12);
        var announcement = $"{Channel.DisplayName} : {Converters.Db(Channel.Gain)}";
        AutomationProperties.SetHelpText(this, announcement);
        FrameworkElementAutomationPeer.CreatePeerForElement(this)?.RaiseNotificationEvent(
            AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent,
            announcement, "Mixdeck.Volume");
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    public ChannelViewModel Channel
    {
        get => (ChannelViewModel)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }
}
