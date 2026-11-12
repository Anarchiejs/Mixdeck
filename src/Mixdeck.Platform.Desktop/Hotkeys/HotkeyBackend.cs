using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.Platform.Desktop.Hotkeys;

public interface IHotkeyBackend
{
    bool IsSupported { get; }

    string? UnavailableReason { get; }
}

public sealed class UnsupportedHotkeyService(string reason) : IHotkeyService, IHotkeyBackend
{
    public bool IsSupported => false;
    public string? UnavailableReason { get; } = reason;
    public IReadOnlyList<HotkeyBinding> Failed { get; private set; } = [];
    public event EventHandler<HotkeyEventArgs>? Triggered { add { } remove { } }

    public void Apply(IReadOnlyList<HotkeyBinding> bindings) => Failed = bindings.ToArray();

    public void Dispose() { }
}
