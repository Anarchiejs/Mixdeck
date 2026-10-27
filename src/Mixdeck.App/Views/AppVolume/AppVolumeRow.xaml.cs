using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.AppVolume;
using Mixdeck.Core.Abstractions;

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

    // « Envoyer vers… » : la liste des sorties est lue à l'ouverture (les appareils peuvent changer).
    private async void OnRouteClick(object sender, RoutedEventArgs e)
    {
        if (Session?.Router is not { } router || sender is not FrameworkElement anchor) return;
        var pid = Session.ProcessId;
        try
        {
            var (endpoints, current) = await Task.Run(() => (router.GetRenderEndpoints(), router.GetEndpoint(pid)));
            var menu = new MenuFlyout();
            menu.Items.Add(Choice(router, pid, null, Strings.Get("CS_082"), current is null));
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var endpoint in endpoints.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                menu.Items.Add(Choice(router, pid, endpoint.Id, endpoint.Name, string.Equals(current, endpoint.Id, StringComparison.OrdinalIgnoreCase)));
            }
            menu.ShowAt(anchor);
        }
        catch (Exception)
        {
            // API privée de Windows : en cas d'échec, le bouton ne fait simplement rien.
        }
    }

    private static ToggleMenuFlyoutItem Choice(IAppAudioRouter router, int pid, string? endpointId, string name, bool isCurrent)
    {
        var item = new ToggleMenuFlyoutItem { Text = name, IsChecked = isCurrent };
        item.Click += (_, _) => _ = Task.Run(() => router.SetEndpoint(pid, endpointId));
        return item;
    }

}
