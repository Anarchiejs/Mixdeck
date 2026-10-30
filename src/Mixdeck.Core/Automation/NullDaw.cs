using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.Core.Automation;

/// <summary>Aucun logiciel de musique : pour les tests et quand l'intégration n'est pas compilée.</summary>
public sealed class NullDaw : IDaw
{
    public static NullDaw Instance { get; } = new();

    public DawConnectionState State => DawConnectionState.Disabled;
    public IReadOnlyDictionary<int, DawTrack> Tracks { get; } = new Dictionary<int, DawTrack>();
    public int SelectedTrack => -1;

    public event EventHandler<DawConnectionState>? StateChanged { add { } remove { } }
    public event EventHandler<DawTrack>? TrackChanged { add { } remove { } }
    public event EventHandler<int>? SelectedTrackChanged { add { } remove { } }

    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SelectTrack(int track) { }
    public void SetMute(int track, bool muted) { }
    public void SetVolume(int track, float volume) { }
    public void SetSlotsEnabled(int track, bool enabled) { }
    public void SetSlot(int track, int slot, bool enabled) { }
    public void ChangePreset(int track, int slot, bool next) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
