using Mixdeck.App.Localization;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Pages.Settings;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Hotkeys;
using Windows.ApplicationModel.DataTransfer;
using Windows.Globalization.NumberFormatting;

namespace Mixdeck.App.ViewModels.Pages;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private readonly ISettingsStore settings;
    private readonly IHotkeyService hotkeys;
    private readonly IShellService shell;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private bool loading;
    private CancellationTokenSource? fadeDelay;
    private Task pendingFade = Task.CompletedTask;
    private CancellationTokenSource? duckingDelay;
    private Task pendingDucking = Task.CompletedTask;
    private int pendingSaves;
    private bool refreshAfterFailure;

    public SettingsPageViewModel(ISettingsStore settings, MixerViewModel mixer, IHotkeyService hotkeys, IShellService shell, MixdeckPaths paths)
    {
        this.settings = settings;
        this.hotkeys = hotkeys;
        this.shell = shell;
        Mixer = mixer;
        DataPath = paths.Root;
        foreach (var action in Enum.GetValues<HotkeyAction>()) Hotkeys.Add(new(action, this));
        Refresh();
    }

    [ObservableProperty]
    public partial int LanguageIndex { get; set; }

    partial void OnLanguageIndexChanged(int value)
    {
        if (value is >= 0 and <= 2)
            QueueSave(s => s with { Language = value == 1 ? "fr-FR" : value == 2 ? "en-US" : null });
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        try
        {
            await FlushAsync();
            if (HasError) return;
            if (Environment.ProcessPath is not { } executable) throw new InvalidOperationException();
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false };
            foreach (var argument in Environment.GetCommandLineArgs().Skip(1).Where(a => a != "--restart" && a != "--after-crash"))
                start.ArgumentList.Add(argument);
            start.ArgumentList.Add("--restart");
            using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException();
            shell.Exit();
        }
        catch (Exception) { ReportError(Strings.Get("Settings_RestartError")); }
    }

    public MixerViewModel Mixer { get; }
    public string DataPath { get; }
    public ObservableCollection<SettingsProfileChoice> ProfileChoices { get; } = [];
    public ObservableCollection<SettingsProfileChoice> HotkeyProfileChoices { get; } = [];
    public string DuckingThresholdText => DuckingThresholdDb.ToString("0", Strings.Culture) + " dBFS";
    public string DuckingAmountText => DuckingAmountDb.ToString("0", Strings.Culture) + " dB";
    public string DuckingHoldText => DuckingHoldMilliseconds.ToString("0", Strings.Culture) + " ms";
    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];
    public string FadeText => Math.Round(FadeMilliseconds).ToString("0", Strings.Culture) + " ms";
    public string AccentSummary => settings.Current.AccentColor is { } color ? Strings.Get("Settings_AccentCustom") + color : Strings.Get("Settings_AccentWindows");
    public string? AccentColor => settings.Current.AccentColor;

    [ObservableProperty]
    public partial bool AlwaysShowTrayIcon { get; set; }
    [ObservableProperty]
    public partial bool ShowMicBadge { get; set; }
    [ObservableProperty]
    public partial bool PlayMicSounds { get; set; }
    [ObservableProperty]
    public partial bool ShowAppVolumes { get; set; }
    [ObservableProperty]
    public partial SettingsProfileChoice? StartupProfile { get; set; }
    [ObservableProperty]
    public partial bool DuckingEnabled { get; set; }
    [ObservableProperty]
    public partial int DuckingSourceStrip { get; set; }
    [ObservableProperty]
    public partial int DuckingTargetStrip { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DuckingThresholdText))]
    public partial double DuckingThresholdDb { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DuckingAmountText))]
    public partial double DuckingAmountDb { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DuckingHoldText))]
    public partial double DuckingHoldMilliseconds { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }
    [ObservableProperty]
    public partial bool AutoLaunchVoicemeeter { get; set; }
    [ObservableProperty]
    public partial bool ShowOsd { get; set; }
    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }
    [ObservableProperty]
    public partial int MicStripIndex { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FadeText))]
    public partial double FadeMilliseconds { get; set; }
    [ObservableProperty]
    public partial bool HasError { get; set; }
    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;
    [ObservableProperty]
    public partial string SaveStatus { get; set; } = Strings.Get("Settings_AutoSave");

    public void Refresh()
    {
        loading = true;
        var current = settings.Current;
        ProfileChoices.Clear();
        HotkeyProfileChoices.Clear();
        ProfileChoices.Add(new(null, Strings.Get("Settings_None")));
        foreach (var profile in Mixer.Profiles)
        {
            var choice = new SettingsProfileChoice(profile.Id, profile.Name);
            ProfileChoices.Add(choice);
            HotkeyProfileChoices.Add(choice);
        }
        if (current.StartupProfileId is { } missing && ProfileChoices.All(p => p.Id != missing))
            ProfileChoices.Add(new(missing, Strings.Get("Settings_MissingProfile")));
        StartupProfile = ProfileChoices.First(p => p.Id == current.StartupProfileId);
        AlwaysShowTrayIcon = current.AlwaysShowTrayIcon;
        ShowMicBadge = current.ShowMicBadge;
        PlayMicSounds = current.PlayMicSounds;
        ShowAppVolumes = current.ShowAppVolumes;
        DuckingEnabled = current.Ducking.Enabled;
        DuckingSourceStrip = current.Ducking.SourceStrip >= 0 && current.Ducking.SourceStrip < Mixer.Strips.Count ? current.Ducking.SourceStrip : -1;
        DuckingTargetStrip = current.Ducking.TargetStrip >= 0 && current.Ducking.TargetStrip < Mixer.Strips.Count ? current.Ducking.TargetStrip : -1;
        DuckingThresholdDb = Math.Clamp(current.Ducking.ThresholdDb, -60, -6);
        DuckingAmountDb = Math.Clamp(current.Ducking.AmountDb, -40, -1);
        DuckingHoldMilliseconds = Math.Clamp(current.Ducking.HoldMilliseconds, 100, 3000);
        StartWithWindows = current.StartWithWindows;
        AutoLaunchVoicemeeter = current.AutoLaunchVoicemeeter;
        ShowOsd = current.ShowOsd;
        CheckForUpdates = current.CheckForUpdates;
        ThemeIndex = (int)current.Theme;
        LanguageIndex = current.Language == "fr-FR" ? 1 : current.Language == "en-US" ? 2 : 0;
        MicStripIndex = current.MicStripIndex >= 0 && current.MicStripIndex < Mixer.Strips.Count ? current.MicStripIndex : -1;
        FadeMilliseconds = Math.Clamp(current.FadeMilliseconds, 0, 2000);
        foreach (var row in Hotkeys) row.Refresh(current.Hotkeys.FirstOrDefault(h => h.Action == row.Action), hotkeys.Failed);
        OnPropertyChanged(nameof(AccentSummary));
        loading = false;
    }

    private void QueueSave(Func<AppSettings, AppSettings> change)
    {
        if (!loading) _ = SaveAsync(change);
    }

    partial void OnAlwaysShowTrayIconChanged(bool value) => QueueSave(s => s with { AlwaysShowTrayIcon = value });
    partial void OnShowMicBadgeChanged(bool value) => QueueSave(s => s with { ShowMicBadge = value });
    partial void OnPlayMicSoundsChanged(bool value) => QueueSave(s => s with { PlayMicSounds = value });
    partial void OnShowAppVolumesChanged(bool value) => QueueSave(s => s with { ShowAppVolumes = value });
    partial void OnStartupProfileChanged(SettingsProfileChoice? value) { if (value is not null) QueueSave(s => s with { StartupProfileId = value.Id }); }
    partial void OnDuckingEnabledChanged(bool value) => QueueSave(s => s with { Ducking = s.Ducking with { Enabled = value } });
    partial void OnDuckingSourceStripChanged(int value) => SaveDuckingChannels(true, value);
    partial void OnDuckingTargetStripChanged(int value) => SaveDuckingChannels(false, value);
    private void SaveDuckingChannels(bool sourceChanged, int value)
    {
        if (loading || value < 0 || value >= Mixer.Strips.Count) return;
        if (DuckingSourceStrip == DuckingTargetStrip)
        {
            loading = true;
            if (sourceChanged) DuckingTargetStrip = (value + 1) % Mixer.Strips.Count;
            else DuckingSourceStrip = (value + 1) % Mixer.Strips.Count;
            loading = false;
        }
        var source = DuckingSourceStrip;
        var target = DuckingTargetStrip;
        if (source < 0 || source >= Mixer.Strips.Count || target < 0 || target >= Mixer.Strips.Count) return;
        QueueSave(s => s with { Ducking = s.Ducking with { SourceStrip = source, TargetStrip = target } });
    }
    partial void OnDuckingThresholdDbChanged(double value) { if (double.IsFinite(value) && value is >= -60 and <= -6) QueueDuckingSave(); }
    partial void OnDuckingAmountDbChanged(double value) { if (double.IsFinite(value) && value is >= -40 and <= -1) QueueDuckingSave(); }
    partial void OnDuckingHoldMillisecondsChanged(double value) { if (double.IsFinite(value) && value is >= 100 and <= 3000) QueueDuckingSave(); }
    private void QueueDuckingSave()
    {
        if (loading) return;
        duckingDelay?.Cancel();
        duckingDelay?.Dispose();
        duckingDelay = new();
        pendingDucking = SaveDuckingAsync((float)DuckingThresholdDb, (float)DuckingAmountDb, (int)Math.Round(DuckingHoldMilliseconds), duckingDelay.Token);
    }
    private async Task SaveDuckingAsync(float threshold, float amount, int hold, CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            await SaveAsync(s => s with { Ducking = s.Ducking with { ThresholdDb = threshold, AmountDb = amount, HoldMilliseconds = hold } });
        }
        catch (OperationCanceledException) { }
    }

    partial void OnStartWithWindowsChanged(bool value) => QueueSave(s => s with { StartWithWindows = value });
    partial void OnAutoLaunchVoicemeeterChanged(bool value) => QueueSave(s => s with { AutoLaunchVoicemeeter = value });
    partial void OnShowOsdChanged(bool value) => QueueSave(s => s with { ShowOsd = value });
    partial void OnCheckForUpdatesChanged(bool value) => QueueSave(s => s with { CheckForUpdates = value });
    partial void OnThemeIndexChanged(int value) { if (value is >= 0 and <= 2) QueueSave(s => s with { Theme = (ThemeMode)value }); }
    partial void OnMicStripIndexChanged(int value) { if (value >= 0 && value < Mixer.Strips.Count) QueueSave(s => s with { MicStripIndex = value }); }
    partial void OnFadeMillisecondsChanged(double value)
    {
        if (loading) return;
        fadeDelay?.Cancel();
        fadeDelay?.Dispose();
        fadeDelay = new();
        pendingFade = SaveFadeAsync((int)Math.Clamp(Math.Round(value), 0, 2000), fadeDelay.Token);
    }

    private async Task SaveFadeAsync(int value, CancellationToken token)
    {
        try
        {
            await Task.Delay(250, token);
            await SaveAsync(s => s with { FadeMilliseconds = value });
        }
        catch (OperationCanceledException) { }
    }

    public async Task<bool> SaveAsync(Func<AppSettings, AppSettings> change)
    {
        pendingSaves++;
        await saveGate.WaitAsync();
        try
        {
            SaveStatus = Strings.Get("Settings_Saving");
            var success = await SettingsWriter.UpdateAsync(settings, change);
            HasError = !success;
            ErrorMessage = success ? string.Empty : Strings.Get("Settings_SaveError");
            SaveStatus = success ? Strings.Get("Settings_Saved") : Strings.Get("Settings_SaveFailed");
            if (!success) refreshAfterFailure = true;
            OnPropertyChanged(nameof(AccentSummary));
            RefreshHotkeyWarnings();
            return success;
        }
        finally
        {
            saveGate.Release();
            if (--pendingSaves == 0 && refreshAfterFailure)
            {
                refreshAfterFailure = false;
                Refresh();
            }
        }
    }

    public void ReportError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    public Task<bool> SetAccentAsync(string? color) => SaveAsync(s => s with { AccentColor = color });

    public async Task FlushAsync()
    {
        await Task.WhenAll(pendingFade, pendingDucking);
        await saveGate.WaitAsync();
        saveGate.Release();
    }

    public async Task SaveHotkeyParameterAsync(HotkeyRowViewModel row, Func<HotkeyBinding, HotkeyBinding> change)
    {
        await SaveAsync(s => s with { Hotkeys = s.Hotkeys.Select(h => h.Action == row.Action ? change(h) : h).ToArray() });
        row.Refresh(settings.Current.Hotkeys.FirstOrDefault(h => h.Action == row.Action), hotkeys.Failed);
    }

    public async Task<bool> SaveHotkeyAsync(HotkeyRowViewModel row, HotkeyBinding? binding)
    {
        if (binding is { Action: HotkeyAction.ApplyProfile } && !Mixer.Profiles.Any(p => p.Id == binding.ProfileId))
        {
            ReportError(Strings.Get("Settings_ChooseProfile"));
            return false;
        }
        if (binding is not null && binding.VirtualKey != 0 && HotkeyConflicts.Find(settings.Current.Hotkeys, binding).Count > 0)
        {
            ReportError(Strings.Get("Settings_HotkeyConflict"));
            return false;
        }
        var success = await SaveAsync(s => s with
        {
            Hotkeys = s.Hotkeys.Where(h => h.Action != row.Action).Concat(binding is null ? [] : new[] { binding }).ToArray(),
        });
        row.Refresh(settings.Current.Hotkeys.FirstOrDefault(h => h.Action == row.Action), hotkeys.Failed);
        return success;
    }

    public bool Conflicts(HotkeyBinding binding) => HotkeyConflicts.Find(settings.Current.Hotkeys, binding).Count > 0;
    public void SuspendHotkeys() => hotkeys.Apply([]);
    public void RestoreHotkeys()
    {
        hotkeys.Apply(settings.Current.Hotkeys);
        RefreshHotkeyWarnings();
    }
    public void RefreshHotkeyWarnings()
    {
        var conflicts = HotkeyConflicts.Conflicting(settings.Current.Hotkeys);
        foreach (var row in Hotkeys)
            row.HasConflict = hotkeys.Failed.Any(h => h.Action == row.Action) || conflicts.Any(h => h.Action == row.Action);
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try { shell.OpenLogsFolder(); }
        catch (Exception) { ReportError(Strings.Get("Settings_OpenLogsError")); }
    }

    [RelayCommand]
    private void CopyDataPath()
    {
        try
        {
            var data = new DataPackage();
            data.SetText(DataPath);
            Clipboard.SetContent(data);
            SaveStatus = Strings.Get("Settings_Copied");
        }
        catch (Exception) { ReportError(Strings.Get("Settings_CopyError")); }
    }
}

