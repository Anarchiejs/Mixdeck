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

    public MixerViewModel Mixer { get; }
    public string DataPath { get; }
    public ObservableCollection<SettingsProfileChoice> ProfileChoices { get; } = [];
    public ObservableCollection<SettingsProfileChoice> HotkeyProfileChoices { get; } = [];
    public string DuckingThresholdText => DuckingThresholdDb.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " dBFS";
    public string DuckingAmountText => DuckingAmountDb.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " dB";
    public string DuckingHoldText => DuckingHoldMilliseconds.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " ms";
    public ObservableCollection<HotkeyRowViewModel> Hotkeys { get; } = [];
    public string FadeText => Math.Round(FadeMilliseconds).ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " ms";
    public string AccentSummary => settings.Current.AccentColor is { } color ? "Couleur personnalisée : " + color : "Couleur de Windows";
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
    public partial string SaveStatus { get; set; } = "Les modifications sont enregistrées automatiquement.";

    public void Refresh()
    {
        loading = true;
        var current = settings.Current;
        ProfileChoices.Clear();
        HotkeyProfileChoices.Clear();
        ProfileChoices.Add(new(null, "Aucun"));
        foreach (var profile in Mixer.Profiles)
        {
            var choice = new SettingsProfileChoice(profile.Id, profile.Name);
            ProfileChoices.Add(choice);
            HotkeyProfileChoices.Add(choice);
        }
        if (current.StartupProfileId is { } missing && ProfileChoices.All(p => p.Id != missing))
            ProfileChoices.Add(new(missing, "Profil indisponible"));
        StartupProfile = ProfileChoices.First(p => p.Id == current.StartupProfileId);
        AlwaysShowTrayIcon = current.AlwaysShowTrayIcon;
        ShowMicBadge = current.ShowMicBadge;
        PlayMicSounds = current.PlayMicSounds;
        ShowAppVolumes = current.ShowAppVolumes;
        DuckingEnabled = current.Ducking.Enabled;
        DuckingSourceStrip = Math.Clamp(current.Ducking.SourceStrip, 0, Mixer.Strips.Count - 1);
        DuckingTargetStrip = Math.Clamp(current.Ducking.TargetStrip, 0, Mixer.Strips.Count - 1);
        DuckingThresholdDb = Math.Clamp(current.Ducking.ThresholdDb, -60, -6);
        DuckingAmountDb = Math.Clamp(current.Ducking.AmountDb, -40, -1);
        DuckingHoldMilliseconds = Math.Clamp(current.Ducking.HoldMilliseconds, 100, 3000);
        StartWithWindows = current.StartWithWindows;
        AutoLaunchVoicemeeter = current.AutoLaunchVoicemeeter;
        ShowOsd = current.ShowOsd;
        CheckForUpdates = current.CheckForUpdates;
        ThemeIndex = (int)current.Theme;
        MicStripIndex = Math.Clamp(current.MicStripIndex, 0, Mixer.Strips.Count - 1);
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
            SaveStatus = "Enregistrement…";
            var success = await SettingsWriter.UpdateAsync(settings, change);
            HasError = !success;
            ErrorMessage = success ? string.Empty : "La modification n’a pas été enregistrée. Vérifiez l’accès au dossier de données et réessayez.";
            SaveStatus = success ? "Modifications enregistrées." : "Échec de l’enregistrement.";
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
            ReportError("Choisissez un profil existant pour ce raccourci.");
            return false;
        }
        var success = await SaveAsync(s => s with
        {
            Hotkeys = s.Hotkeys.Where(h => h.Action != row.Action).Concat(binding is null ? [] : new[] { binding }).ToArray(),
        });
        row.Refresh(settings.Current.Hotkeys.FirstOrDefault(h => h.Action == row.Action), hotkeys.Failed);
        return success;
    }

    public bool Conflicts(HotkeyBinding binding) => settings.Current.Hotkeys.Any(h => h.Action != binding.Action && h.Modifiers == binding.Modifiers && h.VirtualKey == binding.VirtualKey);
    public void SuspendHotkeys() => hotkeys.Apply([]);
    public void RestoreHotkeys()
    {
        hotkeys.Apply(settings.Current.Hotkeys);
        RefreshHotkeyWarnings();
    }
    public void RefreshHotkeyWarnings()
    {
        foreach (var row in Hotkeys) row.HasConflict = hotkeys.Failed.Any(h => h.Action == row.Action);
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try { shell.OpenLogsFolder(); }
        catch (Exception) { ReportError("Impossible d’ouvrir le dossier des journaux."); }
    }

    [RelayCommand]
    private void CopyDataPath()
    {
        try
        {
            var data = new DataPackage();
            data.SetText(DataPath);
            Clipboard.SetContent(data);
            SaveStatus = "Emplacement copié dans le Presse-papiers.";
        }
        catch (Exception) { ReportError("Impossible de copier l’emplacement des données."); }
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
    public string Combination => Binding is null ? "Non attribué" : HotkeyFormatter.Format(Binding);
    public string Name => Action switch
    {
        HotkeyAction.ToggleMic => "Couper / activer le micro",
        HotkeyAction.PushToTalk => "Parler en maintenant la touche",
        HotkeyAction.NextProfile => "Profil suivant",
        HotkeyAction.PreviousProfile => "Profil précédent",
        HotkeyAction.ApplyProfile => "Appliquer un profil",
        HotkeyAction.VolumeUp => "Volume +",
        HotkeyAction.VolumeDown => "Volume −",
        _ => "Ouvrir le panneau rapide",
    };
    public string EditLabel => "Modifier : " + Name;
    public string ClearLabel => "Effacer : " + Name;
    public string StepText => Converters.Db(StepDb);
    public DecimalFormatter StepFormatter { get; } = new(["fr-FR"], "FR") { FractionDigits = 1, IntegerDigits = 1 };

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

    partial void OnTargetBusChanged(int value) { if (!loading && value is >= 0 and < 5 && HasBinding) _ = owner.SaveHotkeyParameterAsync(this, b => b with { TargetBus = value }); }
    partial void OnStepDbChanged(double value) { if (!loading && double.IsFinite(value) && value is >= 0.1 and <= 12 && HasBinding) _ = owner.SaveHotkeyParameterAsync(this, b => b with { StepDb = (float)value }); }

    public void Refresh(HotkeyBinding? binding, IReadOnlyList<HotkeyBinding> failed)
    {
        loading = true;
        Binding = binding;
        TargetProfile = ProfileChoices.FirstOrDefault(p => p.Id == binding?.ProfileId);
        TargetBus = binding?.TargetBus ?? 0;
        StepDb = binding?.StepDb ?? 2;
        HasConflict = failed.Any(h => h.Action == Action);
        OnPropertyChanged(nameof(Combination));
        OnPropertyChanged(nameof(HasBinding));
        loading = false;
    }
}

public sealed record SettingsProfileChoice(string? Id, string Name);
