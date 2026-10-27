using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Interop;

namespace Mixdeck.Platform.Foreground;

public sealed class ForegroundWatcher : IForegroundWatcher
{
    private readonly object _gate = new();
    private readonly ILogger<ForegroundWatcher> _logger;
    private readonly Native.WinEventProc _callback;
    private MessageWindow? _window;
    private nint _hook;
    private string? _current;
    private bool _initialized;
    private bool _disposed;

    public ForegroundWatcher(ILogger<ForegroundWatcher>? logger = null)
    {
        _logger = logger ?? NullLogger<ForegroundWatcher>.Instance;
        _callback = OnForeground;
    }

    public event EventHandler<string?>? ForegroundProcessChanged;
    public string? Current => Volatile.Read(ref _current);

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_window != null) return;
            _window = new MessageWindow("Mixdeck premier plan", (_, _, _) => null);
        }
        _window.Invoke(() =>
        {
            _hook = Native.SetWinEventHook(3, 3, 0, Marshal.GetFunctionPointerForDelegate(_callback), 0, 0, 0);
            if (_hook == 0) _logger.LogWarning("Impossible d'observer le premier plan (erreur {Error}).", Marshal.GetLastPInvokeError());
            Update(Native.GetForegroundWindow());
        });
    }

    private void OnForeground(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time)
    {
        try { Update(window); }
        catch (Exception ex) { _logger.LogWarning(ex, "Impossible de traiter le premier plan."); }
    }

    private unsafe void Update(nint window)
    {
        string? name = null;
        if (window != 0 && Native.GetWindowThreadProcessId(window, out uint pid) != 0)
        {
            using var process = Native.OpenProcess(0x1000, false, pid);
            if (!process.IsInvalid)
            {
                char* path = stackalloc char[32768];
                uint length = 32768;
                if (Native.QueryFullProcessImageName(process, 0, path, ref length)) name = Path.GetFileName(new string(path, 0, (int)length));
            }
        }
        if (_initialized && string.Equals(name, Current, StringComparison.OrdinalIgnoreCase)) return;
        _initialized = true;
        Volatile.Write(ref _current, name);
        try { ForegroundProcessChanged?.Invoke(this, name); }
        catch (Exception ex) { _logger.LogError(ex, "Erreur du gestionnaire de premier plan."); }
    }

    public void Dispose()
    {
        MessageWindow? window;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            window = _window;
        }
        if (window == null) return;
        try { window.Invoke(() => { if (_hook != 0) { Native.UnhookWinEvent(_hook); _hook = 0; } }); }
        finally { window.Dispose(); }
        GC.KeepAlive(_callback);
    }
}
