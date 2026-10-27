using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using static Mixdeck.Platform.Audio.CoreAudioInterop;

namespace Mixdeck.Platform.Audio;

/// <summary>
/// Mélangeur Windows de la sortie multimédia par défaut. Aucun sondage : le thread dort jusqu'à une
/// commande, une notification COM ou l'échéance unique d'une rafale de changements.
/// Créez une instance pour la durée d'affichage du panneau et libérez-la lorsqu'il est caché.
/// </summary>
public sealed class AppAudioSessions : IAppAudioSessions, IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly BlockingCollection<Action> _commands = new();
    private readonly Dictionary<string, TrackedSession> _tracked = new(StringComparer.Ordinal);
    private readonly ILogger<AppAudioSessions> _logger;
    private readonly AppAudioEventLimiter _limiter = new(TimeProvider.System);
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<AppAudioSession> _sessions = Array.Empty<AppAudioSession>();
    private Thread? _thread;
    private volatile bool _disposed;
    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioSessionManager2? _manager;
    private EndpointNotifications? _endpointNotifications;
    private SessionNotifications? _sessionNotifications;
    private string? _endpointId;
    private int _generation;

    public AppAudioSessions(ILogger<AppAudioSessions>? logger = null) =>
        _logger = logger ?? NullLogger<AppAudioSessions>.Instance;

    public IReadOnlyList<AppAudioSession> Sessions => Volatile.Read(ref _sessions);
    public event EventHandler? SessionsChanged;
    internal Task Initialization => _initialized.Task;
    internal bool HasDefaultEndpoint => Volatile.Read(ref _endpointId) is not null;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _thread is not null) return;
            _thread = new Thread(Run) { IsBackground = true, Name = "Mixdeck · sessions audio" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }
    }

    public void SetVolume(string sessionId, float volume)
    {
        if (!float.IsFinite(volume)) return;
        Post(() => WriteGroup(sessionId, entry => Check(entry.Volume.SetMasterVolume(Math.Clamp(volume, 0, 1), Guid.Empty))));
    }

    public void SetMute(string sessionId, bool mute) =>
        Post(() => WriteGroup(sessionId, entry => Check(entry.Volume.SetMute(mute, Guid.Empty))));

    // Dispose ne bloque jamais l'interface ; DisposeAsync permet d'attendre la libération COM hors UI.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            SessionsChanged = null;
            Volatile.Write(ref _sessions, Array.Empty<AppAudioSession>());
            _commands.CompleteAdding();
            if (_thread is null)
            {
                _commands.Dispose();
                _initialized.TrySetResult();
                _completed.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await _completed.Task.ConfigureAwait(false);
    }

    private bool Post(Action action)
    {
        lock (_gate)
        {
            if (_disposed || _thread is null) return false;
            _commands.Add(action);
            return true;
        }
    }

    private void Run()
    {
        var comInitialized = false;
        try
        {
            Check(CoInitializeEx(0, 0));
            comInitialized = true;
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            _endpointNotifications = new EndpointNotifications(this);
            Check(_enumerator.RegisterEndpointNotificationCallback(_endpointNotifications));
            Safely(BindDefaultEndpoint);
            _initialized.TrySetResult();

            while (!_commands.IsCompleted)
            {
                if (_commands.TryTake(out var action, _limiter.DelayMilliseconds)) Safely(action);
                if (_limiter.Take()) Publish();
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Le suivi des sessions audio Windows s'est arrêté.");
        }
        finally
        {
            // Ferme la file avant de vider les AddRef transférés par les callbacks en cours.
            lock (_gate)
            {
                _disposed = true;
                _commands.CompleteAdding();
                SessionsChanged = null;
            }
            ++_generation;
            while (_commands.TryTake(out var pending)) Safely(pending);
            ClearEndpoint();
            if (_enumerator is not null && _endpointNotifications is not null)
                Safely(() => Check(_enumerator.UnregisterEndpointNotificationCallback(_endpointNotifications)));
            Release(_enumerator);
            _enumerator = null;
            _endpointNotifications = null;
            Volatile.Write(ref _sessions, Array.Empty<AppAudioSession>());
            if (comInitialized) CoUninitialize();
            _commands.Dispose();
            _initialized.TrySetResult();
            _completed.TrySetResult();
        }
    }

    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception exception) { _logger.LogWarning(exception, "Une opération sur les sessions audio Windows a échoué."); }
    }

    private void BindDefaultEndpoint()
    {
        ClearEndpoint();
        if (_enumerator is null) return;
        var result = _enumerator.GetDefaultAudioEndpoint(0, 1, out var device); // eRender, eMultimedia
        if (result == unchecked((int)0x80070490)) return; // Aucune sortie : attendre la notification suivante.
        Check(result);
        _device = device;
        Check(device.GetId(out var id));
        _endpointId = id;
        Check(device.Activate(SessionManagerId, 23, 0, out var manager));
        _manager = (IAudioSessionManager2)manager;
        var generation = _generation;
        _sessionNotifications = new SessionNotifications(this, generation);
        Check(_manager.RegisterSessionNotification(_sessionNotifications));
        Check(_manager.GetSessionEnumerator(out var sessions));
        try
        {
            // GetCount arme les notifications ; enregistrer avant d'énumérer évite la fenêtre de course.
            Check(sessions.GetCount(out var count));
            for (var i = 0; i < count; i++)
            {
                if (sessions.GetSession(i, out var control) >= 0) AddSession(control, generation);
            }
        }
        finally { Release(sessions); }
        _limiter.MarkChanged();
    }

    private void ClearEndpoint()
    {
        ++_generation;
        foreach (var entry in _tracked.Values) entry.Dispose(_logger);
        _tracked.Clear();
        if (_manager is not null && _sessionNotifications is not null)
            Safely(() => Check(_manager.UnregisterSessionNotification(_sessionNotifications)));
        Release(_manager);
        Release(_device);
        _manager = null;
        _device = null;
        _sessionNotifications = null;
        _endpointId = null;
        _limiter.MarkChanged();
    }

    // Prend possession de l'unique référence RCW obtenue par l'énumération ou GetObjectForIUnknown.
    private void AddSession(IAudioSessionControl control, int generation)
    {
        TrackedSession? tracked = null;
        var retained = false;
        try
        {
            if (generation != _generation || _disposed) return;
            var control2 = (IAudioSessionControl2)control;
            Check(control2.GetSessionInstanceIdentifier(out var id));
            if (_tracked.ContainsKey(id)) return;
            tracked = new TrackedSession(control, control2, (ISimpleAudioVolume)control,
                new SessionEvents(this, id, generation));
            Check(control.RegisterAudioSessionNotification(tracked.Events));
            tracked.Registered = true;
            if (!tracked.Refresh()) return;
            _tracked.Add(id, tracked);
            retained = true;
            _limiter.MarkChanged();
        }
        catch (Exception exception) { _logger.LogWarning(exception, "Impossible de lire une session audio Windows."); }
        finally
        {
            if (!retained)
            {
                if (tracked is not null) tracked.Dispose(_logger);
                else Release(control);
            }
        }
    }

    private void Refresh(string id, int generation, bool disconnected)
    {
        if (generation != _generation || !_tracked.TryGetValue(id, out var entry)) return;
        if (disconnected || !entry.Refresh())
        {
            _tracked.Remove(id);
            entry.Dispose(_logger);
        }
        _limiter.MarkChanged();
    }

    private void WriteGroup(string id, Action<TrackedSession> write)
    {
        if (!_tracked.TryGetValue(id, out var selected)) return;
        var key = AppAudioSessionPolicy.GroupKey(selected.Entry!);
        foreach (var entry in _tracked.Values.Where(entry => AppAudioSessionPolicy.GroupKey(entry.Entry!) == key))
        {
            Safely(() => { write(entry); entry.Refresh(); });
        }
        _limiter.MarkChanged();
    }

    private void Publish()
    {
        if (_disposed) return;
        var snapshot = AppAudioSessionPolicy.Group(_tracked.Values.Select(entry => entry.Entry!));
        if (Sessions.SequenceEqual(snapshot)) return;
        Volatile.Write(ref _sessions, snapshot);
        var handlers = SessionsChanged;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
            Safely(() => handler(this, EventArgs.Empty));
    }

    private sealed class TrackedSession(IAudioSessionControl control, IAudioSessionControl2 control2,
        ISimpleAudioVolume volume, SessionEvents events)
    {
        private bool _metadataLoaded;
        private string _processName = string.Empty;
        private string? _path;
        private string? _description;
        internal ISimpleAudioVolume Volume { get; } = volume;
        internal SessionEvents Events { get; } = events;
        internal bool Registered { get; set; }
        internal SessionEntry? Entry { get; private set; }

        internal bool Refresh()
        {
            Check(control.GetState(out var state));
            if (state == 2) return false; // Expired ; Inactive reste réglable comme dans Windows.
            Check(control2.GetSessionInstanceIdentifier(out var id));
            Check(control2.GetProcessId(out var pid));
            var system = control2.IsSystemSoundsSession() == 0;
            if (!_metadataLoaded)
            {
                _metadataLoaded = true;
                if (!system && pid != 0)
                {
                    try
                    {
                        using var process = Process.GetProcessById(checked((int)pid));
                        _processName = process.ProcessName + ".exe";
                        _path = process.MainModule?.FileName;
                        if (_path is not null) _description = FileVersionInfo.GetVersionInfo(_path).FileDescription;
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                        or System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException)
                    { /* Processus protégé ou déjà terminé : conserver les informations disponibles. */ }
                }
            }
            control.GetDisplayName(out var name);
            Check(control.GetGroupingParam(out var grouping));
            Check(Volume.GetMasterVolume(out var currentVolume));
            Check(Volume.GetMute(out var mute));
            Entry = new SessionEntry(new AppAudioSession(id, (int)pid, _processName,
                AppAudioSessionPolicy.DisplayName(ResolveName(name ?? string.Empty), _description, _processName, system),
                _path, currentVolume, mute, system), grouping);
            return true;
        }

        internal void Dispose(ILogger logger)
        {
            try
            {
                if (Registered) Check(control.UnregisterAudioSessionNotification(Events));
            }
            catch (Exception exception) { logger.LogDebug(exception, "La session audio était déjà déconnectée."); }
            finally { Release(control); }
            // Les deux autres interfaces partagent ce RCW ; ne pas les libérer une seconde fois.
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class EndpointNotifications(AppAudioSessions owner) : IMMNotificationClient
    {
        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == 0 && role == 1) owner.Post(owner.BindDefaultEndpoint);
            return 0;
        }
        public int OnDeviceStateChanged(string id, uint state)
        {
            owner.Post(() => { if (id == owner._endpointId && state != 1) owner.BindDefaultEndpoint(); });
            return 0;
        }
        public int OnDeviceRemoved(string id) => OnDeviceStateChanged(id, 0);
        public int OnDeviceAdded(string id) => 0;
        public int OnPropertyValueChanged(string id, PropertyKey key) => 0;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class SessionNotifications(AppAudioSessions owner, int generation) : IAudioSessionNotification
    {
        public int OnSessionCreated(nint session)
        {
            Marshal.AddRef(session);
            if (!owner.Post(() =>
            {
                try { owner.AddSession((IAudioSessionControl)Marshal.GetObjectForIUnknown(session), generation); }
                finally { Marshal.Release(session); }
            })) Marshal.Release(session);
            return 0;
        }
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class SessionEvents(AppAudioSessions owner, string id, int generation) : IAudioSessionEvents
    {
        private int Changed(bool disconnected = false)
        {
            owner.Post(() => owner.Refresh(id, generation, disconnected));
            return 0;
        }
        public int OnDisplayNameChanged(string name, in Guid context) => Changed();
        public int OnIconPathChanged(string path, in Guid context) => Changed();
        public int OnSimpleVolumeChanged(float volume, bool mute, in Guid context) => Changed();
        public int OnChannelVolumeChanged(uint count, nint volumes, uint channel, in Guid context) => 0;
        public int OnGroupingParamChanged(in Guid grouping, in Guid context) => Changed();
        public int OnStateChanged(int state) => Changed();
        public int OnSessionDisconnected(int reason) => Changed(true);
    }
}
