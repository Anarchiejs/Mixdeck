using Mixdeck.App.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Mixdeck.App.Services;
using Mixdeck.App.Views.Pages.Settings;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Audio;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.ViewModels.Pages;

public sealed partial class DevicesPageViewModel : ObservableObject
{
    private readonly IMixer mixer;
    private readonly ISettingsStore settings;
    private readonly IUiDispatcher ui;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private bool active;
    private bool loading;
    private int refreshVersion;
    private MixerLayout layout = MixerLayout.Banana;

    public DevicesPageViewModel(IMixer mixer, ISettingsStore settings, IUiDispatcher ui)
    {
        this.mixer = mixer; this.settings = settings; this.ui = ui;
        RefreshSettings();
    }

    public ObservableCollection<DeviceRowViewModel> Inputs { get; } = [];
    public ObservableCollection<DeviceRowViewModel> Outputs { get; } = [];
    public MixerLayout Layout => layout;
    public IReadOnlyList<MicPresetDefinition> Presets { get; } = MicPresets.GetAll(new AppTextProvider());
    public bool CanEdit => IsConnected && !IsRefreshing;
    public bool CanApplyPreset => CanEdit && IsPhysicalMic;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanApplyPreset))]
    public partial bool IsConnected { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanEdit), nameof(CanApplyPreset))]
    public partial bool IsRefreshing { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanApplyPreset))]
    public partial bool IsPhysicalMic { get; set; }
    [ObservableProperty]
    public partial string MicName { get; set; } = "";
    [ObservableProperty]
    public partial MicPresetDefinition? SelectedPreset { get; set; }
    [ObservableProperty]
    public partial bool HideVoicemeeterWindow { get; set; }
    [ObservableProperty]
    public partial bool HasMessage { get; set; }
    [ObservableProperty]
    public partial string Message { get; set; } = "";
    [ObservableProperty]
    public partial Microsoft.UI.Xaml.Controls.InfoBarSeverity Severity { get; set; }

    public void Activate()
    {
        if (active) return;
        active = true;
        mixer.StateChanged += OnStateChanged;
        settings.Changed += OnSettingsChanged;
        mixer.SnapshotChanged += OnSnapshotChanged;
        RefreshSettings();
        IsConnected = mixer.State == MixerConnectionState.Connected;
        RefreshDevicesCommand.Execute(null);
    }
    public void Deactivate()
    {
        active = false;
        refreshVersion++;
        mixer.StateChanged -= OnStateChanged;
        mixer.SnapshotChanged -= OnSnapshotChanged;
        settings.Changed -= OnSettingsChanged;
    }
    private void OnStateChanged(object? sender, MixerConnectionState state) => ui.Post(() =>
    {
        if (!active) return;
        IsConnected = state == MixerConnectionState.Connected;
        RefreshSettings();
        refreshVersion++;
        IsRefreshing = false;
        if (!IsConnected) Report(Strings.Get("CS_036"), true);
        else _ = RefreshDevicesAsync();
    });
    private void OnSettingsChanged(object? sender, AppSettings value) => ui.Post(() => { if (active) RefreshSettings(); });
    private void OnSnapshotChanged(object? sender, MixerSnapshot value) => ui.Post(() =>
    {
        if (!active) return;
        var changed = layout != mixer.Snapshot.Layout;
        RefreshSettings();
        if (changed && mixer.State == MixerConnectionState.Connected) _ = RefreshDevicesAsync();
    });

    private string ChannelName(int index, bool bus)
    {
        var custom = bus ? settings.Current.BusCustomization(index) : settings.Current.StripCustomization(index);
        if (!string.IsNullOrWhiteSpace(custom.DisplayName)) return custom.DisplayName;
        var snapshot = mixer.Snapshot;
        var label = bus ? snapshot.Buses.ElementAtOrDefault(index)?.Label : snapshot.Strips.ElementAtOrDefault(index)?.Label;
        return string.IsNullOrWhiteSpace(label) ? (bus ? layout.BusNames[index] : (index < layout.PhysicalStripCount ? Strings.Format("Core_InputName", index + 1) : layout.DefaultStripLabels[index])) : label;
    }
    private void RefreshSettings()
    {
        loading = true;
        var currentLayout = mixer.Snapshot.Layout;
        if (layout != currentLayout || Inputs.Count != currentLayout.PhysicalStripCount)
        {
            refreshVersion++;
            IsRefreshing = false;
            layout = currentLayout;
            Inputs.Clear();
            Outputs.Clear();
            foreach (var channel in Enumerable.Range(0, layout.PhysicalStripCount)) Inputs.Add(new(this, channel, false));
            foreach (var channel in Enumerable.Range(0, layout.PhysicalBusCount)) Outputs.Add(new(this, channel, true));
        }
        foreach (var row in Inputs.Concat(Outputs)) row.Name = ChannelName(row.Index, row.IsBus);
        var index = settings.Current.MicStripIndex;
        IsPhysicalMic = index >= 0 && index < layout.PhysicalStripCount;
        MicName = index >= 0 && index < layout.StripCount ? ChannelName(index, false) : Strings.Get("CS_016");
        SelectedPreset = Presets.FirstOrDefault(p => p.Preset == settings.Current.MicPreset) ?? Presets[0];
        HideVoicemeeterWindow = settings.Current.HideVoicemeeterWindow;
        loading = false;
    }

    [RelayCommand]
    private async Task RefreshDevicesAsync()
    {
        var version = ++refreshVersion;
        IsRefreshing = true;
        try
        {
            var inputs = await mixer.GetDevicesAsync(AudioDeviceDirection.Input);
            if (inputs.Count == 0 && mixer.State == MixerConnectionState.Connected)
            {
                // Certaines versions de Voicemeeter ne listent aucune entrée : on propose les micros actifs de Windows,
                // dont le nom est celui qu'attend Voicemeeter en WDM.
                inputs = await Task.Run(() => Mixdeck.Platform.Audio.WindowsAudioDevices.GetCaptureEndpoints()
                    .Select(e => new AudioDevice(AudioDeviceDriver.Wdm, e.Name, e.Id)).ToList());
            }
            var outputs = await mixer.GetDevicesAsync(AudioDeviceDirection.Output);
            var rows = Inputs.Concat(Outputs).ToArray();
            var names = new List<string?>();
            foreach (var row in rows)
                names.Add(await mixer.GetStringAsync(row.IsBus ? VmParam.BusDeviceName(row.Index) : VmParam.StripDeviceName(row.Index)));
            if (!active || version != refreshVersion) return;
            for (var i = 0; i < rows.Length; i++) rows[i].Populate(rows[i].IsBus ? outputs : inputs, names[i]);
            IsConnected = mixer.State == MixerConnectionState.Connected;
            Report(IsConnected ? Strings.Get("CS_037") : Strings.Get("CS_038"), !IsConnected);
        }
        catch (Exception) { if (active && version == refreshVersion) Report(Strings.Get("CS_039"), true); }
        finally { if (version == refreshVersion) IsRefreshing = false; }
    }

    internal void SelectDevice(DeviceRowViewModel row, DeviceChoice choice)
    {
        if (!CanEdit || choice.IsCurrent) return;
        try
        {
            var driver = choice.Device?.Driver ?? AudioDeviceDriver.Wdm;
            mixer.SetString(row.IsBus ? VmParam.BusDevice(row.Index, driver) : VmParam.StripDevice(row.Index, driver), choice.Device?.Name ?? "");
            Report(Strings.Get("CS_040"));
        }
        catch (Exception) { Report(Strings.Get("CS_041"), true); }
    }

    [RelayCommand]
    private async Task ApplyPresetAsync()
    {
        var preset = SelectedPreset;
        var strip = settings.Current.MicStripIndex;
        if (!CanApplyPreset || preset is null) { Report(Strings.Get("CS_042"), true); return; }
        await saveGate.WaitAsync();
        try
        {
            if (mixer.State != MixerConnectionState.Connected) { Report(Strings.Get("CS_043"), true); return; }
            if (strip < 0 || strip >= mixer.Layout.PhysicalStripCount) return;
            mixer.ApplyScript(MicPresets.CreateScript(strip, preset.Preset, mixer.Layout));
            var saved = await SettingsWriter.UpdateAsync(settings, s => s with { MicPreset = preset.Preset });
            Report(saved ? Strings.Format("CS_PresetApplied", preset.Name) : Strings.Get("CS_044"), !saved);
        }
        catch (Exception) { Report(Strings.Get("CS_045"), true); }
        finally { saveGate.Release(); }
    }

    partial void OnHideVoicemeeterWindowChanged(bool value) { if (!loading) _ = SaveWindowPreferenceAsync(value); }
    private async Task SaveWindowPreferenceAsync(bool value)
    {
        await saveGate.WaitAsync();
        try
        {
            if (!await SettingsWriter.UpdateAsync(settings, s => s with { HideVoicemeeterWindow = value }))
            {
                RefreshSettings();
                Report(Strings.Get("CS_046"), true);
            }
        }
        finally { saveGate.Release(); }
    }
    public void RestartEngine()
    {
        if (!CanEdit) { Report(Strings.Get("CS_043"), true); return; }
        try { mixer.SetFloat(VmParam.CommandRestart, 1); Report(Strings.Get("CS_047")); }
        catch (Exception) { Report(Strings.Get("CS_048"), true); }
    }
    public void Report(string text, bool error = false)
    {
        Message = text;
        Severity = error ? Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning : Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
        HasMessage = true;
    }
}

