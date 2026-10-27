using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.AppVolume;

namespace Mixdeck.App.Views.AppVolume;

/// <summary>Le conteneur appelle aussi Activate/Deactivate quand sa fenêtre est montrée/cachée sans Unloaded.</summary>
public sealed partial class AppVolumeList : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(AppVolumeListViewModel), typeof(AppVolumeList), new PropertyMetadata(null, OnViewModelChanged));

    public AppVolumeList()
    {
        InitializeComponent();
        Loaded += (_, _) => ViewModel?.Activate();
        Unloaded += (_, _) => ViewModel?.Deactivate();
    }

    public AppVolumeListViewModel? ViewModel
    {
        get => (AppVolumeListViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (AppVolumeList)sender;
        (args.OldValue as AppVolumeListViewModel)?.Deactivate();
        if (control.IsLoaded) control.ViewModel?.Activate();
    }

    public static Visibility ToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
}
