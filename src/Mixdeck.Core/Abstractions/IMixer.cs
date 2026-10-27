using Mixdeck.Core.Model;

namespace Mixdeck.Core.Abstractions;

/// <summary>
/// Accès au moteur audio (Voicemeeter Banana). Toutes les écritures sont mises en file et appliquées sur un
/// thread dédié ; aucune méthode ne bloque l'appelant. Les événements sont levés sur ce thread dédié :
/// l'interface doit repasser sur son propre thread (DispatcherQueue) avant de toucher aux contrôles.
/// </summary>
public interface IMixer : IAsyncDisposable
{
    MixerConnectionState State { get; }

    /// <summary>Dernier état connu (relu dès que Voicemeeter signale un changement).</summary>
    MixerSnapshot Snapshot { get; }

    /// <summary>Derniers niveaux lus (~60 Hz).</summary>
    LevelFrame Levels { get; }

    /// <summary>
    /// Lecture des niveaux active ou non. L'interface l'active seulement quand un vumètre est visible
    /// (panneau ou console ouverts) : au repos, aucun VBVMR_GetLevel n'est appelé.
    /// </summary>
    bool LevelsEnabled { get; set; }

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
