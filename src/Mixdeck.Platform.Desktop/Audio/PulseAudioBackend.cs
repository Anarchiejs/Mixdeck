using System.Globalization;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Audio;

/// <summary>
/// Client PipeWire / PulseAudio via <c>pactl</c>. Le binaire parle au serveur pulse de PipeWire
/// (<c>pipewire-pulse</c>) comme à PulseAudio. Sans <c>pactl</c> ou sans serveur, le backend reste indisponible.
/// </summary>
public sealed class PulseAudioBackend : ISystemAudio
{
    private readonly string? _pactl;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _call = new(1, 1);
    private CancellationTokenSource? _lifetime;
    private Task? _subscribe;
    private List<ParsedDevice> _sinks = [];
    private List<ParsedDevice> _sources = [];
    private List<ParsedSession> _sessions = [];
    private string? _defaultSink;
    private string? _defaultSource;
    private string? _reason;

    private PulseAudioBackend(string? pactl, string? reason)
    {
        _pactl = pactl;
        _reason = reason;
    }

    public static PulseAudioBackend Create()
    {
        var pactl = PactlProcess.Find();
        if (pactl is null)
            return new PulseAudioBackend(null, "pactl was not found. Install PulseAudio or pipewire-pulse.");
        return new PulseAudioBackend(pactl, null);
    }

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason => _reason;
    public bool CanControlAppVolume => IsAvailable;
    public bool CanRouteApps => IsAvailable;
    public event EventHandler? Changed;

