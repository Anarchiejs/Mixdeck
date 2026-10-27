namespace Mixdeck.App;

/// <summary>Arguments de la ligne de commande.</summary>
/// <param name="Demo">--demo : faux moteur Voicemeeter, pour essayer Mixdeck sans Voicemeeter.</param>
/// <param name="Minimized">--minimized : démarrage avec Windows, rien n'est affiché.</param>
/// <param name="Gallery">--gallery : galerie des contrôles (développement).</param>
/// <param name="Flyout">--flyout : ouvre le panneau rapide au démarrage (développement, captures d'écran).</param>
/// <param name="Page">--page=&lt;Mixer|Routing|Profiles|Rules|Settings|About&gt; : ouvre la console sur cette page.</param>
public sealed record AppOptions(bool Demo, bool Minimized, bool Gallery, bool Flyout = false, string? Page = null)
{
    public static AppOptions Parse(IEnumerable<string> args)
    {
        var set = args.Select(a => a.Trim().ToLowerInvariant()).ToHashSet();
        var page = set.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.Ordinal))?["--page=".Length..];
        return new AppOptions(set.Contains("--demo"), set.Contains("--minimized"), set.Contains("--gallery"), set.Contains("--flyout"), page);
    }
}
