using System.Runtime.InteropServices;
using System.Text;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Foreground;

/// <summary>Application au premier plan via NSWorkspace, sur la boucle d'exécution qui a appelé Start.</summary>
public sealed class MacForegroundWatcher : IForegroundWatcher, IForegroundCapability
{
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private readonly TimerCallback _tick;
    private IntPtr _timer;
    private string? _current;

    public MacForegroundWatcher() => _tick = OnTick;

    public bool IsSupported => OperatingSystem.IsMacOS();
    public string? UnavailableReason => IsSupported ? null : "The frontmost application is only read on macOS.";
    public string? Current => _current;
    public event EventHandler<string?>? ForegroundProcessChanged;

    public void Start()
    {
        if (!OperatingSystem.IsMacOS() || _timer != 0) return;
        try { NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"); }
        catch (DllNotFoundException) { return; }
        var timer = CFRunLoopTimerCreate(0, CFAbsoluteTimeGetCurrent() + 0.4, 0.4, 0, 0, _tick, 0);
        if (timer == 0) return;
        CFRunLoopAddTimer(CFRunLoopGetCurrent(), timer, DefaultMode());
        _timer = timer;
        OnTick(timer, 0);
    }

    public void Dispose()
    {
        if (_timer == 0) return;
        CFRunLoopTimerInvalidate(_timer);
        CFRelease(_timer);
        _timer = 0;
    }

    private void OnTick(IntPtr timer, IntPtr info)
    {
        var name = Frontmost();
        if (string.Equals(name, _current, StringComparison.Ordinal)) return;
        _current = name;
        try { ForegroundProcessChanged?.Invoke(this, name); }
        catch { /* Isoler l'abonné. */ }
    }

    private static IntPtr DefaultMode()
    {
        var library = NativeLibrary.Load(CoreFoundation);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCFRunLoopDefaultMode"));
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

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TimerCallback(IntPtr timer, IntPtr info);

    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
    [DllImport(CoreFoundation)] private static extern IntPtr CFRunLoopGetCurrent();
    [DllImport(CoreFoundation)] private static extern double CFAbsoluteTimeGetCurrent();
    [DllImport(CoreFoundation)] private static extern IntPtr CFRunLoopTimerCreate(IntPtr allocator, double fireDate, double interval, uint flags, int order, TimerCallback callout, IntPtr context);
    [DllImport(CoreFoundation)] private static extern void CFRunLoopAddTimer(IntPtr loop, IntPtr timer, IntPtr mode);
    [DllImport(CoreFoundation)] private static extern void CFRunLoopTimerInvalidate(IntPtr timer);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
