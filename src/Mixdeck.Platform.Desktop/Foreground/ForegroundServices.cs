using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Foreground;

public interface IForegroundCapability
{
    bool IsSupported { get; }
    string? UnavailableReason { get; }
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
