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
    private readonly SettingsPage _settings;
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
        _settings = new SettingsPage(host);
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
            RowDefinitions = new RowDefinitions("*,Auto"),
        };
        var sideBorder = new Border { Background = Paint("#171A21"), Child = side };
        root.Children.Add(sideBorder);
        Grid.SetColumn(_pageHost, 1);
        root.Children.Add(_pageHost);
        Grid.SetColumnSpan(_statusBar, 2);
        Grid.SetRow(_statusBar, 1);
        root.Children.Add(_statusBar);
        Content = root;
        host.Status += ShowStatus;
        host.LanguageChanged += Retranslate;
        Closing += OnClosing;
        Select("audio");
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

public sealed class AudioPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly TextBlock _banner;
    private readonly TextBlock _outputsTitle;
    private readonly TextBlock _inputsTitle;
    private readonly StackPanel _outputs = new() { Spacing = 8 };
    private readonly StackPanel _inputs = new() { Spacing = 8 };
    private readonly Dictionary<string, DeviceRow> _rows = new(StringComparer.Ordinal);

    public AudioPage(DesktopHost host)
    {
        _host = host;
        _banner = ShellWindow.Label("", 14);
        _banner.TextWrapping = TextWrapping.Wrap;
        _banner.Foreground = ShellWindow.Paint("#FFB020");
        _outputsTitle = ShellWindow.Label("", 16);
        _outputsTitle.Name = "audio-outputs";
        _inputsTitle = ShellWindow.Label("", 16);
        _inputsTitle.Name = "audio-inputs";
        var scroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 16,
                Children = { _banner, _outputsTitle, _outputs, _inputsTitle, _inputs },
            },
        };
        Content = scroll;
        host.Surfaces.Audio.Changed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    public void Retranslate()
    {
        _outputsTitle.Text = DesktopText.Get("Audio_Outputs");
        _inputsTitle.Text = DesktopText.Get("Audio_Inputs");
        Refresh();
    }

    private void Refresh()
    {
        var audio = _host.Surfaces.Audio;
        _banner.IsVisible = !audio.IsAvailable;
        _banner.Text = audio.UnavailableReason ?? "";
        var snapshot = audio.Snapshot;
        Sync(_outputs, snapshot.Outputs, SystemAudioFlow.Output, snapshot.DefaultOutputId);
        Sync(_inputs, snapshot.Inputs, SystemAudioFlow.Input, snapshot.DefaultInputId);
    }

    private void Sync(StackPanel panel, IReadOnlyList<SystemAudioDevice> devices, SystemAudioFlow flow, string? defaultId)
    {
        var alive = devices.Select(device => flow + ":" + device.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var child in panel.Children.OfType<Control>().ToArray())
        {
            if (child.Tag is string tag && !alive.Contains(tag))
            {
                panel.Children.Remove(child);
                _rows.Remove(tag);
            }
        }
        foreach (var device in devices)
        {
            var tag = flow + ":" + device.Id;
            if (!_rows.TryGetValue(tag, out var row))
            {
                row = new DeviceRow(_host, device, flow);
                row.Tag = tag;
                _rows[tag] = row;
                panel.Children.Add(row);
            }
            row.Update(device, defaultId);
        }
    }

    private sealed class DeviceRow : Border
    {
        private readonly DesktopHost _host;
        private readonly SystemAudioFlow _flow;
        private readonly string _id;
        private readonly TextBlock _name;
        private readonly RadioButton _default;
        private readonly CheckBox _mute;
        private readonly Slider _volume;
        private bool _suppress;

        public DeviceRow(DesktopHost host, SystemAudioDevice device, SystemAudioFlow flow)
        {
            _host = host;
            _flow = flow;
            _id = device.Id;
            Background = ShellWindow.Paint("#1E222B");
            CornerRadius = new CornerRadius(10);
            Padding = new Thickness(12);
            _name = ShellWindow.Label(device.Name, 14);
            _default = new RadioButton { Foreground = ShellWindow.Paint("#F4F6FB"), GroupName = flow.ToString() };
            _mute = new CheckBox { Foreground = ShellWindow.Paint("#F4F6FB") };
            _volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 1 };
            _default.IsCheckedChanged += async (_, _) =>
            {
                if (_suppress || _default.IsChecked != true) return;
                await host.Surfaces.Audio.SetDefaultAsync(flow, _id);
            };
            _mute.IsCheckedChanged += async (_, _) =>
            {
                if (_suppress) return;
                await host.Surfaces.Audio.SetDeviceMuteAsync(flow, _id, _mute.IsChecked == true);
            };
            _volume.ValueChanged += async (_, _) =>
            {
                if (_suppress || !_volume.IsFocused) return;
                await host.Surfaces.Audio.SetDeviceVolumeAsync(flow, _id, (float)(_volume.Value / 100));
            };
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _name,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 16,
                        Children = { _default, _mute },
                    },
                    _volume,
                },
            };
        }

        public void Update(SystemAudioDevice device, string? defaultId)
        {
            _suppress = true;
            _name.Text = device.Name;
            _default.Content = DesktopText.Get("Audio_Default");
            _mute.Content = DesktopText.Get("Audio_Mute");
            _default.IsChecked = device.Id == defaultId;
            _mute.IsChecked = device.Mute;
            if (!_volume.IsFocused) _volume.Value = Math.Clamp(device.Volume, 0, 1) * 100;
            _suppress = false;
        }
    }
}

