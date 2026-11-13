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