    public SystemAudioSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new SystemAudioSnapshot(
                    _sinks.Select(device => ToDevice(device, SystemAudioFlow.Output)).ToArray(),
                    _sources.Select(device => ToDevice(device, SystemAudioFlow.Input)).ToArray(),
                    Resolve(_defaultSink, _sinks),
                    Resolve(_defaultSource, _sources));
            }
        }
    }

    public IReadOnlyList<AppAudioSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Select(session => new AppAudioSession(
                    session.Id,
                    session.ProcessId,
                    session.ProcessName,
                    session.DisplayName,
                    null,
                    session.Volume,
                    session.Mute,
                    session.ProcessId == 0 && session.ProcessName.Length == 0)).ToArray();
            }
        }
    }

    public void Start()
    {
        if (_pactl is null || _lifetime is not null) return;
        _lifetime = new CancellationTokenSource();
        var token = _lifetime.Token;
        _subscribe = Task.Run(() => SubscribeAsync(token), token);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_pactl is null) return;
        await _call.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await ReadAsync(cancellationToken).ConfigureAwait(false); }
        finally { _call.Release(); }
    }

    public Task<bool> SetDefaultAsync(SystemAudioFlow flow, string id, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, flow == SystemAudioFlow.Output ? "set-default-sink" : "set-default-source", id);

    public Task<bool> SetDeviceVolumeAsync(SystemAudioFlow flow, string id, float volume, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, flow == SystemAudioFlow.Output ? "set-sink-volume" : "set-source-volume", id, Percent(volume));

    public Task<bool> SetDeviceMuteAsync(SystemAudioFlow flow, string id, bool mute, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, flow == SystemAudioFlow.Output ? "set-sink-mute" : "set-source-mute", id, mute ? "1" : "0");

    public Task<bool> SetSessionVolumeAsync(string sessionId, float volume, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, "set-sink-input-volume", sessionId, Percent(volume));

    public Task<bool> SetSessionMuteAsync(string sessionId, bool mute, CancellationToken cancellationToken = default) =>
        RunAsync(cancellationToken, "set-sink-input-mute", sessionId, mute ? "1" : "0");

    public async Task<bool> MoveProcessAsync(int processId, string? outputId, CancellationToken cancellationToken = default)
    {
        if (_pactl is null || processId <= 0) return false;
        string[] indexes;
        string target;
        lock (_gate)
        {
            indexes = _sessions.Where(session => session.ProcessId == processId).Select(session => session.Id).ToArray();
            target = outputId ?? Resolve(_defaultSink, _sinks) ?? "";
        }
        if (indexes.Length == 0 || target.Length == 0) return false;
        var moved = true;
        foreach (var index in indexes)
            moved &= await RunAsync(cancellationToken, "move-sink-input", index, target).ConfigureAwait(false);
        return moved;
    }

    public void Dispose()
    {
        var lifetime = _lifetime;
        _lifetime = null;
        if (lifetime is null) return;
        lifetime.Cancel();
        try { _subscribe?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        lifetime.Dispose();
        _call.Dispose();
    }

    private async Task<bool> RunAsync(CancellationToken cancellationToken, params string[] args)
    {
        if (_pactl is null) return false;
        await _call.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await PactlProcess.RunAsync(_pactl, cancellationToken, args).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                NoteFailure(result.Stderr);
                return false;
            }
            await ReadAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            NoteFailure(exception.Message);
            return false;
        }
        finally { _call.Release(); }
    }

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        if (_pactl is null) return;
        var info = await PactlProcess.RunAsync(_pactl, cancellationToken, "info").ConfigureAwait(false);
        if (info.ExitCode != 0)
        {
            NoteFailure(string.IsNullOrWhiteSpace(info.Stderr) ? info.Stdout : info.Stderr);
            return;
        }
        var sinks = await ListAsync("sinks", cancellationToken).ConfigureAwait(false);
        var sources = await ListAsync("sources", cancellationToken).ConfigureAwait(false);
        var sessions = await SessionsAsync(cancellationToken).ConfigureAwait(false);
        var defaults = PactlParser.ParseDefaults(info.Stdout);
        lock (_gate)
        {
            _sinks = sinks.ToList();
            _sources = sources.ToList();
            _sessions = sessions.ToList();
            _defaultSink = defaults.Sink;
            _defaultSource = defaults.Source;
            IsAvailable = true;
            _reason = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<IReadOnlyList<ParsedDevice>> ListAsync(string kind, CancellationToken cancellationToken)
    {
        var json = await PactlProcess.RunAsync(_pactl!, cancellationToken, "-f", "json", "list", kind).ConfigureAwait(false);
        if (json.ExitCode == 0 && PactlParser.LooksLikeJson(json.Stdout))
            return PactlParser.ParseDevices(json.Stdout);
        var text = json.ExitCode == 0
            ? json
            : await PactlProcess.RunAsync(_pactl!, cancellationToken, "list", kind).ConfigureAwait(false);
        return text.ExitCode == 0 ? PactlParser.ParseDevices(text.Stdout) : [];
    }

    private async Task<IReadOnlyList<ParsedSession>> SessionsAsync(CancellationToken cancellationToken)
    {
        var json = await PactlProcess.RunAsync(_pactl!, cancellationToken, "-f", "json", "list", "sink-inputs").ConfigureAwait(false);
        if (json.ExitCode == 0 && json.Stdout.Trim().Length > 0)
            return PactlParser.ParseSessions(json.Stdout);
        var text = await PactlProcess.RunAsync(_pactl!, cancellationToken, "list", "sink-inputs").ConfigureAwait(false);
        return text.ExitCode == 0 ? PactlParser.ParseSessions(text.Stdout) : [];
    }

    private async Task SubscribeAsync(CancellationToken cancellationToken)
    {
        if (_pactl is null) return;
        try { await RefreshAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo(_pactl, "subscribe")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    },
                };
                if (!process.Start()) return;
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (line is null) break;
                    try { await RefreshAsync(cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    catch (IOException) { break; }
                }
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                NoteFailure(exception.Message);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void NoteFailure(string? detail)
    {
        var text = detail ?? "";
        _reason = text.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
            || text.Contains("pa_context_connect", StringComparison.OrdinalIgnoreCase)
            ? "The PulseAudio or PipeWire server is not running."
            : string.IsNullOrWhiteSpace(text) ? "pactl failed." : text.Trim();
        IsAvailable = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static SystemAudioDevice ToDevice(ParsedDevice device, SystemAudioFlow flow) =>
        new(device.Id, device.Name, flow, device.Volume, device.Mute);

    private static string? Resolve(string? id, List<ParsedDevice> devices)
    {
        if (string.IsNullOrWhiteSpace(id)) return devices.Count == 1 ? devices[0].Id : null;
        var byName = devices.FirstOrDefault(device => device.Id.Equals(id, StringComparison.Ordinal));
        if (byName.Id is not null) return byName.Id;
        return devices.FirstOrDefault(device => device.Index.ToString(CultureInfo.InvariantCulture) == id).Id ?? id;
    }

    private static string Percent(float volume)
    {
        var clamped = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0;
        return (clamped * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";
    }
}
