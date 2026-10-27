using Mixdeck.Core.Model;

namespace Mixdeck.Core.Abstractions;

/// <summary>
/// Accès au moteur audio Voicemeeter. Toutes les écritures sont mises en file et appliquées sur un
/// thread dédié ; aucune méthode ne bloque l'appelant. Les événements sont levés sur ce thread dédié :
/// l'interface doit repasser sur son propre thread (DispatcherQueue) avant de toucher aux contrôles.
/// </summary>
public interface IMixer : IAsyncDisposable
{
    MixerConnectionState State { get; }

    /// <summary>Disposition détectée à la connexion. SnapshotChanged publie la nouvelle disposition.</summary>
    MixerLayout Layout { get; }

    /// <summary>Dernier état connu (relu dès que Voicemeeter signale un changement).</summary>
    MixerSnapshot Snapshot { get; }

    /// <summary>Derniers niveaux lus (~60 Hz).</summary>
    LevelFrame Levels { get; }

    /// <summary>
    /// Lecture des niveaux active ou non. L'interface l'active seulement quand un vumètre est visible
    /// (panneau ou console ouverts) : au repos, aucun VBVMR_GetLevel n'est appelé.
    /// </summary>
    bool LevelsEnabled { get; set; }

    /// <summary>
    /// Demande la lecture des niveaux tant que le jeton rendu n'est pas libéré. Les demandes se cumulent avec
    /// <see cref="LevelsEnabled"/> : la lecture s'arrête quand plus personne n'en a besoin (ex. l'atténuation
    /// automatique garde les niveaux pendant que le panneau s'ouvre et se ferme).
    /// </summary>
    IDisposable RequestLevels();

    /// <summary>
    /// Appareils audio connus de Voicemeeter (VBVMR_Input/Output_GetDeviceNumber/DescW), lus sur le thread dédié.
    /// Liste vide hors connexion.
    /// </summary>
    Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken cancellationToken = default);

    /// <summary>Lit un paramètre texte à la demande (ex. "Strip[0].device.name") ; null hors connexion ou si refusé.</summary>
    Task<string?> GetStringAsync(string parameter, CancellationToken cancellationToken = default);

    event EventHandler<MixerConnectionState>? StateChanged;

    /// <summary>Levé après une relecture qui a changé l'état (depuis Mixdeck ou depuis Voicemeeter).</summary>
    event EventHandler<MixerSnapshot>? SnapshotChanged;

    /// <summary>Levé ~60 fois par seconde quand connecté.</summary>
    event EventHandler<LevelFrame>? LevelsUpdated;

    /// <summary>Démarre le thread dédié ; se connecte et se reconnecte tout seul ensuite.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    void SetFloat(string parameter, float value);

    void SetString(string parameter, string value);

    /// <summary>Plusieurs réglages d'un coup, ex. "Strip[0].Mute=1; Strip[1].Gain=-6.0" (VBVMR_SetParameters).</summary>
    void ApplyScript(string script);

    /// <summary>Lance Voicemeeter Banana s'il ne tourne pas (VBVMR_RunVoicemeeter). true si la commande a été acceptée.</summary>
    Task<bool> LaunchVoicemeeterAsync(CancellationToken cancellationToken = default);
}
