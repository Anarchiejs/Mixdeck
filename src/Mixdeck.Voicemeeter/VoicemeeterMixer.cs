using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Voicemeeter.Native;

namespace Mixdeck.Voicemeeter;

public sealed class VoicemeeterMixer : IMixer
{
    private readonly IVoicemeeterApi _api;
    private readonly ILogger<VoicemeeterMixer> _logger;
    private readonly TimeProvider _time;
    private readonly VoicemeeterMixerOptions _options;
    private readonly LevelSampler _sampler;
    private readonly ConcurrentQueue<Command> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _lifecycle = new();
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private Task? _disposeTask;
    private int _disposed, _levelsEnabled, _state, _type, _version;
    private bool _loggedIn;
    private DateTimeOffset _nextConnect = DateTimeOffset.MinValue;
    private DateTimeOffset _nextDirty = DateTimeOffset.MinValue;
    private DateTimeOffset _nextLevel = DateTimeOffset.MinValue;
    private MixerSnapshot _snapshot = MixerSnapshot.Empty;
    private LevelFrame _levels = LevelFrame.Silent;

    public VoicemeeterMixer(IVoicemeeterApi api, ILogger<VoicemeeterMixer> logger, TimeProvider time,
        VoicemeeterMixerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(time);
        _api = api; _logger = logger; _time = time;
        _options = options ?? new();
        _options.Validate();
        _sampler = new(api, _options.LevelType);
    }

    public static VoicemeeterMixer CreateDefault(ILoggerFactory factory) =>
        new(new VoicemeeterApi(), factory.CreateLogger<VoicemeeterMixer>(), TimeProvider.System);
    public static VoicemeeterMixer CreateSimulated(ILoggerFactory factory) =>
        new(new SimulatedVoicemeeterApi(), factory.CreateLogger<VoicemeeterMixer>(), TimeProvider.System);

