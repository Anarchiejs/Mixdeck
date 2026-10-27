using Mixdeck.Core.Model;
namespace Mixdeck.Core.Rules;
public sealed record RuleDecision(string? ProfileId, bool? MicMuted, Rule? ProfileCause, Rule? MicCause);
public sealed class RuleEngine
{
    private Rule[] rules = [];
    private readonly Dictionary<string, int> processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> activations = new();
    private long sequence;
    private string? foreground;
    private string? baselineProfile, lastProfile;
    private bool baselineMic, lastMic;
    private Rule? profileOwner, micOwner;
    public void SetRules(IEnumerable<Rule> value) => rules = value.ToArray();
    public void Initialize(IEnumerable<string> runningProcesses, string? foregroundProcess)
    {
        processes.Clear();
        activations.Clear();
        foreach (var name in runningProcesses) Add(name);
        foreground = Normalize(foregroundProcess);
    }
    public void SetBaselineProfile(string? profileId) { baselineProfile = profileId; lastProfile = profileId; }
    public void SetBaselineMicMuted(bool muted) { baselineMic = muted; lastMic = muted; }
    private static string Normalize(string? exe)
    {
        var name = (exe ?? "").Replace('\\', '/').Split('/')[^1];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
    private void Add(string exe)
    {
        var name = Normalize(exe);
        processes[name] = processes.GetValueOrDefault(name) + 1;
    }
    public RuleDecision OnProcessStarted(string exe) { Add(exe); return Evaluate(); }
    public RuleDecision OnProcessExited(string exe)
    {
        var name = Normalize(exe);
        if (processes.TryGetValue(name, out var count))
        {
            if (count <= 1) processes.Remove(name); else processes[name] = count - 1;
        }
        return Evaluate();
    }
    public RuleDecision OnForegroundChanged(string? exe) { foreground = Normalize(exe); return Evaluate(); }
    public RuleDecision Evaluate()
    {
        var active = rules.Where(r => r.Enabled && Normalize(r.ProcessName).Length > 0 &&
            (r.Trigger == RuleTrigger.ProcessRunning ? processes.ContainsKey(Normalize(r.ProcessName)) :
                string.Equals(foreground, Normalize(r.ProcessName), StringComparison.OrdinalIgnoreCase))).ToArray();
        var ids = active.Select(r => r.Id).ToHashSet();
        foreach (var id in activations.Keys.Where(id => !ids.Contains(id)).ToArray()) activations.Remove(id);
        foreach (var r in active) if (!activations.ContainsKey(r.Id)) activations[r.Id] = ++sequence;
        var ordered = active.OrderByDescending(r => r.Priority).ThenByDescending(r => activations[r.Id]).ToArray();
        var profile = ordered.FirstOrDefault(r => r.Action == RuleActionKind.SwitchProfile && r.ProfileId is not null);
        var mic = ordered.FirstOrDefault(r => r.Action == RuleActionKind.SetMicMuted && r.MicMuted.HasValue);
        var desiredProfile = profile?.ProfileId ?? (profileOwner?.RevertWhenInactive == true ? baselineProfile : lastProfile);
        var desiredMic = mic?.MicMuted ?? (micOwner?.RevertWhenInactive == true ? baselineMic : lastMic);
        var changeProfile = desiredProfile != lastProfile ? desiredProfile : null;
        bool? changeMic = desiredMic != lastMic ? desiredMic : null;
        var profileCause = profile ?? profileOwner;
        var micCause = mic ?? micOwner;
        lastProfile = desiredProfile;
        lastMic = desiredMic;
        profileOwner = profile;
        micOwner = mic;
        return new(changeProfile, changeMic, changeProfile is null ? null : profileCause, changeMic is null ? null : micCause);
    }
}
