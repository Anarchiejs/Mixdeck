namespace Mixdeck.Core.Model;

/// <summary>État d'une tranche d'entrée tel que lu dans Voicemeeter.</summary>
/// <param name="Routes">5 booléens, dans l'ordre A1, A2, A3, B1, B2.</param>
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
    public bool IsVirtual => BananaLayout.IsVirtualStrip(Index);

    public static StripState Default(int index) => new(
        index, string.Empty, 0f, false, false, 0f,
        [index == 0, false, false, false, false],
        BananaLayout.IsVirtualStrip(index) ? [0f, 0f, 0f] : []);
}

/// <summary>État d'un bus de sortie tel que lu dans Voicemeeter.</summary>
public sealed record BusState(int Index, string Label, float Gain, bool Mute, bool Mono, bool EqOn)
{
    public bool IsVirtual => BananaLayout.IsVirtualBus(Index);

    public string Name => BananaLayout.BusNames[Index];

    public static BusState Default(int index) => new(index, string.Empty, 0f, false, false, false);
}

/// <summary>Photo immuable de tout l'état du mixeur. Une seule source de vérité : Voicemeeter.</summary>
/// <param name="Revision">Incrémentée à chaque relecture qui a changé quelque chose.</param>
public sealed record MixerSnapshot(IReadOnlyList<StripState> Strips, IReadOnlyList<BusState> Buses, long Revision)
{
    public static MixerSnapshot Empty { get; } = new(
        [.. Enumerable.Range(0, BananaLayout.StripCount).Select(StripState.Default)],
        [.. Enumerable.Range(0, BananaLayout.BusCount).Select(BusState.Default)],
        0);
}

/// <summary>Crête instantanée d'un canal stéréo, en amplitude linéaire (1.0 = 0 dBFS).</summary>
public readonly record struct StereoLevel(float Left, float Right)
{
    public float Max => MathF.Max(Left, Right);
}

/// <summary>Niveaux de toutes les entrées (post-fader) et de tous les bus, lus ~60 fois par seconde.</summary>
public sealed record LevelFrame(IReadOnlyList<StereoLevel> Strips, IReadOnlyList<StereoLevel> Buses, long TimestampTicks)
{
    public static LevelFrame Silent { get; } = new(
        [.. Enumerable.Repeat(default(StereoLevel), BananaLayout.StripCount)],
        [.. Enumerable.Repeat(default(StereoLevel), BananaLayout.BusCount)],
        0);
}

public enum MixerConnectionState
{
    /// <summary>Pas encore démarré, ou arrêté.</summary>
    Disconnected,
    /// <summary>La DLL Voicemeeter est introuvable (Voicemeeter non installé).</summary>
    NotInstalled,
    /// <summary>Connecté à l'API mais le moteur Voicemeeter n'est pas lancé.</summary>
    VoicemeeterNotRunning,
    /// <summary>Voicemeeter lancé mais ce n'est pas Banana (Standard ou Potato).</summary>
    UnsupportedType,
    Connected,
}
