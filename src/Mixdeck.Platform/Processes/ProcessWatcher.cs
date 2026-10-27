using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Processes;

public sealed class ProcessWatcher : IProcessWatcher
{
    private readonly object _gate = new();
    private readonly ILogger<ProcessWatcher> _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly TimeProvider _time = TimeProvider.System;
    private readonly Dictionary<int, string> _known = [];
    private ManagementEventWatcher? _created, _deleted;
    private Task? _polling;
    private bool _started, _disposed;

    public ProcessWatcher(ILogger<ProcessWatcher>? logger = null) => _logger = logger ?? NullLogger<ProcessWatcher>.Instance;
    public event EventHandler<ProcessEventArgs>? ProcessStarted;
    public event EventHandler<ProcessEventArgs>? ProcessExited;

    public IReadOnlyCollection<string> GetRunningProcessNames() => Snapshot().Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started) return;
            _started = true;
            foreach (var pair in Snapshot()) _known[pair.Key] = pair.Value;
            try
            {
                _created = CreateWatcher("Creation", true);
                _deleted = CreateWatcher("Deletion", false);
                _created.Start();
                _deleted.Start();
            }
            catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _logger.LogWarning(ex, "WMI indisponible ; sondage des processus toutes les trois secondes.");
                StartPolling();
            }
        }
    }

    private ManagementEventWatcher CreateWatcher(string kind, bool started)
    {
        var watcher = new ManagementEventWatcher(new WqlEventQuery($"SELECT * FROM __Instance{kind}Event WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'"));
        watcher.EventArrived += (_, args) =>
        {
            try
            {
                using var target = (ManagementBaseObject)args.NewEvent["TargetInstance"];
                int pid = Convert.ToInt32(target["ProcessId"], CultureInfo.InvariantCulture);
                string? name = target["Name"] as string;
                if (string.IsNullOrEmpty(name)) return;
                ProcessEventArgs? notification = null;
                lock (_gate)
                {
                    if (_disposed || _polling != null) return;
                    if (started)
                    {
                        if (!_known.TryGetValue(pid, out string? previous) || !string.Equals(previous, name, StringComparison.OrdinalIgnoreCase)) notification = new ProcessEventArgs(name, pid);
                        _known[pid] = name;
                    }
                    else if (_known.Remove(pid)) notification = new ProcessEventArgs(name, pid);
                }
                if (notification != null) Raise(started, notification);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Erreur de notification WMI ; activation du sondage.");
                lock (_gate) { if (!_disposed) StartPolling(); }
            }
        };
        watcher.Stopped += (_, args) =>
        {
            lock (_gate)
            {
                if (_disposed || _polling != null) return;
                _logger.LogWarning("Observation WMI interrompue ({Status}) ; activation du sondage.", args.Status);
                StartPolling();
            }
        };
        return watcher;
    }

    private void StartPolling() => _polling ??= Task.Run(PollAsync);

    private async Task PollAsync()
    {
        // L'arrêt des abonnements se fait hors du rappel WMI et hors du verrou.
        StopWatcher(_created);
        StopWatcher(_deleted);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                var current = Snapshot();
                List<(bool Started, ProcessEventArgs Args)> changes = [];
                lock (_gate)
                {
                    if (_disposed) return;
                    foreach (var pair in _known)
                        if (!current.TryGetValue(pair.Key, out string? name) || !string.Equals(name, pair.Value, StringComparison.OrdinalIgnoreCase)) changes.Add((false, new ProcessEventArgs(pair.Value, pair.Key)));
                    foreach (var pair in current)
                        if (!_known.TryGetValue(pair.Key, out string? name) || !string.Equals(name, pair.Value, StringComparison.OrdinalIgnoreCase)) changes.Add((true, new ProcessEventArgs(pair.Value, pair.Key)));
                    _known.Clear();
                    foreach (var pair in current) _known.Add(pair.Key, pair.Value);
                }
                foreach (var change in changes)
                {
                    if (_stop.IsCancellationRequested) return;
                    Raise(change.Started, change.Args);
                }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private Dictionary<int, string> Snapshot()
    {
        Dictionary<int, string> result = [];
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        string name = process.ProcessName;
                        if (!string.IsNullOrWhiteSpace(name)) result[process.Id] = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { _logger.LogWarning(ex, "Impossible d'énumérer les processus."); }
        return result;
    }

    private void Raise(bool started, ProcessEventArgs args)
    {
        try { (started ? ProcessStarted : ProcessExited)?.Invoke(this, args); }
        catch (Exception ex) { _logger.LogError(ex, "Erreur du gestionnaire de processus."); }
    }

    private void StopWatcher(ManagementEventWatcher? watcher)
    {
        if (watcher == null) return;
        try { watcher.Stop(); }
        catch (Exception ex) { _logger.LogDebug(ex, "Arrêt de l'observation WMI."); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
        }
        // Ne pas attendre le sondage : Dispose peut être appelé par un abonné.
        StopWatcher(_created);
        StopWatcher(_deleted);
        _created?.Dispose();
        _deleted?.Dispose();
        if (_polling == null) _stop.Dispose();
        else _ = _polling.ContinueWith(_ => _stop.Dispose(), TaskScheduler.Default);
    }
}
