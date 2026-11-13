using Mixdeck.Core.Model;

namespace Mixdeck.Core.Rules;

/// <summary>Champs de l'éditeur de règle, indépendants de l'interface.</summary>
public static class RuleDraft
{
    public static Rule Build(Rule original, string name, string processName, int triggerIndex, int actionIndex,
        string? profileId, int busIndex, float gainDb, int stripIndex, string? endpointId, bool revert, bool appendExe)
    {
        var process = processName.Trim();
        if (appendExe && process.Length > 0 && !process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            process += ".exe";
        var action = actionIndex switch
        {
            1 or 2 => RuleActionKind.SetMicMuted,
            3 => RuleActionKind.SetBusGain,
            4 => RuleActionKind.SetStripMute,
            5 => RuleActionKind.RouteApp,
            _ => RuleActionKind.SwitchProfile,
        };
        return original with
        {
            Name = string.IsNullOrWhiteSpace(name) ? process : name.Trim(),
            ProcessName = process,
            Trigger = triggerIndex == 1 ? RuleTrigger.ProcessForeground : RuleTrigger.ProcessRunning,
            Action = action,
            ProfileId = action == RuleActionKind.SwitchProfile ? profileId : null,
            MicMuted = action switch
            {
                RuleActionKind.SetMicMuted => actionIndex == 1,
                RuleActionKind.SetStripMute => true,
                _ => null,
            },
            BusIndex = busIndex,
            GainDb = gainDb,
            StripIndex = stripIndex,
            OutputEndpointId = string.IsNullOrWhiteSpace(endpointId) ? null : endpointId.Trim(),
            RevertWhenInactive = revert,
        };
    }

    public static int ActionIndex(Rule rule) => rule.Action switch
    {
        RuleActionKind.SetMicMuted => rule.MicMuted == false ? 2 : 1,
        RuleActionKind.SetBusGain => 3,
        RuleActionKind.SetStripMute => 4,
        RuleActionKind.RouteApp => 5,
        _ => 0,
    };
}
