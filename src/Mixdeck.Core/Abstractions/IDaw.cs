using Mixdeck.Core.Model;

namespace Mixdeck.Core.Abstractions;

/// <summary>
/// Pilotage du mixeur de FL Studio par le script device_Mixdeck.py. Les commandes sont mises en file et ne bloquent
/// jamais ; l'état revient de FL de façon asynchrone (<see cref="TrackChanged"/>). Les événements sont levés sur un
/// thread de fond : l'interface repasse sur son propre thread avant de toucher aux contrôles.
/// </summary>
public interface IDaw : IAsyncDisposable
{
    DawConnectionState State { get; }

    /// <summary>Pistes utilisées (Master, pistes renommées ou avec effets) et toute piste demandée ensuite, par index.</summary>
    IReadOnlyDictionary<int, DawTrack> Tracks { get; }

    /// <summary>Piste sélectionnée dans le mixeur de FL ; -1 inconnue.</summary>
    int SelectedTrack { get; }

    event EventHandler<DawConnectionState>? StateChanged;

    /// <summary>Une piste a changé (depuis Mixdeck ou depuis FL), ou la liste a été relue.</summary>
    event EventHandler<DawTrack>? TrackChanged;

    event EventHandler<int>? SelectedTrackChanged;

    /// <summary>Démarre la recherche des ports et la reconnexion automatique (selon les réglages).</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Relit la liste des pistes utilisées ; sans effet hors connexion.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    void SelectTrack(int track);

    void SetMute(int track, bool muted);

    /// <param name="volume">0 à 1 ; 0,8 = 0 dB.</param>
    void SetVolume(int track, float volume);

    void SetSlotsEnabled(int track, bool enabled);

    void SetSlot(int track, int slot, bool enabled);

    void ChangePreset(int track, int slot, bool next);
}
