using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.Desktop;

/// <summary>Voicemeeter n'existe pas sur ce système. Les écritures sont ignorées, rien n'est inventé.</summary>
public sealed class UnavailableMixer : IMixer
{
    public MixerConnectionState State => MixerConnectionState.NotInstalled;
    public MixerLayout Layout => MixerLayout.Banana;
    public MixerSnapshot Snapshot => MixerSnapshot.Empty;
    public LevelFrame Levels => LevelFrame.Silent;
    public bool LevelsEnabled { get; set; }
    public IDisposable RequestLevels() => Empty.Instance;
    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(AudioDeviceDirection direction, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<AudioDevice>>([]);
    public Task<string?> GetStringAsync(string parameter, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    public event EventHandler<MixerConnectionState>? StateChanged { add { } remove { } }
    public event EventHandler<MixerSnapshot>? SnapshotChanged { add { } remove { } }
    public event EventHandler<LevelFrame>? LevelsUpdated { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SetFloat(string parameter, float value) { }
    public void SetString(string parameter, string value) { }
    public void ApplyScript(string script) { }
    public Task<bool> LaunchVoicemeeterAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class Empty : IDisposable
    {
        public static Empty Instance { get; } = new();
        public void Dispose() { }
    }
}
