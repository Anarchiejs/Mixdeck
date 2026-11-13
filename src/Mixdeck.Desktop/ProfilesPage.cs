using Avalonia.Controls;
using Avalonia.Layout;
using Mixdeck.Core.Model;

namespace Mixdeck.Desktop;

public sealed class ProfilesPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly TextBlock _empty;
    private readonly StackPanel _rows = new() { Spacing = 8 };

    public ProfilesPage(DesktopHost host)
    {
        _host = host;
        _empty = ShellWindow.Label("", 14);
        _empty.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { _empty, _rows } } };
        host.Profiles.ProfilesChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    public void Retranslate() => Refresh();

    private void Refresh()
    {
        _rows.Children.Clear();
        var profiles = _host.Profiles.Profiles;
        _empty.Text = DesktopText.Get("Profiles_Empty");
        _empty.IsVisible = profiles.Count == 0;
        foreach (var profile in profiles)
            _rows.Children.Add(Row(profile));
    }

    private Border Row(Profile profile)
    {
        var apply = new Button { Content = DesktopText.Get("Profiles_Apply") };
        apply.Click += async (_, _) =>
        {
            try
            {
                await _host.Profiles.ApplyAsync(profile.Id);
                _host.RaiseStatus(string.Format(DesktopText.Culture, DesktopText.Get("Core_Profile"), profile.Name));
            }
            catch (Exception)
            {
                _host.RaiseStatus(DesktopText.Get("Status_ProfileWindowsOnly"));
            }
        };
        return new Border
        {
            Background = ShellWindow.Paint("#1E222B"),
            CornerRadius = new Avalonia.CornerRadius(10),
            Padding = new Avalonia.Thickness(12),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children = { ShellWindow.Label(profile.Name, 14), apply },
            },
        };
    }
}
