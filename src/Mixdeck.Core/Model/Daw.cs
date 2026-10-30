namespace Mixdeck.Core.Model;

/// <summary>État de la liaison avec le logiciel de musique (FL Studio).</summary>
public enum DawConnectionState
{
    /// <summary>Intégration désactivée dans les réglages.</summary>
    Disabled,
    /// <summary>Ports MIDI virtuels introuvables (loopMIDI absent ou ports non créés).</summary>
    PortsMissing,
    /// <summary>Ports présents, mais le script ne répond pas (FL fermé ou script non activé).</summary>
    WaitingForDaw,
    Connected,
}

/// <summary>Un slot d'effet occupé d'une piste du mixeur.</summary>
/// <param name="Index">0 à 9.</param>
/// <param name="Enabled">Faux = slot coupé (le plugin ne traite pas le son).</param>
/// <param name="Name">Nom du plugin affiché dans le slot ; vide si inconnu (ancien script).</param>
public sealed record DawSlot(int Index, bool Enabled, string Name = "");

/// <summary>État d'une piste du mixeur de FL Studio (0 = Master).</summary>
/// <param name="Volume">Position du fader de 0 à 1 ; 0,8 = 0 dB dans FL.</param>
/// <param name="SlotsEnabled">Interrupteur « effets de la piste » (tous les slots d'un coup).</param>
/// <param name="Slots">Slots occupés seulement, dans l'ordre.</param>
public sealed record DawTrack(int Index, string Name, float Volume, bool Muted, bool SlotsEnabled, IReadOnlyList<DawSlot> Slots);

/// <summary>Part FL Studio d'un profil : seuls les champs renseignés sont appliqués.</summary>
public sealed record DawTrackProfile(int Track, bool? Muted = null, float? Volume = null, bool? SlotsEnabled = null,
    IReadOnlyList<DawSlot>? Slots = null);

public sealed record FlStudioSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Port où Mixdeck écrit (entrée du script dans FL). Recherche par morceau du nom.</summary>
    public string OutputPort { get; init; } = "Mixdeck to FL";

    /// <summary>Port où Mixdeck lit (sortie du script dans FL).</summary>
    public string InputPort { get; init; } = "FL to Mixdeck";

    /// <summary>Inclure l'état de FL Studio quand on enregistre un profil.</summary>
    public bool CaptureInProfiles { get; init; } = true;
}
