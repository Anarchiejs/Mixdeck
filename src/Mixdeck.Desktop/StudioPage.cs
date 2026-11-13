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

