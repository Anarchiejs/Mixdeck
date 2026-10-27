using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Profiles;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Tests;
internal sealed class FakeMixer : IMixer
{
    public MixerConnectionState State { get; private set; } = MixerConnectionState.Connected;
    public MixerSnapshot Snapshot { get; set; } = MixerSnapshot.Empty;
    public LevelFrame Levels => LevelFrame.Silent;
    public bool LevelsEnabled { get; set; }
    public ConcurrentQueue<string> Scripts { get; } = new();
    public ConcurrentQueue<(string Parameter, float Value)> Floats { get; } = new();
    public event EventHandler<MixerConnectionState>? StateChanged;
    public event EventHandler<MixerSnapshot>? SnapshotChanged { add { } remove { } }
    public event EventHandler<LevelFrame>? LevelsUpdated { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void SetState(MixerConnectionState state) { State = state; StateChanged?.Invoke(this, state); }
    public void SetFloat(string parameter, float value)
    {
        if (State != MixerConnectionState.Connected) return; // comme Voicemeeter : écritures ignorées hors connexion
        Floats.Enqueue((parameter, value));
        var index = int.Parse(parameter.AsSpan(parameter.IndexOf('[') + 1, 1), CultureInfo.InvariantCulture);
        if (parameter.StartsWith("Strip", StringComparison.Ordinal))
            Snapshot = Snapshot with { Strips = Snapshot.Strips.Select(s => s.Index != index ? s : parameter.EndsWith("Gain", StringComparison.Ordinal) ? s with { Gain = value } : s with { Mute = value != 0 }).ToArray() };
        else Snapshot = Snapshot with { Buses = Snapshot.Buses.Select(b => b.Index != index ? b : parameter.EndsWith("Gain", StringComparison.Ordinal) ? b with { Gain = value } : b with { Mute = value != 0 }).ToArray() };
    }
    public void SetString(string parameter, string value) { }
    public void ApplyScript(string script)
    {
        if (State != MixerConnectionState.Connected) return;
        Scripts.Enqueue(script);
        foreach (var command in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = command.Split('=');
            if (parts[0].EndsWith("Gain", StringComparison.Ordinal) || parts[0].EndsWith("Mute", StringComparison.Ordinal))
                SetFloat(parts[0], float.Parse(parts[1], CultureInfo.InvariantCulture));
        }
    }
    public Task<bool> LaunchVoicemeeterAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
internal sealed class FakeSettings : ISettingsStore
{
    public AppSettings Current { get; private set; } = new() { FadeMilliseconds = 0 };
    public event EventHandler<AppSettings>? Changed;
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    { Current = settings; Changed?.Invoke(this, settings); return Task.CompletedTask; }
    public Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default) => SaveAsync(change(Current), cancellationToken);
}
internal sealed class FakeProcesses : IProcessWatcher
{
    public event EventHandler<ProcessEventArgs>? ProcessStarted;
    public event EventHandler<ProcessEventArgs>? ProcessExited;
    public IReadOnlyCollection<string> Running { get; set; } = [];
    public IReadOnlyCollection<string> GetRunningProcessNames() => Running;
    public void Start() { }
    public void StartProcess(string name) => ProcessStarted?.Invoke(this, new(name, 1));
    public void ExitProcess(string name) => ProcessExited?.Invoke(this, new(name, 1));
    public void Dispose() { }
}
internal sealed class FakeForeground : IForegroundWatcher
{
    public event EventHandler<string?>? ForegroundProcessChanged;
    public string? Current { get; private set; }
    public void Change(string? name) { Current = name; ForegroundProcessChanged?.Invoke(this, name); }
    public void Start() { }
    public void Dispose() { }
}
internal sealed class FakeOsd : IOsdService
{
    public ConcurrentQueue<(string Text, string Glyph, bool Warning)> Messages { get; } = new();
    public void Show(string text, string glyph, bool isWarning = false) => Messages.Enqueue((text, glyph, isWarning));
}
internal sealed class TestRig : IDisposable
{
    public MixdeckPaths Paths { get; } = new(Path.Combine(Path.GetTempPath(), "Mixdeck-L2-tests", Guid.NewGuid().ToString("N")));
    public FakeMixer Mixer { get; } = new();
    public FakeSettings Settings { get; } = new();
    public FakeTimeProvider Time { get; } = new();
    public ProfileService Profiles { get; }
    public TestRig() => Profiles = new(Mixer, Settings, Paths, NullLogger<ProfileService>.Instance, Time);
    public void Dispose() { if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, true); }
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
}
