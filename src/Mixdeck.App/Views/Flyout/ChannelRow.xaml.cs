using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Shared;

namespace Mixdeck.App.Views.Flyout;

/// <summary>Une ligne du panneau rapide : icône, nom, gain, curseur, vumètre, muet.</summary>
public sealed partial class ChannelRow : UserControl
{
    public static readonly DependencyProperty ChannelProperty = DependencyProperty.Register(
        nameof(Channel), typeof(ChannelViewModel), typeof(ChannelRow), new PropertyMetadata(null));

    public ChannelRow()
    {
        InitializeComponent();
    }

    public ChannelViewModel Channel
    {
        get => (ChannelViewModel)GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }
}
