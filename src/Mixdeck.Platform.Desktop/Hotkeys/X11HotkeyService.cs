using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Platform.Desktop.Native;

namespace Mixdeck.Platform.Desktop.Hotkeys;

public sealed class X11HotkeyService : IHotkeyService, IHotkeyBackend
{
    private readonly X11Session? _session;

    internal X11HotkeyService(X11Session? session, string? reason)
    {
        _session = session;
        IsSupported = session is not null;
        UnavailableReason = reason;
    }

    public bool IsSupported { get; }
    public string? UnavailableReason { get; }
    public IReadOnlyList<HotkeyBinding> Failed { get; private set; } = [];
    public event EventHandler<HotkeyEventArgs>? Triggered;

    public void Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        if (_session is null)
        {
            Failed = bindings.ToArray();
            return;
        }
        var done = new TaskCompletionSource<IReadOnlyList<HotkeyBinding>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.SetHotkeys(bindings, (binding, pressed) =>
        {
            try { Triggered?.Invoke(this, new HotkeyEventArgs(binding, pressed)); }
            catch { /* Isoler l'abonné. */ }
        }, failed => done.TrySetResult(failed));
        Failed = done.Task.Wait(TimeSpan.FromSeconds(2)) ? done.Task.Result : bindings.ToArray();
    }

    public void Dispose() { }
}