    public MixerConnectionState State => (MixerConnectionState)Volatile.Read(ref _state);
    public MixerSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public LevelFrame Levels => Volatile.Read(ref _levels);
    public int VoicemeeterType => Volatile.Read(ref _type);
    public int VoicemeeterVersion => Volatile.Read(ref _version);
    public bool LevelsEnabled
    {
        get => Volatile.Read(ref _levelsEnabled) != 0;
        set { Volatile.Write(ref _levelsEnabled, value ? 1 : 0); Signal(); }
    }
    public event EventHandler<MixerConnectionState>? StateChanged;
    public event EventHandler<MixerSnapshot>? SnapshotChanged;
    public event EventHandler<LevelFrame>? LevelsUpdated;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycle)
        {
            ObjectDisposedException.ThrowIf(_disposed != 0, this);
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Voicemeeter" };
                _thread.Start();
            }
        }
        return _started.Task.WaitAsync(cancellationToken);
    }

    public void SetFloat(string parameter, float value)
    {
        ValidateText(parameter, nameof(parameter));
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
        EnqueueWrite(new(CommandKind.Float, parameter, value, null, null));
    }
    public void SetString(string parameter, string value)
    {
        ValidateText(parameter, nameof(parameter));
        ValidateText(value, nameof(value), allowEmpty: true);
        EnqueueWrite(new(CommandKind.String, parameter, 0, value, null));
    }
    public void ApplyScript(string script)
    {
        ValidateText(script, nameof(script));
        EnqueueWrite(new(CommandKind.Script, "", 0, script, null));
    }
    private static void ValidateText(string text, string name, bool allowEmpty = false)
    {
        ArgumentNullException.ThrowIfNull(text, name);
        if ((!allowEmpty && string.IsNullOrWhiteSpace(text)) || text.Contains('\0'))
            throw new ArgumentException("Le texte est vide ou contient un caractère nul.", name);
    }
    private void EnqueueWrite(Command command)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (State != MixerConnectionState.Connected)
        {
            _logger.LogDebug("Commande ignorée hors connexion : {Parameter}", command.Parameter);
            return;
        }
        _commands.Enqueue(command);
        Signal();
    }
    public Task<bool> LaunchVoicemeeterAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Enqueue(new(CommandKind.Launch, "", 0, null, completion, cancellationToken));
        // Dispose peut avoir vidé la file entre le contrôle précédent et Enqueue.
        if (Volatile.Read(ref _disposed) != 0) completion.TrySetResult(false);
        Signal();
        return completion.Task.WaitAsync(cancellationToken);
    }
    private void Signal()
    {
        try { _wake.Set(); }
        catch (ObjectDisposedException) { /* Une écriture peut croiser la fin de DisposeAsync. */ }
    }

    private void Run()
    {
        try
        {
            while (Volatile.Read(ref _disposed) == 0)
            {
                Tick();
                _started.TrySetResult();
                if (Volatile.Read(ref _disposed) == 0) _wake.WaitOne(NextWait());
            }
        }
        finally
        {
            Logout();
            CancelPending();
            ChangeState(MixerConnectionState.Disconnected);
            _started.TrySetResult();
            _stopped.TrySetResult();
        }
    }

    // Les tests pilotent cette même boucle sans démarrer de thread.
    internal void Tick()
    {
        if (_thread is not null && Thread.CurrentThread != _thread)
            throw new InvalidOperationException("L'API est réservée au thread Voicemeeter.");
        if (Volatile.Read(ref _disposed) != 0) return;
        var now = _time.GetUtcNow();
        try
        {
            if (State != MixerConnectionState.Connected && now >= _nextConnect) Connect(now);
            DrainCommands();
            if (State != MixerConnectionState.Connected) return;
            if (now >= _nextDirty)
            {
                _nextDirty = now + _options.DirtyPollInterval;
                var dirty = _api.IsParametersDirty();
                if (dirty < 0) { ConnectionLost(now); return; }
                if (dirty == 1) PublishSnapshot(ReadSnapshot());
            }
            if (LevelsEnabled && now >= _nextLevel)
            {
                _nextLevel = now + _options.LevelInterval;
                var frame = _sampler.Sample(_time.GetTimestamp());
                Volatile.Write(ref _levels, frame);
                Raise(LevelsUpdated, frame);
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            _logger.LogWarning(exception, "API Remote indisponible.");
            Logout();
            _nextConnect = now + TimeSpan.FromSeconds(30);
            ChangeState(MixerConnectionState.NotInstalled);
            DropWrites();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Échec de communication avec Voicemeeter.");
            ConnectionLost(now);
            DropWrites();
        }
    }

    private void Connect(DateTimeOffset now)
    {
        Logout();
        Volatile.Write(ref _type, 0);
        Volatile.Write(ref _version, 0);
        _nextConnect = now + _options.ReconnectInterval;
        var login = _api.Login();
        _loggedIn = login is 0 or 1 or -2;
        if (login is not (0 or -2)) { ChangeState(MixerConnectionState.VoicemeeterNotRunning); return; }
        // L'API synchronise sa mémoire partagée lors du premier appel dirty après Login.
        var dirty = _api.IsParametersDirty();
        if (dirty < 0 || _api.GetVoicemeeterType(out var type) != 0)
        {
            ChangeState(MixerConnectionState.VoicemeeterNotRunning);
            return;
        }
        Volatile.Write(ref _type, type);
        if (_api.GetVoicemeeterVersion(out var version) == 0) Volatile.Write(ref _version, version);
        if (type != BananaLayout.VoicemeeterType) { ChangeState(MixerConnectionState.UnsupportedType); return; }
        var snapshot = ReadSnapshot();
        _nextDirty = now + _options.DirtyPollInterval;
        _nextLevel = now;
        ChangeState(MixerConnectionState.Connected);
        PublishSnapshot(snapshot);
    }

    private void ConnectionLost(DateTimeOffset now)
    {
        Logout();
        _nextConnect = now + _options.ReconnectInterval;
        ChangeState(MixerConnectionState.VoicemeeterNotRunning);
    }
    private void Logout()
    {
        if (!_loggedIn) return;
        _loggedIn = false;
        try { _api.Logout(); }
        catch (Exception exception) { _logger.LogDebug(exception, "Échec de déconnexion de l'API."); }
    }

    private void DrainCommands()
    {
        var pending = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);
        var count = _commands.Count;
        for (var i = 0; i < count && _commands.TryDequeue(out var command); i++)
        {
            if (command.Kind is CommandKind.Float or CommandKind.String)
            {
                pending[command.Parameter] = command;
                continue;
            }
            // Un script constitue une barrière : les écritures qui le suivent restent après lui.
            try { Flush(pending); }
            catch
            {
                command.Completion?.TrySetResult(false);
                throw;
            }
            if (command.Kind == CommandKind.Launch) Launch(command);
            else if (State == MixerConnectionState.Connected) CheckWrite(_api.SetParameters(command.Text!), "script");
            else _logger.LogDebug("Script ignoré hors connexion.");
        }
        Flush(pending);
    }
    private void Flush(Dictionary<string, Command> pending)
    {
        foreach (var command in pending.Values)
        {
            if (State != MixerConnectionState.Connected)
            {
                _logger.LogDebug("Commande ignorée hors connexion : {Parameter}", command.Parameter);
                continue;
            }
            CheckWrite(command.Kind == CommandKind.Float
                ? _api.SetParameterFloat(command.Parameter, command.Number)
                : _api.SetParameterString(command.Parameter, command.Text!), command.Parameter);
        }
        pending.Clear();
    }
    private void CheckWrite(int result, string parameter)
    {
        if (result != 0) _logger.LogDebug("Écriture refusée pour {Parameter} : {Result}", parameter, result);
    }
    private void Launch(Command command)
    {
        if (command.Cancellation.IsCancellationRequested) { command.Completion!.TrySetCanceled(command.Cancellation); return; }
        try
        {
            var accepted = _api.RunVoicemeeter(5) == 0 || _api.RunVoicemeeter(2) == 0;
            command.Completion!.TrySetResult(accepted);
            if (accepted && State != MixerConnectionState.Connected) _nextConnect = _time.GetUtcNow();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Impossible de lancer Voicemeeter.");
            command.Completion!.TrySetResult(false);
        }
    }
    private void DropWrites()
    {
        // Les demandes de lancement gardent leur réponse même si une connexion vient d'échouer.
        var count = _commands.Count;
        for (var i = 0; i < count && _commands.TryDequeue(out var command); i++)
            if (command.Kind == CommandKind.Launch) Launch(command);
            else _logger.LogDebug("Commande ignorée hors connexion : {Parameter}", command.Parameter);
    }
    private void CancelPending()
    {
        while (_commands.TryDequeue(out var command)) command.Completion?.TrySetResult(false);
    }

    private MixerSnapshot ReadSnapshot()
    {
        var strips = new StripState[BananaLayout.StripCount];
        var buses = new BusState[BananaLayout.BusCount];
        for (var i = 0; i < strips.Length; i++)
        {
            var routes = Enumerable.Range(0, BananaLayout.BusCount).Select(bus => ReadFloat(VmParam.StripRoute(i, bus)) != 0).ToArray();
            var eq = BananaLayout.IsVirtualStrip(i)
                ? Enumerable.Range(0, BananaLayout.VirtualStripEqBands).Select(band => ReadFloat(VmParam.StripEqGain(i, band))).ToArray() : [];
            strips[i] = new(i, ReadString(VmParam.StripLabel(i)), ReadFloat(VmParam.StripGain(i)),
                ReadFloat(VmParam.StripMute(i)) != 0, ReadFloat(VmParam.StripSolo(i)) != 0,
                ReadFloat(VmParam.StripPanX(i)), Array.AsReadOnly(routes), Array.AsReadOnly(eq));
        }
        for (var i = 0; i < buses.Length; i++)
            buses[i] = new(i, ReadString(VmParam.BusLabel(i)), ReadFloat(VmParam.BusGain(i)),
                ReadFloat(VmParam.BusMute(i)) != 0, ReadFloat(VmParam.BusMono(i)) != 0, ReadFloat(VmParam.BusEqOn(i)) != 0);
        return new(Array.AsReadOnly(strips), Array.AsReadOnly(buses), Snapshot.Revision + 1);
    }
    private float ReadFloat(string parameter)
    {
        var result = _api.GetParameterFloat(parameter, out var value);
        if (result != 0 || !float.IsFinite(value)) throw new IOException($"Lecture impossible : {parameter} ({result}).");
        return value;
    }
    private string ReadString(string parameter)
    {
        var result = _api.GetParameterString(parameter, out var value);
        if (result != 0) throw new IOException($"Lecture impossible : {parameter} ({result}).");
        return value;
    }
    private void PublishSnapshot(MixerSnapshot next)
    {
        var previous = Snapshot;
        var same = previous.Buses.SequenceEqual(next.Buses) && previous.Strips.Zip(next.Strips).All(pair =>
            pair.First.Index == pair.Second.Index && pair.First.Label == pair.Second.Label &&
            pair.First.Gain == pair.Second.Gain && pair.First.Mute == pair.Second.Mute &&
            pair.First.Solo == pair.Second.Solo && pair.First.PanX == pair.Second.PanX &&
            pair.First.Routes.SequenceEqual(pair.Second.Routes) && pair.First.EqGains.SequenceEqual(pair.Second.EqGains));
        if (same) return;
        Volatile.Write(ref _snapshot, next);
        Raise(SnapshotChanged, next);
    }
    private void ChangeState(MixerConnectionState state)
    {
        if (State == state) return;
        Volatile.Write(ref _state, (int)state);
        Raise(StateChanged, state);
    }
    private void Raise<T>(EventHandler<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (EventHandler<T> handler in handlers.GetInvocationList())
        {
            try { handler(this, value); }
            catch (Exception exception) { _logger.LogError(exception, "Un gestionnaire d'événement du mixeur a échoué."); }
        }
    }
    private TimeSpan NextWait()
    {
        if (!_commands.IsEmpty) return TimeSpan.Zero;
        var next = State == MixerConnectionState.Connected ? _nextDirty : _nextConnect;
        if (State == MixerConnectionState.Connected && LevelsEnabled && _nextLevel < next) next = _nextLevel;
        var remaining = next - _time.GetUtcNow();
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromMilliseconds(Math.Clamp(Math.Ceiling(remaining.TotalMilliseconds), 1, 30000));
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifecycle)
        {
            if (_disposeTask is not null) return new(_disposeTask);
            Interlocked.Exchange(ref _disposed, 1);
            Signal();
            if (_thread is null)
            {
                Logout(); CancelPending(); ChangeState(MixerConnectionState.Disconnected);
                _stopped.TrySetResult();
            }
            _disposeTask = FinishDisposeAsync();
            return new(_disposeTask);
        }
    }
    private async Task FinishDisposeAsync()
    {
        await _stopped.Task.ConfigureAwait(false);
        _wake.Dispose();
    }

    private enum CommandKind { Float, String, Script, Launch }
    private sealed record Command(CommandKind Kind, string Parameter, float Number, string? Text,
        TaskCompletionSource<bool>? Completion, CancellationToken Cancellation = default);
}