public sealed class AppsPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly TextBlock _banner;
    private readonly StackPanel _rows = new() { Spacing = 8 };

    public AppsPage(DesktopHost host)
    {
        _host = host;
        _banner = ShellWindow.Label("", 14);
        _banner.TextWrapping = TextWrapping.Wrap;
        Content = new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { _banner, _rows } } };
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
        var sessions = audio.Sessions;
        _banner.IsVisible = sessions.Count == 0;
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

public sealed class StudioPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly TextBlock _voicemeeterTitle;
    private readonly TextBlock _voicemeeterBody;
    private readonly StackPanel _strips = new() { Spacing = 8 };
    private readonly TextBlock _flTitle;
    private readonly TextBlock _flBody;

    public StudioPage(DesktopHost host)
    {
        _host = host;
        _voicemeeterTitle = ShellWindow.Label("", 18);
        _voicemeeterBody = ShellWindow.Label("", 14);
        _voicemeeterBody.TextWrapping = TextWrapping.Wrap;
        _flTitle = ShellWindow.Label("", 18);
        _flBody = ShellWindow.Label("", 14);
        _flBody.TextWrapping = TextWrapping.Wrap;
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { _voicemeeterTitle, _voicemeeterBody, _strips, _flTitle, _flBody },
            },
        };
        host.Mixer.StateChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        host.Mixer.SnapshotChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshStrips);
        host.Daw.StateChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    public void Retranslate() => Refresh();

    private void Refresh()
    {
        var connected = _host.Mixer.State == MixerConnectionState.Connected;
        _voicemeeterTitle.Text = connected && _host.Options.Demo
            ? DesktopText.Get("Studio_Demo")
            : DesktopText.Get("Studio_Voicemeeter");
        _voicemeeterBody.Text = connected ? "" : DesktopText.Get("Studio_VoicemeeterBody");
        _voicemeeterBody.IsVisible = !connected;
        _strips.IsVisible = connected;
        _flTitle.Text = DesktopText.Get("Fl_Title");
        if (_host.Midi is IMidiAvailability { IsSupported: false })
            _flBody.Text = DesktopText.Get("Fl_Unavailable");
        else if (OperatingSystem.IsMacOS())
            _flBody.Text = DesktopText.Get("Fl_Mac") + " " + FlState();
        else
            _flBody.Text = FlState();
        if (connected) RefreshStrips();
        else _strips.Children.Clear();
    }

    private string FlState() => _host.Daw.State switch
    {
        DawConnectionState.Connected => DesktopText.Get("Fl_Connected"),
        DawConnectionState.WaitingForDaw => DesktopText.Get("Fl_Waiting"),
        DawConnectionState.PortsMissing => DesktopText.Get("Fl_Ports"),
        DawConnectionState.Disabled => DesktopText.Get("Fl_Disabled"),
        _ => DesktopText.Get("Fl_Waiting"),
    };

    private void RefreshStrips()
    {
        if (_host.Mixer.State != MixerConnectionState.Connected) return;
        var snapshot = _host.Mixer.Snapshot;
        if (_strips.Children.Count != snapshot.Strips.Count) _strips.Children.Clear();
        for (var i = 0; i < snapshot.Strips.Count; i++)
        {
            var strip = snapshot.Strips[i];
            if (i >= _strips.Children.Count) _strips.Children.Add(new StripRow(_host, strip.Index));
            ((StripRow)_strips.Children[i]).Update(strip);
        }
    }

    private sealed class StripRow : Border
    {
        private readonly DesktopHost _host;
        private readonly int _index;
        private readonly TextBlock _name;
        private readonly CheckBox _mute;
        private readonly Slider _gain;
        private bool _suppress;

        public StripRow(DesktopHost host, int index)
        {
            _host = host;
            _index = index;
            Background = ShellWindow.Paint("#1E222B");
            CornerRadius = new CornerRadius(10);
            Padding = new Thickness(12);
            _name = ShellWindow.Label("", 14);
            _mute = new CheckBox { Foreground = ShellWindow.Paint("#F4F6FB") };
            _gain = new Slider { Minimum = MixerLayout.MinGainDb, Maximum = MixerLayout.MaxGainDb };
            _mute.IsCheckedChanged += (_, _) =>
            {
                if (_suppress) return;
                host.Mixer.SetFloat(VmParam.StripMute(index), VmParam.FromBool(_mute.IsChecked == true));
            };
            _gain.ValueChanged += (_, _) =>
            {
                if (_suppress || !_gain.IsFocused) return;
                host.Mixer.SetFloat(VmParam.StripGain(index), (float)_gain.Value);
            };
            Child = new StackPanel { Spacing = 6, Children = { _name, _mute, _gain } };
        }

        public void Update(StripState strip)
        {
            _suppress = true;
            _name.Text = string.IsNullOrWhiteSpace(strip.Label) ? strip.Layout.DefaultStripLabels[strip.Index] : strip.Label;
            _mute.Content = DesktopText.Get("Studio_Mute");
            _mute.IsChecked = strip.Mute;
            if (!_gain.IsFocused) _gain.Value = strip.Gain;
            _suppress = false;
        }
    }
}

