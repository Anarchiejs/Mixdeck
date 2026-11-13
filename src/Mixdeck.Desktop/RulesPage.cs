using Avalonia.Controls;
using Avalonia.Layout;
using Mixdeck.Core.Model;
using Mixdeck.Core.Rules;

namespace Mixdeck.Desktop;

public sealed class RulesPage : UserControl
{
    private readonly DesktopHost _host;
    private readonly TextBlock _hint;
    private readonly TextBox _process = new();
    private readonly ComboBox _trigger = new();
    private readonly ComboBox _action = new();
    private readonly TextBox _bus = new() { Text = "0" };
    private readonly TextBox _gain = new() { Text = "0" };
    private readonly TextBox _strip = new() { Text = "0" };
    private readonly TextBox _endpoint = new();
    private readonly CheckBox _revert = new();
    private readonly StackPanel _rows = new() { Spacing = 8 };

    public RulesPage(DesktopHost host)
    {
        _host = host;
        _hint = ShellWindow.Label("", 13);
        _hint.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        _hint.Opacity = 0.75;
        foreach (var box in new ComboBox[] { _trigger, _action })
            box.HorizontalAlignment = HorizontalAlignment.Left;
        var add = new Button();
        add.Click += async (_, _) => await AddAsync();
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _hint, _process, _trigger, _action, _bus, _gain, _strip, _endpoint, _revert, add, _rows,
                },
            },
        };
        Retranslate();
    }

    public void Retranslate()
    {
        _hint.Text = DesktopText.Get("Rules_Hint");
        _process.PlaceholderText = DesktopText.Get("Rules_Process");
        Fill(_trigger, "Rules_TriggerRunning", "Rules_TriggerForeground");
        Fill(_action, "Rules_ActionProfile", "Rules_ActionMicOff", "Rules_ActionBus", "Rules_ActionStrip", "Rules_ActionRoute");
        _bus.PlaceholderText = DesktopText.Get("Rules_Bus");
        _gain.PlaceholderText = DesktopText.Get("Rules_Gain");
        _strip.PlaceholderText = DesktopText.Get("Rules_Strip");
        _endpoint.PlaceholderText = DesktopText.Get("Rules_Endpoint");
        _revert.Content = DesktopText.Get("Rules_Revert");
        if (Content is ScrollViewer { Content: StackPanel panel } && panel.Children[9] is Button add)
            add.Content = DesktopText.Get("Rules_Add");
        Refresh();
    }

    private static void Fill(ComboBox box, params string[] keys)
    {
        var index = box.SelectedIndex;
        box.Items.Clear();
        foreach (var key in keys) box.Items.Add(DesktopText.Get(key));
        box.SelectedIndex = index >= 0 && index < box.ItemCount ? index : 0;
    }

    private void Refresh()
    {
        _rows.Children.Clear();
        if (_host.Settings.Current.Rules.Count == 0)
            _rows.Children.Add(ShellWindow.Label(DesktopText.Get("Rules_Empty"), 13));
        foreach (var rule in _host.Settings.Current.Rules)
        {
            var delete = new Button { Content = DesktopText.Get("Rules_Delete"), Tag = rule.Id };
            delete.Click += async (_, _) =>
            {
                await _host.SaveAsync(settings => settings with
                {
                    Rules = settings.Rules.Where(item => item.Id != rule.Id).ToArray(),
                });
                Refresh();
            };
            _rows.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { ShellWindow.Label(rule.Name + " — " + rule.ProcessName, 13), delete },
            });
        }
    }

    private async Task AddAsync()
    {
        var action = _action.SelectedIndex switch
        {
            1 => 1,
            2 => 3,
            3 => 4,
            4 => 5,
            _ => 0,
        };
        var draft = RuleDraft.Build(
            new Rule { Id = Guid.NewGuid().ToString("N"), Name = _process.Text ?? "", ProcessName = _process.Text ?? "" },
            _process.Text ?? "",
            _process.Text ?? "",
            _trigger.SelectedIndex,
            action,
            _host.Profiles.Profiles.FirstOrDefault()?.Id,
            int.TryParse(_bus.Text, out var bus) ? bus : 0,
            float.TryParse(_gain.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gain) ? gain : 0,
            int.TryParse(_strip.Text, out var strip) ? strip : 0,
            _endpoint.Text,
            _revert.IsChecked == true,
            appendExe: false);
        if (string.IsNullOrWhiteSpace(draft.ProcessName)) return;
        if (draft.Action == RuleActionKind.SwitchProfile && draft.ProfileId is null)
        {
            _host.RaiseStatus(DesktopText.Get("Profiles_Empty"));
            return;
        }
        await _host.SaveAsync(settings => settings with { Rules = settings.Rules.Append(draft).ToArray() });
        Refresh();
    }
}
