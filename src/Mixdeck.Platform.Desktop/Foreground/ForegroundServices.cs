using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Desktop.Native;

namespace Mixdeck.Platform.Desktop.Foreground;

public interface IForegroundCapability
{
    bool IsSupported { get; }
    string? UnavailableReason { get; }
}

public sealed class X11ForegroundWatcher : IForegroundWatcher, IForegroundCapability
{
    private readonly X11Session? _session;
    private string? _current;

    internal X11ForegroundWatcher(X11Session? session, string? reason)
    {
        _session = session;
        IsSupported = session is not null;
        UnavailableReason = reason;
    }

    public bool IsSupported { get; }
    public string? UnavailableReason { get; }
    public string? Current => _current;
    public event EventHandler<string?>? ForegroundProcessChanged;

    public void Start()
    {
        _session?.WatchForeground(name =>
        {
            _current = name;
            try { ForegroundProcessChanged?.Invoke(this, name); }
            catch { /* Isoler l'abonné. */ }
        });
    }

    public void Dispose() { }
}

public sealed class IdleForegroundWatcher(string reason) : IForegroundWatcher, IForegroundCapability
{
    public bool IsSupported => false;
    public string? UnavailableReason { get; } = reason;
    public string? Current => null;
    public event EventHandler<string?>? ForegroundProcessChanged { add { } remove { } }
    public void Start() { }
    public void Dispose() { }
}
