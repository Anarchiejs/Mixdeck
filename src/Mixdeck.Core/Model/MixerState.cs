namespace Mixdeck.Core.Model;

/// <summary>État d'une tranche d'entrée tel que lu dans Voicemeeter.</summary>
/// <param name="Routes">Routages dans l'ordre des bus de la disposition.</param>
/// <param name="EqGains">Tranches virtuelles : 3 gains en dB (graves, médiums, aigus) ; tranches physiques : vide.</param>
public sealed record StripState(
    int Index,
    string Label,
    float Gain,
    bool Mute,
    bool Solo,
    float PanX,
    IReadOnlyList<bool> Routes,
    IReadOnlyList<float> EqGains)
{
    public MixerLayout Layout { get; init; } = MixerLayout.Banana;
    public bool IsVirtual => Layout.IsVirtualStrip(Index);

    public static StripState Default(int index) => Default(index, MixerLayout.Banana);
    public static StripState Default(int index, MixerLayout layout) => new(
        index, string.Empty, 0f, false, false, 0f,
        Array.AsReadOnly(Enumerable.Range(0, layout.BusCount).Select(bus => index == 0 && bus == 0).ToArray()),
        layout.IsVirtualStrip(index) ? Array.AsReadOnly(new float[MixerLayout.VirtualStripEqBands]) : []) { Layout = layout };
}

/// <summary>État d'un bus de sortie tel que lu dans Voicemeeter.</summary>
public sealed record BusState(int Index, string Label, float Gain, bool Mute, bool Mono, bool EqOn)
{
    public MixerLayout Layout { get; init; } = MixerLayout.Banana;
    public bool IsVirtual => Layout.IsVirtualBus(Index);

    public string Name => Layout.BusNames[Index];

    public static BusState Default(int index) => new(index, string.Empty, 0f, false, false, false);
    public static BusState Default(int index, MixerLayout layout) => Default(index) with { Layout = layout };
}

/// <summary>Photo immuable de tout l'état du mixeur. Une seule source de vérité : Voicemeeter.</summary>
/// <param name="Revision">Incrémentée à chaque relecture qui a changé quelque chose.</param>
public sealed record MixerSnapshot(IReadOnlyList<StripState> Strips, IReadOnlyList<BusState> Buses, long Revision)
{
    public MixerLayout Layout { get; init; } = MixerLayout.Banana;
    public static MixerSnapshot Empty { get; } = EmptyFor(MixerLayout.Banana);
    public static MixerSnapshot EmptyFor(MixerLayout layout) => new(
        Array.AsReadOnly(Enumerable.Range(0, layout.StripCount).Select(i => StripState.Default(i, layout)).ToArray()),
        Array.AsReadOnly(Enumerable.Range(0, layout.BusCount).Select(i => BusState.Default(i, layout)).ToArray()),
        0) { Layout = layout };
}

/// <summary>Crête instantanée d'un canal stéréo, en amplitude linéaire (1.0 = 0 dBFS).</summary>
public readonly record struct StereoLevel(float Left, float Right)
{
    public float Max => MathF.Max(Left, Right);
}

/// <summary>Niveaux de toutes les entrées (post-fader) et de tous les bus, lus ~60 fois par seconde.</summary>
public sealed record LevelFrame(IReadOnlyList<StereoLevel> Strips, IReadOnlyList<StereoLevel> Buses, long TimestampTicks)
{
    public MixerLayout Layout { get; init; } = MixerLayout.Banana;
    public static LevelFrame Silent { get; } = SilentFor(MixerLayout.Banana);
    public static LevelFrame SilentFor(MixerLayout layout) => new(
        Array.AsReadOnly(new StereoLevel[layout.StripCount]),
        Array.AsReadOnly(new StereoLevel[layout.BusCount]), 0) { Layout = layout };
}

public enum MixerConnectionState
{
    /// <summary>Pas encore démarré, ou arrêté.</summary>
    Disconnected,
    /// <summary>La DLL Voicemeeter est introuvable (Voicemeeter non installé).</summary>
    NotInstalled,
    /// <summary>Connecté à l'API mais le moteur Voicemeeter n'est pas lancé.</summary>
    VoicemeeterNotRunning,
    /// <summary>Voicemeeter lancé avec un type inconnu de Mixdeck.</summary>
    UnsupportedType,
    Connected,
}
