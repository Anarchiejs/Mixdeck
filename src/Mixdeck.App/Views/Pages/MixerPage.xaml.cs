using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages;

public sealed partial class MixerPage : Page
{
    public MixerPage()
    {
        ViewModel = App.GetService<MixerPageViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public MixerPageViewModel ViewModel { get; }
    public bool HasProfiles => ViewModel.Mixer.Profiles.Count > 0;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Mixer.Profiles.CollectionChanged += OnProfilesChanged;
        Bindings.Update();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Mixer.Profiles.CollectionChanged -= OnProfilesChanged;
    }

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Bindings.Update();

    private async void OnProfileInvoked(ProfilePillList sender, ProfilePillInvokedEventArgs e)
    {
        if (e.Item is Mixdeck.App.ViewModels.Shared.ProfileItemViewModel profile)
            await ViewModel.ApplyProfileCommand.ExecuteAsync(profile);
    }
}
