using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.AppVolume;

namespace Mixdeck.App.Views.AppVolume;

public sealed partial class AppVolumeRow : UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(AppVolumeRowViewModel), typeof(AppVolumeRow), new PropertyMetadata(null));

    public AppVolumeRow() => InitializeComponent();

    public AppVolumeRowViewModel Session
    {
        get => (AppVolumeRowViewModel)GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

}
