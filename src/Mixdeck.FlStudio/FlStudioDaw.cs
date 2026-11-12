using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.FlStudio;

public sealed class FlStudioDaw : IDaw
{
    private readonly IMidiPorts _ports;
    private readonly ISettingsStore _settings;
    private readonly ILogger<FlStudioDaw> _logger;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly ConcurrentQueue<(long Session, byte[] Message)> _received = new();
    private readonly Queue<byte[]> _commands = new();
    private readonly Dictionary<int, byte[]> _volumes = [];
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private bool _disposed;
    private FlStudioSettings? _activeSettings;
    private IMidiInput? _input;
    private IMidiOutput? _output;
    private long _session;
    private int _state, _selected = -1;
    private IReadOnlyDictionary<int, DawTrack> _tracks = new ReadOnlyDictionary<int, DawTrack>(new Dictionary<int, DawTrack>());
    private HashSet<int>? _listing;
    private DateTimeOffset _nextConnect, _nextPing, _pongDeadline, _nextVolume;
    private bool _awaitingPong;
    private int _missedPings;

    public FlStudioDaw(IMidiPorts ports, ISettingsStore settings, ILogger<FlStudioDaw> logger, TimeProvider time)
    {
        _ports = ports; _settings = settings; _logger = logger; _time = time;
        _settings.Changed += SettingsChanged;
    }

