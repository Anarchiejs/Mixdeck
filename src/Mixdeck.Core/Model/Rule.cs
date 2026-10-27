namespace Mixdeck.Core.Model;

public enum RuleTrigger
{
    /// <summary>Active tant qu'un processus portant ce nom tourne.</summary>
    ProcessRunning,
    /// <summary>Active tant que la fenêtre au premier plan appartient à ce processus.</summary>
    ProcessForeground,
}

public enum RuleActionKind
{
    /// <summary>Passer au profil <see cref="Rule.ProfileId"/>.</summary>
    SwitchProfile,
    /// <summary>Couper (true) ou activer (false) le micro : <see cref="Rule.MicMuted"/>.</summary>
    SetMicMuted,
}

/// <summary>« Quand &lt;processus&gt; &lt;démarre | est au premier plan&gt; → &lt;action&gt; ».</summary>
public sealed record Rule
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;

    /// <summary>La règle active de plus haute priorité gagne ; à égalité, la plus récemment activée.</summary>
    public int Priority { get; init; }

    public RuleTrigger Trigger { get; init; } = RuleTrigger.ProcessRunning;

    /// <summary>Nom de l'exécutable, ex. "FL64.exe" (comparaison insensible à la casse, extension facultative).</summary>
    public required string ProcessName { get; init; }

    public RuleActionKind Action { get; init; } = RuleActionKind.SwitchProfile;
    public string? ProfileId { get; init; }
    public bool? MicMuted { get; init; }

    /// <summary>Quand la règle devient inactive, revenir à l'état d'avant.</summary>
    public bool RevertWhenInactive { get; init; } = true;

    public static string NewId() => Guid.NewGuid().ToString("N");
}
