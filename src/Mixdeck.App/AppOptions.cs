namespace Mixdeck.App;

/// <summary>Arguments de la ligne de commande.</summary>
/// <param name="Demo">--demo : faux moteur Voicemeeter, pour essayer Mixdeck sans Voicemeeter.</param>
/// <param name="Minimized">--minimized : démarrage avec Windows, rien n'est affiché.</param>
/// <param name="Gallery">--gallery : galerie des contrôles (développement).</param>
public sealed record AppOptions(bool Demo, bool Minimized, bool Gallery)
{
    public static AppOptions Parse(IEnumerable<string> args)
    {
        var set = args.Select(a => a.Trim().ToLowerInvariant()).ToHashSet();
        return new AppOptions(set.Contains("--demo"), set.Contains("--minimized"), set.Contains("--gallery"));
    }
}
