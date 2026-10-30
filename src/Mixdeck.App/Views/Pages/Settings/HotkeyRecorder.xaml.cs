using Mixdeck.App.Localization;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mixdeck.Core.Model;
using Mixdeck.Core.Abstractions;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.Platform.Hotkeys;
using Windows.System;
using Windows.UI.Core;

namespace Mixdeck.App.Views.Pages.Settings;

public sealed partial class HotkeyRecorder : UserControl
{
    private readonly HotkeyAction action;
    private readonly int targetBus;
    private readonly float stepDb;
    private readonly Func<HotkeyBinding, bool> conflicts;
    private bool isDaw;
    public HotkeyRecorder(HotkeyAction action, int targetBus, float stepDb, Func<HotkeyBinding, bool> conflicts, IReadOnlyList<SettingsProfileChoice> profiles, string? profileId, int dawTrack = 0, int dawSlot = 0)
    {
        this.action = action;
        this.targetBus = targetBus;
        this.stepDb = stepDb;
        this.conflicts = conflicts;
        InitializeComponent();
        ProfilePicker.Visibility = action == HotkeyAction.ApplyProfile ? Visibility.Visible : Visibility.Collapsed;
        ProfilePicker.ItemsSource = profiles.Where(p => p.Id is not null).ToArray();
        ProfilePicker.SelectedItem = profiles.FirstOrDefault(p => p.Id == profileId && p.Id is not null);
        isDaw = action is HotkeyAction.ToggleDawSlot or HotkeyAction.ToggleDawTrackMute;
        var daw = App.GetService<IDaw>();
        var choices = daw.State == DawConnectionState.Connected
            ? daw.Tracks.Values.OrderBy(t => t.Index).Select(t => new DawTrackChoice(t.Index, t.Index == 0 ? "Master" : t.Index + " · " + t.Name)).ToList() : [];
        TrackPicker.Header = Strings.Get("Fl_TrackChoice");
        TrackNumber.Header = Strings.Get("Fl_TrackNumber");
        SlotNumber.Header = Strings.Get("Fl_SlotNumber");
        TrackPicker.Visibility = isDaw && choices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TrackNumber.Visibility = isDaw && choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SlotNumber.Visibility = action == HotkeyAction.ToggleDawSlot ? Visibility.Visible : Visibility.Collapsed;
        if (choices.Count > 0 && choices.All(t => t.Index != dawTrack)) choices.Add(new(dawTrack, dawTrack.ToString(Strings.Culture)));
        TrackPicker.ItemsSource = choices;
        TrackPicker.SelectedItem = choices.FirstOrDefault(t => t.Index == dawTrack);
        TrackNumber.Value = dawTrack;
        SlotNumber.Value = dawSlot + 1;
    }
    private HotkeyBinding? captured;
    public HotkeyBinding? Value
    {
        get
        {
            if (captured is null || (action == HotkeyAction.ApplyProfile && ProfilePicker.SelectedItem is not SettingsProfileChoice { Id: not null })) return null;
            var track = TrackPicker.Visibility == Visibility.Visible ? (TrackPicker.SelectedItem as DawTrackChoice)?.Index ?? double.NaN : TrackNumber.Value;
            var slot = SlotNumber.Value;
            if (isDaw && (!double.IsFinite(track) || track < 0 || track > 16383 || track != Math.Truncate(track))) return null;
            if (action == HotkeyAction.ToggleDawSlot && (!double.IsFinite(slot) || slot < 1 || slot > 10 || slot != Math.Truncate(slot))) return null;
            return captured with { ProfileId = action == HotkeyAction.ApplyProfile ? ((SettingsProfileChoice)ProfilePicker.SelectedItem).Id : null,
                DawTrack = isDaw ? (int)track : 0, DawSlot = action == HotkeyAction.ToggleDawSlot ? (int)slot - 1 : 0 };
        }
    }
    private void OnTargetChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (captured is not null) { ValidationError.IsOpen = false; Captured?.Invoke(this, EventArgs.Empty); }
    }
    private void OnProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (captured is not null) { ValidationError.IsOpen = false; Captured?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? Captured;
    private void OnLoaded(object sender, RoutedEventArgs e) => CaptureButton.Focus(FocusState.Programmatic);
    public void ShowError(string message) { ValidationError.Message = message; ValidationError.IsOpen = true; }
    private static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
    private void OnCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) return;
        var modifiers = HotkeyModifiers.None;
        if (Down(VirtualKey.Control)) modifiers |= HotkeyModifiers.Control;
        if (Down(VirtualKey.Menu)) modifiers |= HotkeyModifiers.Alt;
        if (Down(VirtualKey.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= HotkeyModifiers.Win;
        if (e.Key == VirtualKey.Tab && modifiers is HotkeyModifiers.None or HotkeyModifiers.Shift) return;
        e.Handled = true;
        var key = (int)e.Key;
        if (key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) return;
        captured = null;
        if (!HotkeyFormatter.IsValidCombination(modifiers, key))
            ShowError(Strings.Get("CS_137"));
        else
        {
            var binding = new HotkeyBinding(action, modifiers, key, targetBus, stepDb);
            if (conflicts(binding)) ShowError(Strings.Get("CS_138"));
            else
            {
                captured = binding;
                ValidationError.IsOpen = false;
                CombinationText.Text = LocalizedHotkeyFormatter.Format(binding);
                if (Value is null) ShowError(Strings.Get(isDaw ? "Fl_ChooseTarget" : "CS_139"));
            }
        }
        Captured?.Invoke(this, EventArgs.Empty);
    }
}

public sealed record DawTrackChoice(int Index, string Name);
