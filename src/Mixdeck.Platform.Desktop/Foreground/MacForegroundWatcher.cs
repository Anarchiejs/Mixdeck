using System.Runtime.InteropServices;
using System.Text;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Foreground;

/// <summary>Application au premier plan via NSWorkspace.frontmostApplication. macOS seulement.</summary>
public sealed class MacForegroundWatcher : IForegroundWatcher, IForegroundCapability
{
    private CancellationTokenSource? _lifetime;
    private string? _current;

    public bool IsSupported => OperatingSystem.IsMacOS();
    public string? UnavailableReason => IsSupported ? null : "The frontmost application is only read on macOS.";
    public string? Current => _current;
    public event EventHandler<string?>? ForegroundProcessChanged;

    public void Start()
    {
        if (!OperatingSystem.IsMacOS() || _lifetime is not null) return;
        try { NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"); }
        catch (DllNotFoundException)
        {
            return;
        }
        _lifetime = new CancellationTokenSource();
        var token = _lifetime.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                var name = Frontmost();
                if (!string.Equals(name, _current, StringComparison.Ordinal))
                {
                    _current = name;
                    try { ForegroundProcessChanged?.Invoke(this, name); }
                    catch { /* Isoler l'abonné. */ }
                }
                try { await Task.Delay(TimeSpan.FromMilliseconds(400), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }, token);
    }

    public void Dispose()
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private static string? Frontmost()
    {
        var pool = objc_msgSend(objc_msgSend(objc_getClass("NSAutoreleasePool"), Sel("alloc")), Sel("init"));
        try
        {
            var app = objc_msgSend(objc_msgSend(objc_getClass("NSWorkspace"), Sel("sharedWorkspace")), Sel("frontmostApplication"));
            if (app == 0) return null;
            var path = objc_msgSend(objc_msgSend(app, Sel("executableURL")), Sel("path"));
            var leaf = objc_msgSend(path, Sel("lastPathComponent"));
            var utf8 = objc_msgSend(leaf, Sel("UTF8String"));
            return utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
        }
        finally
        {
            if (pool != 0) objc_msgSend(pool, Sel("drain"));
        }
    }

    private static IntPtr Sel(string name) => sel_registerName(name);

    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
}
