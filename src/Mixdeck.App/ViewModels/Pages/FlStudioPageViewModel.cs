using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.Localization;
using Mixdeck.App.Services;
using Mixdeck.App.Views.Pages.Settings;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.ViewModels.Pages;

public sealed partial class FlStudioPageViewModel(IDaw daw, ISettingsStore settings, IUiDispatcher ui) : ObservableObject
{
    private bool active;
    private bool loading;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    public ObservableCollection<FlTrackViewModel> Tracks { get; } = [];
    [ObservableProperty] public partial bool IsConnected { get; set; }
    [ObservableProperty] public partial string StateText { get; set; } = "";
    [ObservableProperty] public partial string SetupText { get; set; } = "";
    [ObservableProperty] public partial string Error { get; set; } = "";
    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial string OutputPort { get; set; } = "";
    [ObservableProperty] public partial string InputPort { get; set; } = "";
    [ObservableProperty] public partial bool CaptureInProfiles { get; set; }
    public bool NeedsSetup => !IsConnected;

    public void Activate()
    {
        if (active) return;
        active = true;
        daw.StateChanged += StateChanged;
        daw.TrackChanged += TrackChanged;
        daw.SelectedTrackChanged += SelectedChanged;
        settings.Changed += SettingsChanged;
        LoadSettings(); Sync();
    }
    public void Deactivate()
    {
        active = false;
        daw.StateChanged -= StateChanged;
        daw.TrackChanged -= TrackChanged;
        daw.SelectedTrackChanged -= SelectedChanged;
        settings.Changed -= SettingsChanged;
    }
    private void StateChanged(object? sender, DawConnectionState state) => PostSync();
    private void TrackChanged(object? sender, DawTrack track) => PostSync();
    private void SelectedChanged(object? sender, int index) => PostSync();
    private void PostSync() => ui.Post(() => { if (active) Sync(); });
    private void SettingsChanged(object? sender, AppSettings value) => ui.Post(() => { if (active) LoadSettings(); });
    private void LoadSettings()
    {
        loading = true;
        var value = settings.Current.FlStudio;
        Enabled = value.Enabled; OutputPort = value.OutputPort; InputPort = value.InputPort; CaptureInProfiles = value.CaptureInProfiles;
        SetupText = Strings.Format("Fl_Setup", OutputPort, InputPort);
        loading = false;
    }
    private void Sync()
    {
        IsConnected = daw.State == DawConnectionState.Connected;
        OnPropertyChanged(nameof(NeedsSetup));
        StateText = Strings.Get(daw.State switch
        {
            DawConnectionState.Disabled => "Fl_State_Disabled",
            DawConnectionState.PortsMissing => "Fl_State_PortsMissing",
            DawConnectionState.WaitingForDaw => "Fl_State_WaitingForDaw",
            _ => "Fl_State_Connected",
        });
        var snapshot = daw.Tracks.Values.OrderBy(t => t.Index).ToArray();
        var indexes = snapshot.Select(t => t.Index).ToHashSet();
        foreach (var row in Tracks.Where(t => !indexes.Contains(t.Index)).ToArray()) Tracks.Remove(row);
        foreach (var track in snapshot)
        {
            var row = Tracks.FirstOrDefault(t => t.Index == track.Index);
            if (row is null) { row = new(daw, track.Index); Tracks.Insert(Tracks.TakeWhile(t => t.Index < track.Index).Count(), row); }
            row.Update(track, daw.SelectedTrack);
        }
    }
    partial void OnEnabledChanged(bool value) => Save(s => s with { Enabled = value });
    partial void OnOutputPortChanged(string value) => Save(s => s with { OutputPort = value.Trim() });
    partial void OnInputPortChanged(string value) => Save(s => s with { InputPort = value.Trim() });
    partial void OnCaptureInProfilesChanged(bool value) => Save(s => s with { CaptureInProfiles = value });
    private void Save(Func<FlStudioSettings, FlStudioSettings> change) { if (!loading) _ = SaveAsync(change); }
    private async Task SaveAsync(Func<FlStudioSettings, FlStudioSettings> change)
    {
        await saveGate.WaitAsync();
        try
        {
            if (!await SettingsWriter.UpdateAsync(settings, s => s with { FlStudio = change(s.FlStudio) }))
            { LoadSettings(); Error = Strings.Get("Fl_SaveError"); }
            else Error = "";
        }
        finally { saveGate.Release(); }
    }
}

public sealed partial class FlTrackViewModel(IDaw daw, int index) : ObservableObject
{
    private bool loading;
    public int Index => index;
    public ObservableCollection<FlSlotViewModel> Slots { get; } = [];
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial bool Selected { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VolumeText))] public partial double Volume { get; set; }
    [ObservableProperty] public partial bool Muted { get; set; }
    [ObservableProperty] public partial bool Effects { get; set; }
    public string VolumeText => Volume.ToString("P0", Strings.Culture);
    partial void OnVolumeChanged(double value) { if (!loading) daw.SetVolume(index, (float)value); }
    partial void OnMutedChanged(bool value) { if (!loading) daw.SetMute(index, value); }
    partial void OnEffectsChanged(bool value) { if (!loading) daw.SetSlotsEnabled(index, value); }
    [RelayCommand] private void Select() => daw.SelectTrack(index);
    public void Update(DawTrack track, int selected)
    {
        loading = true;
        Name = index == 0 ? "Master" : track.Name; Selected = selected == index;
        Volume = track.Volume; Muted = track.Muted; Effects = track.SlotsEnabled;
        foreach (var row in Slots.Where(s => !track.Slots.Any(t => t.Index == s.Index)).ToArray()) Slots.Remove(row);
        foreach (var slot in track.Slots.OrderBy(s => s.Index))
        {
            var row = Slots.FirstOrDefault(s => s.Index == slot.Index);
            if (row is null) { row = new(daw, index, slot.Index); Slots.Insert(Slots.TakeWhile(s => s.Index < slot.Index).Count(), row); }
            row.Update(slot.Enabled, slot.Name);
        }
        loading = false;
    }
}

public sealed partial class FlSlotViewModel(IDaw daw, int track, int index) : ObservableObject
{
    private bool loading;
    public int Index => index;
    // « Slot 3 · Pitcher » : le nom du plugin dit ce qu'on active ou coupe.
    [ObservableProperty] public partial string Name { get; private set; } = Strings.Format("Fl_Slot", index + 1);
    [ObservableProperty] public partial bool Enabled { get; set; }
    partial void OnEnabledChanged(bool value) { if (!loading) daw.SetSlot(track, index, value); }
    public void Update(bool value, string plugin)
    {
        loading = true;
        Enabled = value;
        Name = Strings.Format("Fl_Slot", index + 1) + (string.IsNullOrWhiteSpace(plugin) ? "" : " · " + plugin);
        loading = false;
    }
}
