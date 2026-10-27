using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Controls;
using Mixdeck.App.Localization;
using Mixdeck.App.ViewModels.Shared;
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
        // Un périphérique a pu changer depuis la page Appareils.
        _ = ViewModel.Mixer.RefreshDevicesAsync();
    }

    private void OnAddStripClick(object sender, RoutedEventArgs e) => ShowHidden((FrameworkElement)sender, ViewModel.Mixer.Strips);

    private void OnAddBusClick(object sender, RoutedEventArgs e) => ShowHidden((FrameworkElement)sender, ViewModel.Mixer.Buses);

    private static void ShowHidden(FrameworkElement target, IEnumerable<ChannelViewModel> channels)
    {
        var menu = new MenuFlyout();
        foreach (var channel in channels.Where(c => !c.IsInConsole))
        {
            var text = channel.HasDevice == false ? Strings.Format("CS_ConsoleNoDevice", channel.DisplayName) : channel.DisplayName;
            var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = channel.Icon } };
            item.Click += (_, _) => channel.ShowInConsole = true;
            menu.Items.Add(item);
        }
        if (menu.Items.Count > 0) menu.ShowAt(target);
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