public sealed partial class HotkeyRowViewModel : ObservableObject
{
    private readonly SettingsPageViewModel owner;
    private bool loading;
    public HotkeyRowViewModel(HotkeyAction action, SettingsPageViewModel owner) { Action = action; this.owner = owner; }
    public HotkeyAction Action { get; }
    public MixerViewModel Mixer => owner.Mixer;
    public HotkeyBinding? Binding { get; private set; }
    public bool IsApplyProfile => Action == HotkeyAction.ApplyProfile;
    public ObservableCollection<SettingsProfileChoice> ProfileChoices => owner.HotkeyProfileChoices;
    public bool IsVolume => Action is HotkeyAction.VolumeUp or HotkeyAction.VolumeDown;
    public bool HasBinding => Binding is not null;
    public string Combination => Binding is null ? Strings.Get("Settings_Unassigned") : LocalizedHotkeyFormatter.Format(Binding);
    public string Name => Action switch
    {
        HotkeyAction.ToggleMic => Strings.Get("Settings_ToggleMic"),
        HotkeyAction.PushToTalk => Strings.Get("Settings_PushToTalk"),
        HotkeyAction.NextProfile => Strings.Get("Settings_NextProfile"),
        HotkeyAction.PreviousProfile => Strings.Get("Settings_PreviousProfile"),
        HotkeyAction.ApplyProfile => Strings.Get("Settings_ApplyProfile"),
        HotkeyAction.ToggleDawSlot => Strings.Get("Settings_ToggleDawSlot"),
        HotkeyAction.ToggleDawTrackMute => Strings.Get("Settings_ToggleDawTrackMute"),
        HotkeyAction.VolumeUp => Strings.Get("Settings_VolumeUp"),
        HotkeyAction.VolumeDown => Strings.Get("Settings_VolumeDown"),
        _ => Strings.Get("Settings_OpenFlyout"),
    };
    public string EditLabel => Strings.Get("Settings_Edit") + Name;
    public string ClearLabel => Strings.Get("Settings_Clear") + Name;
    public string StepText => Converters.Db(StepDb);
    public bool IsDaw => Action is HotkeyAction.ToggleDawSlot or HotkeyAction.ToggleDawTrackMute;
    public string DawTarget => Binding is null ? "" : Strings.Get("Fl_TrackNumber") + ": " + Binding.DawTrack
        + (Action == HotkeyAction.ToggleDawSlot ? " · " + Strings.Format("Fl_Slot", Binding.DawSlot + 1) : "");
    public DecimalFormatter StepFormatter { get; } = new([Strings.Language], new RegionInfo(Strings.Language).TwoLetterISORegionName) { FractionDigits = 1, IntegerDigits = 1 };

