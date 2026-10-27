namespace Mixdeck.Core.Model;

/// <summary>
/// Compatibilité de l'outil Mixdeck.Probe, hors du périmètre de migration.
/// Le moteur et l'application utilisent leur MixerLayout courant.
/// </summary>
public static class BananaLayout
{
    public static int StripCount => MixerLayout.Banana.StripCount;
    public static int BusCount => MixerLayout.Banana.BusCount;
    public static IReadOnlyList<string> BusNames => MixerLayout.Banana.BusNames;
    public static IReadOnlyList<string> DefaultStripLabels => MixerLayout.Banana.DefaultStripLabels;
}
