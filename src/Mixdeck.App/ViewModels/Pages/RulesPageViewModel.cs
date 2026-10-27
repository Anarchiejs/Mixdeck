using Mixdeck.App.Localization;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Pages.Settings;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Pages;

public sealed partial class RulesPageViewModel(ISettingsStore settings, MixerViewModel mixer, IProcessWatcher processes) : ObservableObject
{
    private readonly SemaphoreSlim saveGate = new(1, 1);
    public MixerViewModel Mixer { get; } = mixer;
    public ObservableCollection<RuleCardViewModel> Rules { get; } = [];
    public bool IsEmpty => Rules.Count == 0;
    public bool HasRules => !IsEmpty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    public void Refresh()
    {
        Rules.Clear();
        foreach (var rule in settings.Current.Rules.OrderByDescending(r => r.Priority))
            Rules.Add(new RuleCardViewModel(rule, this));
        UpdatePositions();
    }

    private void UpdatePositions()
    {
        for (var i = 0; i < Rules.Count; i++)
        {
            Rules[i].CanMoveUp = i > 0;
            Rules[i].CanMoveDown = i < Rules.Count - 1;
            Rules[i].Position = Strings.Format("CS_Priority", i + 1);
        }
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasRules));
    }

    public RuleEditorViewModel CreateEditor(Rule? rule) => new(rule, Mixer);

    public async Task<string[]> GetProcessesAsync()
    {
        try { return await Task.Run(() => processes.GetRunningProcessNames().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray()); }
        catch (Exception) { return []; } // La saisie libre reste disponible si l'énumération échoue.
    }

    public Task<bool> SaveRuleAsync(Rule rule) => SaveAsync(next =>
    {
        var index = next.FindIndex(r => r.Id == rule.Id);
        if (index < 0) next.Add(rule); else next[index] = rule;
        return next;
    });

    public Task<bool> DeleteAsync(RuleCardViewModel item) => SaveAsync(next => next.Where(r => r.Id != item.Rule.Id).ToList());

    public Task<bool> SetEnabledAsync(RuleCardViewModel item, bool value) =>
        SaveAsync(next => next.Select(r => r.Id == item.Rule.Id ? r with { Enabled = value } : r).ToList());

    public Task<bool> SetRevertAsync(RuleCardViewModel item, bool value) =>
        SaveAsync(next => next.Select(r => r.Id == item.Rule.Id ? r with { RevertWhenInactive = value } : r).ToList());

    [RelayCommand]
    private async Task MoveUpAsync(RuleCardViewModel? item) => await MoveAsync(item, -1);

    [RelayCommand]
    private async Task MoveDownAsync(RuleCardViewModel? item) => await MoveAsync(item, 1);

    private async Task MoveAsync(RuleCardViewModel? item, int offset)
    {
        if (item is null) return;
        await SaveAsync(next =>
        {
            var index = next.FindIndex(r => r.Id == item.Rule.Id);
            var target = index + offset;
            if (index >= 0 && target >= 0 && target < next.Count)
                (next[index], next[target]) = (next[target], next[index]);
            return next;
        });
    }

    private async Task<bool> SaveAsync(Func<List<Rule>, List<Rule>> change)
    {
        await saveGate.WaitAsync();
        IsBusy = true;
        HasError = false;
        try
        {
            Rule[] ordered = [];
            var success = await SettingsWriter.UpdateAsync(settings, s =>
            {
                var rules = change(s.Rules.OrderByDescending(r => r.Priority).ToList());
                ordered = rules.Select((r, index) => r with { Priority = rules.Count - index }).ToArray();
                return s with { Rules = ordered };
            });
            HasError = !success;
            // Réutiliser les cartes existantes conserve le focus lors d'un changement d'état.
            if (success)
            {
                for (var i = 0; i < ordered.Length; i++)
                {
                    var item = Rules.FirstOrDefault(r => r.Rule.Id == ordered[i].Id);
                    if (item is null) { item = new(ordered[i], this); Rules.Insert(i, item); }
                    else
                    {
                        item.Update(ordered[i]);
                        var old = Rules.IndexOf(item);
                        if (old != i) Rules.Move(old, i);
                    }
                }
                while (Rules.Count > ordered.Length) Rules.RemoveAt(Rules.Count - 1);
                UpdatePositions();
            }
            else foreach (var item in Rules) item.Update(item.Rule);
            return success;
        }
        finally { IsBusy = false; saveGate.Release(); }
    }
}