    [ObservableProperty]
    public partial bool HasConflict { get; set; }
    [ObservableProperty]
    public partial int TargetBus { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText))]
    public partial double StepDb { get; set; } = 2;

    [ObservableProperty]
    public partial SettingsProfileChoice? TargetProfile { get; set; }
    partial void OnTargetProfileChanged(SettingsProfileChoice? value)
    {
        if (!loading && value?.Id is { } id && HasBinding && IsApplyProfile)
            _ = owner.SaveHotkeyParameterAsync(this, b => b with { ProfileId = id });
    }

    partial void OnTargetBusChanged(int value) { if (!loading && value >= 0 && value < Mixer.Buses.Count && HasBinding) _ = owner.SaveHotkeyParameterAsync(this, b => b with { TargetBus = value }); }
    partial void OnStepDbChanged(double value) { if (!loading && double.IsFinite(value) && value is >= 0.1 and <= 12 && HasBinding) _ = owner.SaveHotkeyParameterAsync(this, b => b with { StepDb = (float)value }); }

    public void Refresh(HotkeyBinding? binding, IReadOnlyList<HotkeyBinding> failed)
    {
        loading = true;
        Binding = binding;
        TargetProfile = ProfileChoices.FirstOrDefault(p => p.Id == binding?.ProfileId);
        TargetBus = binding is null ? 0 : binding.TargetBus >= 0 && binding.TargetBus < Mixer.Buses.Count ? binding.TargetBus : -1;
        StepDb = binding?.StepDb ?? 2;
        HasConflict = failed.Any(h => h.Action == Action);
        OnPropertyChanged(nameof(Combination));
        OnPropertyChanged(nameof(HasBinding));
        OnPropertyChanged(nameof(DawTarget));
        loading = false;
    }
}

public sealed record SettingsProfileChoice(string? Id, string Name);