    public DawConnectionState State => (DawConnectionState)Volatile.Read(ref _state);
    public IReadOnlyDictionary<int, DawTrack> Tracks => Volatile.Read(ref _tracks);
    public int SelectedTrack => Volatile.Read(ref _selected);
    public event EventHandler<DawConnectionState>? StateChanged;
    public event EventHandler<DawTrack>? TrackChanged;
    public event EventHandler<int>? SelectedTrackChanged;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Mixdeck FL Studio" };
                _thread.Start();
            }
        }
        return _started.Task.WaitAsync(cancellationToken);
    }

    private void SettingsChanged(object? sender, AppSettings settings) => Signal();
    private void Signal() { lock (_gate) { if (!_disposed) _wake.Set(); } }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Enqueue(FlProtocol.List());
        return Task.CompletedTask;
    }
    public void SelectTrack(int track) => Enqueue(FlProtocol.Select(track));
    public void SetMute(int track, bool muted) => Enqueue(FlProtocol.Mute(track, muted));
    public void SetSlotsEnabled(int track, bool enabled) => Enqueue(FlProtocol.SlotsEnabled(track, enabled));
    public void SetSlot(int track, int slot, bool enabled) => Enqueue(FlProtocol.Slot(track, slot, enabled));
    public void ChangePreset(int track, int slot, bool next) => Enqueue(FlProtocol.Preset(track, slot, next));
    public void SetVolume(int track, float volume)
    {
        var message = FlProtocol.Volume(track, volume);
        lock (_gate)
        {
            if (_disposed || State != DawConnectionState.Connected) return;
            _volumes[track] = message;
            _wake.Set();
        }
    }
    private void Enqueue(byte[] message)
    {
        lock (_gate)
        {
            if (_disposed || State != DawConnectionState.Connected) return;
            _commands.Enqueue(message);
            _wake.Set();
        }
    }

    private void Run()
    {
        try
        {
            while (true)
            {
                lock (_gate) { if (_disposed) break; }
                try { Tick(); }
                catch (Exception e) when (e is MidiPortException or IOException)
                {
                    _logger.LogWarning(e, "Liaison MIDI interrompue.");
                    Disconnect(DawConnectionState.PortsMissing);
                    _nextConnect = _time.GetUtcNow().AddSeconds(2);
                }
                catch (Exception e)
                {
                    // Un défaut de l'intégration FL ne doit jamais faire tomber Mixdeck : on coupe et on réessaie plus tard.
                    _logger.LogError(e, "Erreur inattendue de l'intégration FL Studio.");
                    Disconnect(DawConnectionState.PortsMissing);
                    _nextConnect = _time.GetUtcNow().AddSeconds(10);
                }
                _started.TrySetResult();
                // Réveillé par chaque message ou commande ; sinon 4 fois par seconde pour les échéances (ping, reconnexion) :
                // au repos, FL fermé ou intégration désactivée, le thread ne coûte rien.
                bool volumesPending;
                lock (_gate) volumesPending = _volumes.Count > 0;
                _wake.WaitOne(volumesPending ? 34 : 250);
            }
        }
        finally
        {
            try { Disconnect(DawConnectionState.Disabled); }
            finally
            {
                lock (_gate) { _wake.Dispose(); }
                _started.TrySetResult();
                _stopped.TrySetResult();
            }
        }
    }

    private void Tick()
    {
        var now = _time.GetUtcNow();
        var config = _settings.Current.FlStudio;
        var supported = _ports is not IMidiAvailability availability || availability.IsSupported;
        if (_activeSettings is null || config.Enabled != _activeSettings.Enabled ||
            config.InputPort != _activeSettings.InputPort || config.OutputPort != _activeSettings.OutputPort)
        {
            Disconnect(!config.Enabled ? DawConnectionState.Disabled
                : supported ? DawConnectionState.WaitingForDaw
                : DawConnectionState.PortsMissing);
            _activeSettings = config;
            _nextConnect = now;
        }
        if (!config.Enabled || !supported) return;
        if (_output is null && now >= _nextConnect)
        {
            _nextConnect = now.AddSeconds(2);
            var session = ++_session;
            _input = _ports.OpenInput(config.InputPort, data =>
            {
                _received.Enqueue((session, data.ToArray()));
                Signal();
            });
            _output = _ports.OpenOutput(config.OutputPort);
            Ping(now);
        }
        while (_received.TryDequeue(out var received))
            if (received.Session == _session) Receive(FlProtocol.Decode(received.Message), now);

        if (_awaitingPong && now >= _pongDeadline)
        {
            _awaitingPong = false;
            if (State != DawConnectionState.Connected || ++_missedPings >= 2)
            {
                var wasConnected = State == DawConnectionState.Connected;
                Disconnect(DawConnectionState.WaitingForDaw);
                if (wasConnected) _nextConnect = now.AddSeconds(2);
                return;
            }
        }
        if (State != DawConnectionState.Connected) return;
        if (!_awaitingPong && now >= _nextPing) Ping(now);

        // Toutes les écritures et notifications passent par ce thread, hors verrou.
        byte[][] commands;
        byte[]? volume = null;
        lock (_gate)
        {
            commands = _commands.ToArray();
            _commands.Clear();
            if (now >= _nextVolume && _volumes.Count > 0)
            {
                var first = _volumes.First();
                volume = first.Value;
                _volumes.Remove(first.Key);
                _nextVolume = now.AddMilliseconds(34);
            }
        }
        foreach (var command in commands)
        {
            if (command[4] == FlProtocol.CmdList)
            {
                if (_listing is not null) continue;
                _listing = [];
            }
            _output!.SendSysex(command);
        }
        if (volume is not null) _output!.SendSysex(volume);
    }

    private void Ping(DateTimeOffset now)
    {
        _awaitingPong = true;
        _pongDeadline = now.AddSeconds(1);
        _nextPing = now.AddSeconds(5);
        _output!.SendSysex(FlProtocol.Ping());
    }

    private void Receive(FlMessage? message, DateTimeOffset now)
    {
        if (message is FlPong && _awaitingPong)
        {
            _awaitingPong = false;
            _missedPings = 0;
            _nextPing = now.AddSeconds(5);
            if (State != DawConnectionState.Connected)
            {
                SetState(DawConnectionState.Connected);
                _listing = [];
                _output!.SendSysex(FlProtocol.List());
            }
            return;
        }
        if (message is FlError error)
        {
            _logger.LogWarning("Erreur FL Studio : commande {Command}, code {Code}.", error.Command, error.Code);
            return;
        }
        if (State != DawConnectionState.Connected) return;
        switch (message)
        {
            case FlTrackState state:
                var track = state.Track with { Slots = Array.AsReadOnly(state.Track.Slots.ToArray()) };
                var tracks = Tracks.ToDictionary();
                tracks[track.Index] = track;
                Publish(tracks);
                _listing?.Add(track.Index);
                Notify(TrackChanged, track);
                break;
            case FlSelectedTrack selected:
                if (Interlocked.Exchange(ref _selected, selected.Track) != selected.Track)
                    Notify(SelectedTrackChanged, selected.Track);
                break;
            case FlListBegin:
                _listing = [];
                break;
            case FlListEnd when _listing is not null:
                Publish(Tracks.Where(p => _listing.Contains(p.Key)).ToDictionary());
                _listing = null;
                break;
        }
    }

    private void Publish(Dictionary<int, DawTrack> tracks) =>
        Volatile.Write(ref _tracks, new ReadOnlyDictionary<int, DawTrack>(tracks));
    private void SetState(DawConnectionState state)
    {
        if (Interlocked.Exchange(ref _state, (int)state) != (int)state) Notify(StateChanged, state);
    }
    private void Notify<T>(EventHandler<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
            try { handler(this, value); }
            catch (Exception e) { _logger.LogWarning(e, "Abonné FL Studio en échec."); }
    }
    private void Disconnect(DawConnectionState state)
    {
        ClosePorts();
        lock (_gate) { _commands.Clear(); _volumes.Clear(); }
        Publish([]);
        _listing = null;
        if (Interlocked.Exchange(ref _selected, -1) != -1) Notify(SelectedTrackChanged, -1);
        SetState(state);
    }
    private void ClosePorts()
    {
        ++_session;
        _awaitingPong = false;
        _missedPings = 0;
        try { _input?.Dispose(); }
        finally { _input = null; _output?.Dispose(); _output = null; }
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                _settings.Changed -= SettingsChanged;
                if (_thread is null) { _wake.Dispose(); _stopped.TrySetResult(); }
                else _wake.Set();
            }
        }
        return new ValueTask(_stopped.Task);
    }
}