public sealed class SettingsPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly ComboBox _language;
    private readonly CheckBox _autostart;
    private readonly CheckBox _osd;
    private readonly CheckBox _updates;
    private readonly TextBlock _autostartHint;
    private readonly TextBlock _hotkeyTitle;
    private readonly TextBlock _featureTitle;
    private readonly TextBlock _about;
    private readonly StackPanel _hotkeys = new() { Spacing = 4 };
    private readonly StackPanel _features = new() { Spacing = 4 };
    private readonly Button _check;
    private bool _suppress;

    public SettingsPage(DesktopHost host)
    {
        _host = host;
        _language = new ComboBox();
        _language.Items.Add(new ComboBoxItem { Tag = null });
        _language.Items.Add(new ComboBoxItem { Tag = "en-US" });
        _language.Items.Add(new ComboBoxItem { Tag = "fr-FR" });
        _language.SelectionChanged += async (_, _) =>
        {
            if (_suppress || _language.SelectedItem is not ComboBoxItem item) return;
            await host.SetLanguageAsync(item.Tag as string);
        };
        _autostart = new CheckBox { Foreground = ShellWindow.Paint("#F4F6FB") };
        _osd = new CheckBox { Foreground = ShellWindow.Paint("#F4F6FB") };
        _updates = new CheckBox { Foreground = ShellWindow.Paint("#F4F6FB") };
        _autostartHint = ShellWindow.Label("", 12);
        _autostartHint.Opacity = 0.7;
        _autostartHint.TextWrapping = TextWrapping.Wrap;
        _hotkeyTitle = ShellWindow.Label("", 16);
        _featureTitle = ShellWindow.Label("", 16);
        _about = ShellWindow.Label("", 13);
        _about.TextWrapping = TextWrapping.Wrap;
        _about.Opacity = 0.8;
        _check = new Button();
        _check.Click += async (_, _) =>
        {
            var result = await host.Updates.CheckAsync();
            if (result.Error is not null) host.RaiseStatus(result.Error);
            else if (result.Available && result.Version is not null)
            {
                host.RaiseStatus(string.Format(DesktopText.Culture, DesktopText.Get("Updates_Ready"), result.Version));
                await host.Updates.ApplyAsync();
            }
            else host.RaiseStatus(DesktopText.Get("Updates_None"));
        };
        _autostart.IsCheckedChanged += async (_, _) =>
        {
            if (_suppress) return;
            await host.Settings.UpdateAsync(settings => settings with { StartWithWindows = _autostart.IsChecked == true });
            host.SyncAutoStart();
        };
        _osd.IsCheckedChanged += async (_, _) =>
        {
            if (_suppress) return;
            await host.Settings.UpdateAsync(settings => settings with { ShowOsd = _osd.IsChecked == true });
        };
        _updates.IsCheckedChanged += async (_, _) =>
        {
            if (_suppress) return;
            await host.Settings.UpdateAsync(settings => settings with { CheckForUpdates = _updates.IsChecked == true });
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _language, _autostart, _autostartHint, _osd, _updates, _check,
                    _hotkeyTitle, _hotkeys, _featureTitle, _features, _about,
                },
            },
        };
        Retranslate();
    }

    public void Retranslate()
    {
        var settings = _host.Settings.Current;
        _suppress = true;
        ((ComboBoxItem)_language.Items[0]!).Content = DesktopText.Get("Settings_System");
        ((ComboBoxItem)_language.Items[1]!).Content = DesktopText.Get("Settings_English");
        ((ComboBoxItem)_language.Items[2]!).Content = DesktopText.Get("Settings_French");
        _language.SelectedIndex = settings.Language switch { "en-US" => 1, "fr-FR" => 2, _ => 0 };
        _autostart.Content = DesktopText.Get("Settings_Autostart");
        _autostart.IsChecked = settings.StartWithWindows;
        _autostart.IsEnabled = _host.Updates.IsInstalled;
        _autostartHint.Text = DesktopText.Get("Settings_AutostartHint");
        _osd.Content = DesktopText.Get("Settings_Osd");
        _osd.IsChecked = settings.ShowOsd;
        _updates.Content = DesktopText.Get("Settings_Updates");
        _updates.IsChecked = settings.CheckForUpdates;
        _check.Content = DesktopText.Get("Settings_Updates");
        _hotkeyTitle.Text = DesktopText.Get("Settings_Hotkeys");
        _featureTitle.Text = DesktopText.Get("Settings_Features");
        _about.Text = DesktopText.Get("About_Body") + " " + _host.Updates.CurrentVersion;
        _suppress = false;
        _hotkeys.Children.Clear();
        var failed = _host.Surfaces.Hotkeys.Failed.ToHashSet();
        var hotkeyDetail = (_host.Surfaces.Hotkeys as IHotkeyBackend)?.UnavailableReason;
        if (hotkeyDetail is not null)
        {
            var note = ShellWindow.Label(hotkeyDetail, 13);
            note.TextWrapping = TextWrapping.Wrap;
            _hotkeys.Children.Add(note);
        }
        foreach (var binding in settings.Hotkeys)
        {
            var text = VirtualKeyMap.Format(binding) + " — " + binding.Action;
            if (failed.Contains(binding)) text += " (" + DesktopText.Get("Settings_HotkeyFailed") + ")";
            _hotkeys.Children.Add(ShellWindow.Label(text, 13));
        }
        _features.Children.Clear();
        var caps = _host.Surfaces.Capabilities;
        Feature("Feature_Voicemeeter", caps.Voicemeeter);
        Feature("Feature_Devices", caps.SystemDevices && _host.Surfaces.Audio.IsAvailable);
        Feature("Feature_Apps", caps.PerAppVolume && _host.Surfaces.Audio.IsAvailable);
        Feature("Feature_Route", caps.PerAppRouting && _host.Surfaces.Audio.IsAvailable);
        Feature("Feature_Hotkeys", caps.GlobalHotkeys);
        Feature("Feature_Foreground", caps.ForegroundApps);
        Feature("Feature_Fl", caps.FlStudio);
    }

    private void Feature(string key, bool available)
    {
        var text = DesktopText.Get(key) + " — " + DesktopText.Get(available ? "Feature_Yes" : "Feature_No");
        var label = ShellWindow.Label(text, 13);
        label.Foreground = ShellWindow.Paint(available ? "#3DDC97" : "#9AA3B2");
        _features.Children.Add(label);
    }
}
