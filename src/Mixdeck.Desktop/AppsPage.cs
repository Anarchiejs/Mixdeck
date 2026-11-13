using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.FlStudio;
using Mixdeck.Platform.Desktop.Audio;
using Mixdeck.Platform.Desktop.Hotkeys;

namespace Mixdeck.Desktop;

public sealed class AppsPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly TextBlock _banner;
    private readonly TextBox _search = new();
    private readonly StackPanel _rows = new() { Spacing = 8 };

    public AppsPage(DesktopHost host)
    {
        _host = host;
        _banner = ShellWindow.Label("", 14);
        _banner.TextWrapping = TextWrapping.Wrap;
        _search.TextChanged += (_, _) => Refresh();
        Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { _search, _banner, _rows } } };
        host.Surfaces.Audio.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    public void Retranslate() => Refresh();

    private void Refresh()
    {
        var audio = _host.Surfaces.Audio;
        var caps = _host.Surfaces.Capabilities;
        if (!caps.PerAppVolume)
        {
            _banner.Text = DesktopText.Get("Apps_Unavailable");
            _banner.IsVisible = true;
            _rows.Children.Clear();
            return;
        }
        if (!audio.IsAvailable)
        {
            _banner.Text = audio.UnavailableReason ?? DesktopText.Get("Apps_Unavailable");
            _banner.IsVisible = true;
            _rows.Children.Clear();
            return;
        }
        _search.PlaceholderText = DesktopText.Get("Apps_Search");
        var sessions = audio.Sessions.Where(session => Mixdeck.Core.Audio.AppSessionFilter.Matches(session.DisplayName, session.ProcessName, _search.Text)).ToArray();
        _banner.IsVisible = sessions.Length == 0;
        _banner.Text = DesktopText.Get("Apps_Empty");
        _rows.Children.Clear();
        foreach (var session in sessions)
            _rows.Children.Add(new SessionRow(_host, session));
    }

    private sealed class SessionRow : Border
    {
        public SessionRow(DesktopHost host, AppAudioSession session)
        {
            Background = ShellWindow.Paint("#1E222B");
            CornerRadius = new CornerRadius(10);
            Padding = new Thickness(12);
            var title = ShellWindow.Label(string.IsNullOrWhiteSpace(session.DisplayName) ? session.ProcessName : session.DisplayName, 14);
            var volume = new Slider { Minimum = 0, Maximum = 100, Value = Math.Clamp(session.Volume, 0, 1) * 100 };
            var mute = new CheckBox { Content = DesktopText.Get("Audio_Mute"), IsChecked = session.Mute, Foreground = ShellWindow.Paint("#F4F6FB") };
            volume.ValueChanged += async (_, _) =>
            {
                if (!volume.IsFocused) return;
                await host.Surfaces.Audio.SetSessionVolumeAsync(session.Id, (float)(volume.Value / 100));
            };
            mute.IsCheckedChanged += async (_, _) =>
            {
                await host.Surfaces.Audio.SetSessionMuteAsync(session.Id, mute.IsChecked == true);
            };
            var panel = new StackPanel { Spacing = 6, Children = { title, mute, volume } };
            if (host.Surfaces.Capabilities.PerAppRouting)
            {
                var route = new ComboBox { HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
                route.Items.Add(new ComboBoxItem { Content = DesktopText.Get("Apps_DefaultRoute"), Tag = null });
                foreach (var output in host.Surfaces.Audio.Snapshot.Outputs)
                    route.Items.Add(new ComboBoxItem { Content = output.Name, Tag = output.Id });
                route.SelectedIndex = 0;
                route.SelectionChanged += async (_, _) =>
                {
                    if (route.SelectedItem is not ComboBoxItem item) return;
                    await host.Surfaces.Audio.MoveProcessAsync(session.ProcessId, item.Tag as string);
                };
                panel.Children.Add(route);
            }
            Child = panel;
        }
    }
}

