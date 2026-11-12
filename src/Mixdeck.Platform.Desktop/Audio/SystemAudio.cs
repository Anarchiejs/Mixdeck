using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Audio;

public enum SystemAudioFlow
{
    Output,
    Input,
}

public sealed record SystemAudioDevice(string Id, string Name, SystemAudioFlow Flow, float Volume, bool Mute);

public sealed record SystemAudioSnapshot(
    IReadOnlyList<SystemAudioDevice> Outputs,
    IReadOnlyList<SystemAudioDevice> Inputs,
    string? DefaultOutputId,
    string? DefaultInputId)
{
    public static SystemAudioSnapshot Empty { get; } = new([], [], null, null);
}

/// <summary>
/// Périphériques, volume et sessions du système sur macOS (Core Audio).
/// Quand l'API n'existe pas, <see cref="IsAvailable"/> est faux et les écritures échouent.
/// </summary>
public interface ISystemAudio : IDisposable
{
    bool IsAvailable { get; }

    string? UnavailableReason { get; }

    bool CanControlAppVolume { get; }

    bool CanRouteApps { get; }

    event EventHandler? Changed;

    SystemAudioSnapshot Snapshot { get; }

    IReadOnlyList<AppAudioSession> Sessions { get; }

    void Start();

    Task RefreshAsync(CancellationToken cancellationToken = default);

    Task<bool> SetDefaultAsync(SystemAudioFlow flow, string id, CancellationToken cancellationToken = default);

    Task<bool> SetDeviceVolumeAsync(SystemAudioFlow flow, string id, float volume, CancellationToken cancellationToken = default);

    Task<bool> SetDeviceMuteAsync(SystemAudioFlow flow, string id, bool mute, CancellationToken cancellationToken = default);

    Task<bool> SetSessionVolumeAsync(string sessionId, float volume, CancellationToken cancellationToken = default);

    Task<bool> SetSessionMuteAsync(string sessionId, bool mute, CancellationToken cancellationToken = default);

    /// <summary>Envoie les flux de ce processus vers une sortie. null = la sortie par défaut.</summary>
    Task<bool> MoveProcessAsync(int processId, string? outputId, CancellationToken cancellationToken = default);
}

public sealed class UnavailableSystemAudio(string reason) : ISystemAudio
{
    public bool IsAvailable => false;
    public string? UnavailableReason { get; } = reason;
    public bool CanControlAppVolume => false;
    public bool CanRouteApps => false;
    public event EventHandler? Changed { add { } remove { } }
    public SystemAudioSnapshot Snapshot => SystemAudioSnapshot.Empty;
    public IReadOnlyList<AppAudioSession> Sessions { get; } = [];
    public void Start() { }
    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<bool> SetDefaultAsync(SystemAudioFlow flow, string id, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> SetDeviceVolumeAsync(SystemAudioFlow flow, string id, float volume, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> SetDeviceMuteAsync(SystemAudioFlow flow, string id, bool mute, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> SetSessionVolumeAsync(string sessionId, float volume, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> SetSessionMuteAsync(string sessionId, bool mute, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> MoveProcessAsync(int processId, string? outputId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public void Dispose() { }
}
