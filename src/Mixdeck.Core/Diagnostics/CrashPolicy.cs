namespace Mixdeck.Core.Diagnostics;

/// <summary>Règles du journal des plantages : relance unique, fichiers conservés. Sans effet de bord, donc testable.</summary>
public static class CrashPolicy
{
    /// <summary>Deux plantages plus rapprochés que ça : on ne relance plus (pas de boucle de plantages).</summary>
    public static readonly TimeSpan RestartCooldown = TimeSpan.FromMinutes(5);

    public const int KeptReports = 10;

    public const string ReportPrefix = "crash-";

    /// <summary>Relancer Mixdeck après un plantage, sauf si le précédent date de moins de <see cref="RestartCooldown"/>.</summary>
    public static bool ShouldRestart(DateTimeOffset now, DateTimeOffset? previousCrash) =>
        previousCrash is not { } previous || now - previous >= RestartCooldown || previous > now;

    /// <summary>Nom du rapport, triable par ordre chronologique.</summary>
    public static string ReportFileName(DateTimeOffset now) => $"{ReportPrefix}{now.UtcDateTime:yyyyMMdd-HHmmss-fff}.txt";

    /// <summary>Rapports à supprimer pour n'en garder que les <see cref="KeptReports"/> plus récents.</summary>
    public static IReadOnlyList<string> ReportsToDelete(IEnumerable<string> reportPaths) =>
        [.. reportPaths
            .Where(p => Path.GetFileName(p).StartsWith(ReportPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .Skip(KeptReports)];
}