public sealed record DeviceChoice(AudioDevice? Device, string Name, bool IsCurrent = false);

public sealed partial class DeviceRowViewModel(DevicesPageViewModel owner, int index, bool isBus) : ObservableObject
{
    private bool loading;
    private readonly string kind = isBus ? owner.Layout.BusNames[index] + Strings.Get("CS_049") : Strings.Get("CS_050") + (index + 1);
    public DevicesPageViewModel Owner => owner;
    public int Index => index;
    public bool IsBus => isBus;
    public string Kind => kind;
    // Liste simple : la ComboBox de WinUI n'affiche pas de groupes, le pilote figure donc dans chaque nom.
    public ObservableCollection<DeviceChoice> Choices { get; } = [];
    [ObservableProperty]
    public partial string Name { get; set; } = "";
    [ObservableProperty]
    public partial DeviceChoice? SelectedDevice { get; set; }
    [ObservableProperty]
    public partial string CurrentName { get; set; } = Strings.Get("CS_051");
    partial void OnSelectedDeviceChanged(DeviceChoice? value) { if (!loading && value is not null) owner.SelectDevice(this, value); }
    public void Populate(IReadOnlyList<AudioDevice> devices, string? current)
    {
        loading = true;
        var none = new DeviceChoice(null, Strings.Get("CS_051"));
        Choices.Clear();
        Choices.Add(none);
        CurrentName = string.IsNullOrWhiteSpace(current) ? Strings.Get("CS_051") : current;
        DeviceChoice selected = none;
        if (!string.IsNullOrWhiteSpace(current))
        {
            // Le nom seul ne permet pas de déduire le pilote : ne pas prétendre que l’appareil courant utilise WDM.
            selected = new(null, current + Strings.Get("CS_052"), true);
            Choices.Add(selected);
        }
        AudioDeviceDriver[] drivers = [AudioDeviceDriver.Wdm, AudioDeviceDriver.Ks, AudioDeviceDriver.Mme, AudioDeviceDriver.Asio];
        foreach (var driver in drivers)
        {
            var label = driver == AudioDeviceDriver.Wdm ? Strings.Get("CS_053") : VmParam.DriverKey(driver).ToUpperInvariant();
            foreach (var device in devices.Where(d => d.Driver == driver).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
                Choices.Add(new DeviceChoice(device, $"{device.Name} · {label}"));
        }
        SelectedDevice = selected;
        loading = false;
    }
}
