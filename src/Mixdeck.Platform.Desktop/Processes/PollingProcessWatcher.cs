using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Processes;

/// <summary>Diff du tableau des processus. Linux lit /proc ; macOS interroge la liste des processus.</summary>
public sealed class PollingProcessWatcher : IProcessWatcher
{
    private readonly TimeSpan _interval;
    private CancellationTokenSource? _lifetime;
    private HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);

    public PollingProcessWatcher(TimeSpan? interval = null) => _interval = interval ?? TimeSpan.FromSeconds(1);

    public event EventHandler<ProcessEventArgs>? ProcessStarted;
    public event EventHandler<ProcessEventArgs>? ProcessExited;

    public IReadOnlyCollection<string> GetRunningProcessNames() => ProcessNames.Snapshot();

    public void Start()
    {
        if (_lifetime is not null) return;
        _known = new HashSet<string>(GetRunningProcessNames(), StringComparer.OrdinalIgnoreCase);
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
        HashSet<string> current;
        try { current = new HashSet<string>(GetRunningProcessNames(), StringComparer.OrdinalIgnoreCase); }
        catch (IOException) { return; }
        foreach (var name in current)
        {
            if (_known.Contains(name)) continue;
            try { ProcessStarted?.Invoke(this, new ProcessEventArgs(name, 0)); }
            catch { /* Isoler l'abonné. */ }
        }
        foreach (var name in _known)
        {
            if (current.Contains(name)) continue;
            try { ProcessExited?.Invoke(this, new ProcessEventArgs(name, 0)); }
            catch { /* Isoler l'abonné. */ }
        }
        _known = current;
    }
}
