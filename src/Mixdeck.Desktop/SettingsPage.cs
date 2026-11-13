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
    private readonly Button _crash;
    private bool _suppress;
    private HotkeyAction? _capture;

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
            if (await host.SaveAsync(settings => settings with { StartWithWindows = _autostart.IsChecked == true }))
                host.SyncAutoStart();
        };
        _osd.IsCheckedChanged += async (_, _) =>
        {
            if (_suppress) return;
            await host.SaveAsync(settings => settings with { ShowOsd = _osd.IsChecked == true });
        };
        _updates.IsCheckedChanged += async (_, _) =>
        {
            if (_suppress) return;
            await host.SaveAsync(settings => settings with { CheckForUpdates = _updates.IsChecked == true });
        };
        _crash = new Button();
        _crash.Click += (_, _) =>
        {
            var text = Mixdeck.Core.Diagnostics.CrashReportCopy.ReadLatest(host.Paths.LogsDirectory);
            if (string.IsNullOrEmpty(text))
            {
                host.RaiseStatus(DesktopText.Get("About_NoCrash"));
                return;
            }
            var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            var window = new Window
            {
                Title = DesktopText.Get("About_CopyCrash"),
                Width = 720,
                Height = 480,
                Content = new ScrollViewer { Content = box, Margin = new Thickness(12) },
            };
            window.Show();
            host.RaiseStatus(DesktopText.Get("About_CrashCopied"));
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    _language, _autostart, _autostartHint, _osd, _updates, _check,
                    _hotkeyTitle, _hotkeys, _featureTitle, _features, _about, _crash,
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
        _crash.Content = DesktopText.Get("About_CopyCrash");
        _suppress = false;
        _hotkeys.Children.Clear();
        var failed = _host.Surfaces.Hotkeys.Failed.ToHashSet();
        var conflicts = HotkeyConflicts.Conflicting(settings.Hotkeys);
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
            if (conflicts.Any(item => item.Action == binding.Action)) text += " (" + DesktopText.Get("Hotkey_Conflict") + ")";
            var record = new Button { Content = DesktopText.Get("Hotkey_Record"), Tag = binding.Action };
            record.Click += (_, _) => _capture = (HotkeyAction)record.Tag!;
            _hotkeys.Children.Add(new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { ShellWindow.Label(text, 13), record },
            });
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

    public bool Capture(Avalonia.Input.KeyEventArgs e, out HotkeyBinding binding)
    {
        binding = new HotkeyBinding(HotkeyAction.ToggleMic, HotkeyModifiers.None, 0);
        if (_capture is not { } action) return false;
        if (!AvaloniaKeyMap.TryVirtualKey(e.Key, e.KeyModifiers, out var virtualKey, out var modifiers)) return false;
        var current = _host.Settings.Current.Hotkeys.FirstOrDefault(item => item.Action == action);
        if (current is null) return false;
        binding = current with { VirtualKey = virtualKey, Modifiers = modifiers };
        _capture = null;
        return true;
    }

    private void Feature(string key, bool available)
    {
        var text = DesktopText.Get(key) + " — " + DesktopText.Get(available ? "Feature_Yes" : "Feature_No");
        var label = ShellWindow.Label(text, 13);
        label.Foreground = ShellWindow.Paint(available ? "#3DDC97" : "#9AA3B2");
        _features.Children.Add(label);
    }
}
