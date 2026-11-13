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

public sealed class ShellWindow : Window
{
    private readonly DesktopHost _host;
    private readonly Border _pageHost;
    private readonly TextBlock _statusText;
    private readonly Border _statusBar;
    private readonly TextBlock _osLabel;
    private readonly Dictionary<string, Button> _nav = new(StringComparer.Ordinal);
    private readonly AudioPage _audio;
    private readonly AppsPage _apps;
    private readonly StudioPage _studio;
    private readonly ProfilesPage _profiles;
    private readonly RulesPage _rules;
    private readonly SettingsPage _settings;
    private readonly Border _setup;
    private bool _allowClose;
    private string _page = "audio";
    private CancellationTokenSource? _statusClear;

    public ShellWindow(DesktopHost host)
    {
        _host = host;
        Title = "Mixdeck";
        Width = 1080;
        Height = 720;
        MinWidth = 860;
        MinHeight = 560;
        Background = Paint("#101218");
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _audio = new AudioPage(host);
        _apps = new AppsPage(host);
        _studio = new StudioPage(host);
        _profiles = new ProfilesPage(host);
        _rules = new RulesPage(host);
        _settings = new SettingsPage(host);
        _setup = BuildSetup(host);
        _osLabel = Label(host.Surfaces.Capabilities.OperatingSystemName, 12);
        _osLabel.Opacity = 0.7;
        _osLabel.TextWrapping = TextWrapping.Wrap;
        var side = new DockPanel { Margin = new Thickness(16) };
        var title = Label("Mixdeck", 22);
        title.FontWeight = FontWeight.SemiBold;
        title.Foreground = Paint("#3DDC97");
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(_osLabel, Dock.Bottom);
        side.Children.Add(title);
        side.Children.Add(_osLabel);
        var nav = new StackPanel { Spacing = 6, Margin = new Thickness(0, 24, 0, 16) };
        nav.Children.Add(Nav("audio", "Nav_Audio"));
        nav.Children.Add(Nav("apps", "Nav_Apps"));
        nav.Children.Add(Nav("studio", "Nav_Studio"));
        nav.Children.Add(Nav("profiles", "Nav_Profiles"));
        nav.Children.Add(Nav("rules", "Nav_Rules"));
        nav.Children.Add(Nav("settings", "Nav_Settings"));
        side.Children.Add(nav);
        _pageHost = new Border { Margin = new Thickness(8, 20, 20, 12), Child = _audio };
        _statusText = Label("", 13);
        _statusBar = new Border
        {
            Background = Paint("#22262F"),
            Padding = new Thickness(16, 8),
            IsVisible = false,
            Child = _statusText,
        };
        var root = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,*"),
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        Grid.SetColumn(_setup, 1);
        root.Children.Add(_setup);
        var sideBorder = new Border { Background = Paint("#171A21"), Child = side };
        Grid.SetRowSpan(sideBorder, 3);
        root.Children.Add(sideBorder);
        Grid.SetColumn(_pageHost, 1);
        Grid.SetRow(_pageHost, 1);
        root.Children.Add(_pageHost);
        Grid.SetColumnSpan(_statusBar, 2);
        Grid.SetRow(_statusBar, 2);
        root.Children.Add(_statusBar);
        Content = root;
        host.Status += ShowStatus;
        host.LanguageChanged += Retranslate;
        Closing += OnClosing;
        KeyDown += OnKeyDown;
        Select("audio");
    }

    private Border BuildSetup(DesktopHost host)
    {
        var copy = new Button { Content = DesktopText.Get("Setup_Copy") };
        var done = new Button { Content = DesktopText.Get("Setup_Done") };
        var text = Label(DesktopText.Get("Setup_Body"), 13);
        text.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        copy.Click += (_, _) => host.RaiseStatus(host.CopyFlScript());
        done.Click += async (_, _) =>
        {
            if (await host.CompleteSetupAsync()) _setup.IsVisible = false;
        };
        return new Border
        {
            Margin = new Thickness(8, 12, 20, 0),
            Padding = new Thickness(12),
            Background = Paint("#243028"),
            IsVisible = !host.Settings.Current.SetupCompleted,
            Child = new StackPanel { Spacing = 8, Children = { text, copy, done } },
        };
    }

    private async void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (!_settings.Capture(e, out var binding)) return;
        e.Handled = true;
        if (HotkeyConflicts.Find(_host.Settings.Current.Hotkeys, binding).Count > 0)
        {
            _host.RaiseStatus(DesktopText.Get("Hotkey_Conflict"));
            return;
        }
        var saved = await _host.SaveAsync(settings => settings with
        {
            Hotkeys = settings.Hotkeys.Where(item => item.Action != binding.Action).Append(binding).ToArray(),
        });
        if (!saved) return;
        _host.Surfaces.Hotkeys.Apply(_host.Settings.Current.Hotkeys);
        _settings.Retranslate();
    }

    public void AllowClose() => _allowClose = true;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        Hide();
    }

    private Button Nav(string page, string key)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = Paint("#F4F6FB"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Tag = key,
            Name = "nav-" + page,
        };
        button.Click += (_, _) => Select(page);
        _nav[page] = button;
        return button;
    }

    private void Select(string page)
    {
        _page = page;
        _pageHost.Child = page switch
        {
            "apps" => _apps,
            "studio" => _studio,
            "profiles" => _profiles,
            "rules" => _rules,
            "settings" => _settings,
            _ => _audio,
        };
        foreach (var (name, button) in _nav)
        {
            var selected = name == page;
            button.Background = selected ? Paint("#243028") : Brushes.Transparent;
            button.Foreground = selected ? Paint("#3DDC97") : Paint("#F4F6FB");
        }
        Retranslate();
    }

    private void Retranslate()
    {
        foreach (var (name, button) in _nav)
            if (button.Tag is string key) button.Content = DesktopText.Get(key);
        _osLabel.Text = _host.Surfaces.Capabilities.OperatingSystemName;
        _audio.Retranslate();
        _apps.Retranslate();
        _studio.Retranslate();
        _profiles.Retranslate();
        _rules.Retranslate();
        _settings.Retranslate();
    }

    private void ShowStatus(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _statusText.Text = text;
            _statusBar.IsVisible = true;
            _statusClear?.Cancel();
            _statusClear = new CancellationTokenSource();
            var token = _statusClear.Token;
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(2800, token); }
                catch (OperationCanceledException) { return; }
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _statusBar.IsVisible = false);
            });
        });
    }

    internal static SolidColorBrush Paint(string hex) => new(Color.Parse(hex));
    internal static TextBlock Label(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Paint("#F4F6FB"),
    };
}

