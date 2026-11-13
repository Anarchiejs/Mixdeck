using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Processes;

/// <summary>Diff des processus par identifiant, avec le nom du fichier exécutable.</summary>
public sealed class PollingProcessWatcher : IProcessWatcher
{
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _lifetime;
    private Dictionary<int, string> _known = [];

    public PollingProcessWatcher(TimeSpan? interval = null) => _interval = interval ?? TimeSpan.FromSeconds(1);

    public event EventHandler<ProcessEventArgs>? ProcessStarted;
    public event EventHandler<ProcessEventArgs>? ProcessExited;

    public IReadOnlyCollection<string> GetRunningProcessNames() =>
        ProcessNames.Snapshot().Select(record => record.Name).ToArray();

    public void Start()
    {
        if (_lifetime is not null) return;
        _known = ProcessNames.Snapshot().ToDictionary(record => record.Pid, record => record.Name);
        _lifetime = new CancellationTokenSource();
        var token = _lifetime.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await Task.Delay(_interval, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                Diff();
            }
        }, token);
    }

    public void Dispose()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private void Diff()
    {
        Dictionary<int, string> current;
        try { current = ProcessNames.Snapshot().ToDictionary(record => record.Pid, record => record.Name); }
        catch (IOException) { return; }
        foreach (var pair in current)
        {
            if (_known.ContainsKey(pair.Key)) continue;
            try { ProcessStarted?.Invoke(this, new ProcessEventArgs(pair.Value, pair.Key)); }
            catch { /* Isoler l'abonné. */ }
        }
        foreach (var pair in _known)
        {
            if (current.ContainsKey(pair.Key)) continue;
            try { ProcessExited?.Invoke(this, new ProcessEventArgs(pair.Value, pair.Key)); }
            catch { /* Isoler l'abonné. */ }
        }
        _known = current;
    }
}