public sealed partial class RuleCardViewModel : ObservableObject
{
    private readonly RulesPageViewModel owner;
    private bool syncing;

    public RuleCardViewModel(Rule rule, RulesPageViewModel owner)
    {
        this.owner = owner;
        Rule = rule;
        Update(rule);
    }

    public Rule Rule { get; private set; }
    public string Name => Rule.Name;
    public string ProcessName => Rule.ProcessName;
    public string TriggerText => Rule.Trigger == RuleTrigger.ProcessForeground ? Strings.Get("CS_061") : Strings.Get("CS_140");
    public string ActionText => Rule.Action == RuleActionKind.SwitchProfile
        ? Strings.Format("CS_RuleProfile", owner.Mixer.Profiles.FirstOrDefault(p => p.Id == Rule.ProfileId)?.Name ?? Strings.Get("CS_065"))
        : Rule.MicMuted == true ? Strings.Get("Core_MicMuted") : Strings.Get("Core_MicOn");
    public string Sentence => Strings.Format("CS_RuleSentence", ProcessName, TriggerText, ActionText);

    [ObservableProperty]
    public partial bool Enabled { get; set; }
    [ObservableProperty]
    public partial bool RevertWhenInactive { get; set; }
    [ObservableProperty]
    public partial bool CanMoveUp { get; set; }
    [ObservableProperty]
    public partial bool CanMoveDown { get; set; }
    [ObservableProperty]
    public partial string Position { get; set; } = string.Empty;

    partial void OnEnabledChanged(bool value) { if (!syncing) _ = owner.SetEnabledAsync(this, value); }
    partial void OnRevertWhenInactiveChanged(bool value) { if (!syncing) _ = owner.SetRevertAsync(this, value); }

    public void Update(Rule rule)
    {
        syncing = true;
        Rule = rule;
        Enabled = rule.Enabled;
        RevertWhenInactive = rule.RevertWhenInactive;
        syncing = false;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Sentence));
        OnPropertyChanged(nameof(ProcessName));
        OnPropertyChanged(nameof(TriggerText));
        OnPropertyChanged(nameof(ActionText));
    }
}

public sealed partial class RuleEditorViewModel : ObservableObject
{
    private readonly Rule original;

    public RuleEditorViewModel(Rule? rule, MixerViewModel mixer)
    {
        Mixer = mixer;
        original = rule ?? new Rule { Id = Rule.NewId(), Name = string.Empty, ProcessName = string.Empty };
        Name = original.Name;
        ProcessName = original.ProcessName;
        TriggerIndex = original.Trigger == RuleTrigger.ProcessForeground ? 1 : 0;
        ActionIndex = original.Action == RuleActionKind.SwitchProfile ? 0 : original.MicMuted == true ? 1 : 2;
        SelectedProfile = mixer.Profiles.FirstOrDefault(p => p.Id == original.ProfileId);
        RevertWhenInactive = original.RevertWhenInactive;
    }

    public MixerViewModel Mixer { get; }
    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial string ProcessName { get; set; } = string.Empty;
    [ObservableProperty]
    public partial int TriggerIndex { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfileAction), nameof(CanSave))]
    public partial int ActionIndex { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial ProfileItemViewModel? SelectedProfile { get; set; }
    [ObservableProperty]
    public partial bool RevertWhenInactive { get; set; }
    [ObservableProperty]
    public partial bool HasError { get; set; }

    public bool IsProfileAction => ActionIndex == 0;
    public bool CanSave => !string.IsNullOrWhiteSpace(ProcessName) && (!IsProfileAction || SelectedProfile is not null);

    public Rule Build()
    {
        var process = ProcessName.Trim();
        if (!process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) process += ".exe";
        return original with
        {
            Name = string.IsNullOrWhiteSpace(Name) ? process : Name.Trim(),
            ProcessName = process,
            Trigger = TriggerIndex == 1 ? RuleTrigger.ProcessForeground : RuleTrigger.ProcessRunning,
            Action = IsProfileAction ? RuleActionKind.SwitchProfile : RuleActionKind.SetMicMuted,
            ProfileId = IsProfileAction ? SelectedProfile?.Id : null,
            MicMuted = IsProfileAction ? null : ActionIndex == 1,
            RevertWhenInactive = RevertWhenInactive,
        };
    }
}
