using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mixdeck.Core.Model;
using Mixdeck.Platform.Desktop.Hotkeys;
using Mixdeck.Platform.Desktop.Processes;

namespace Mixdeck.Platform.Desktop.Native;

/// <summary>
/// Une connexion X11 pour les raccourcis globaux (XGrabKey) et la fenêtre active (_NET_ACTIVE_WINDOW).
/// Sous Wayland ces deux fonctions n'ont pas d'équivalent stable : la session n'est pas ouverte.
/// Les offsets de XKeyEvent et XErrorEvent sont ceux de libX11 64 bits.
/// </summary>
internal sealed class X11Session : IDisposable
{
    private const int KeyPress = 2;
    private const int KeyRelease = 3;
    private const int PropertyNotify = 28;
    private const int StateOffset = 80;
    private const int KeycodeOffset = 84;
    private const int TimeOffset = 56;
    private const int EventSize = 192;
    private const int ErrorCodeOffset = 32;
    private const byte BadAccess = 10;
    private const int GrabModeAsync = 1;
    private const int QueuedAfterReading = 1;
    private const long PropertyChangeMask = 1 << 22;
    private const ulong AnyModifier = 1u << 15;

    private readonly IntPtr _display;
    private readonly nuint _root;
    private readonly nuint _activeAtom;
    private readonly nuint _pidAtom;
    private readonly Thread _thread;
    private readonly ConcurrentQueue<Action> _commands = new();
    private static readonly XErrorHandler ErrorThunk = OnNativeError;
    private static X11Session? _handling;

    private readonly byte[] _event = new byte[EventSize];
    private readonly byte[] _peek = new byte[EventSize];
    private List<GrabbedKey> _keys = [];
    private Action<HotkeyBinding, bool>? _onHotkey;
    private Action<string?>? _onForeground;
    private string? _foreground;
    private int _stop;
    private int _badAccess;

    private X11Session(IntPtr display)
    {
        _display = display;
        _root = XDefaultRootWindow(display);
        _activeAtom = XInternAtom(display, "_NET_ACTIVE_WINDOW", false);
        _pidAtom = XInternAtom(display, "_NET_WM_PID", false);
        XSelectInput(display, _root, PropertyChangeMask);
        _thread = new Thread(Loop) { IsBackground = true, Name = "Mixdeck X11" };
        _thread.Start();
    }

    public static bool IsWaylandSession()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "x11", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    public static X11Session? TryOpen(out string? reason)
    {
        if (!OperatingSystem.IsLinux())
        {
            reason = "X11 hotkeys are only used on Linux.";
            return null;
        }
        if (IsWaylandSession())
        {
            reason = "Global hotkeys and the foreground app are not available on Wayland. An X11 session can register them.";
            return null;
        }
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            reason = "DISPLAY is not set, so X11 hotkeys are unavailable.";
            return null;
        }
        if (!NativeLibrary.TryLoad("libX11.so.6", out _) && !NativeLibrary.TryLoad("libX11.so", out _))
        {
            reason = "libX11 was not found.";
            return null;
        }
        try
        {
            XInitThreads();
            var display = XOpenDisplay(0);
            if (display == 0)
            {
                reason = "XOpenDisplay failed.";
                return null;
            }
            reason = null;
            return new X11Session(display);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            reason = exception.Message;
            return null;
        }
    }

    public void SetHotkeys(IReadOnlyList<HotkeyBinding> bindings, Action<HotkeyBinding, bool> onHotkey, Action<IReadOnlyList<HotkeyBinding>> onFailed)
    {
        _commands.Enqueue(() => Apply(bindings, onHotkey, onFailed));
    }

    public void WatchForeground(Action<string?> onForeground)
    {
        _onForeground = onForeground;
        _commands.Enqueue(PublishForeground);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stop, 1) != 0) return;
        _thread.Join(TimeSpan.FromSeconds(2));
        try { XCloseDisplay(_display); }
        catch (DllNotFoundException) { }
    }

    private void Loop()
    {
        var lastForeground = Environment.TickCount64;
        while (Volatile.Read(ref _stop) == 0)
        {
            while (_commands.TryDequeue(out var command))
            {
                try { command(); }
                catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException) { }
            }
            while (Volatile.Read(ref _stop) == 0 && XPending(_display) > 0)
            {
                if (!ReadEvent(_event)) break;
                Handle(_event);
            }
            if (Environment.TickCount64 - lastForeground > 400)
            {
                lastForeground = Environment.TickCount64;
                PublishForeground();
            }
            Thread.Sleep(15);
        }
    }

    private void Apply(IReadOnlyList<HotkeyBinding> bindings, Action<HotkeyBinding, bool> onHotkey, Action<IReadOnlyList<HotkeyBinding>> onFailed)
    {
        _onHotkey = onHotkey;
        _handling = this;
        var previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(ErrorThunk));
        var grabbed = new List<GrabbedKey>();
        var failed = new List<HotkeyBinding>();
        try
        {
            XUngrabKey(_display, 0, (uint)AnyModifier, _root);
            foreach (var binding in bindings)
            {
                if (!VirtualKeyMap.TryX11Keysym(binding.VirtualKey, out var keysym))
                {
                    failed.Add(binding);
                    continue;
                }
                var keycode = XKeysymToKeycode(_display, keysym);
                if (keycode == 0)
                {
                    failed.Add(binding);
                    continue;
                }
                var accepted = true;
                foreach (var mask in VirtualKeyMap.X11GrabMasks(binding.Modifiers))
                {
                    _badAccess = 0;
                    XGrabKey(_display, keycode, mask, _root, false, GrabModeAsync, GrabModeAsync);
                    XSync(_display, false);
                    if (Volatile.Read(ref _badAccess) != 0) accepted = false;
                }
                if (!accepted) failed.Add(binding);
                else grabbed.Add(new GrabbedKey(keycode, VirtualKeyMap.X11Modifiers(binding.Modifiers), binding));
            }
        }
        finally
        {
            XSetErrorHandler(previous);
            _handling = null;
        }
        _keys = grabbed;
        onFailed(failed);
    }

    private void Handle(byte[] buffer)
    {
        var type = BitConverter.ToInt32(buffer, 0);
        if (type == PropertyNotify) { PublishForeground(); return; }
        if (type is not (KeyPress or KeyRelease)) return;
        var state = BitConverter.ToUInt32(buffer, StateOffset);
        var keycode = (byte)BitConverter.ToUInt32(buffer, KeycodeOffset);
        var time = BitConverter.ToUInt64(buffer, TimeOffset);
        if (type == KeyRelease && IsAutoRepeat(keycode, time)) return;
        var modifiers = state & ~(VirtualKeyMap.X11Lock | VirtualKeyMap.X11NumLock);
        foreach (var key in _keys)
        {
            if (key.Keycode != keycode || key.Modifiers != modifiers) continue;
            try { _onHotkey?.Invoke(key.Binding, type == KeyPress); }
            catch { /* L'abonné ne doit pas arrêter la boucle X11. */ }
            return;
        }
    }

    private bool IsAutoRepeat(byte keycode, ulong time)
    {
        if (XPending(_display) <= 0) return false;
        if (!PeekEvent(_peek)) return false;
        if (BitConverter.ToInt32(_peek, 0) != KeyPress) return false;
        if ((byte)BitConverter.ToUInt32(_peek, KeycodeOffset) != keycode) return false;
        if (BitConverter.ToUInt64(_peek, TimeOffset) != time) return false;
        ReadEvent(_event);
        return true;
    }

    private void PublishForeground()
    {
        var window = ReadCardinal(_root, _activeAtom);
        string? name = null;
        if (window != 0)
        {
            var pid = ReadCardinal((nuint)window, _pidAtom);
            if (pid is > 0 and <= int.MaxValue)
                name = ProcessNames.FromPid((int)pid);
        }
        if (string.Equals(name, _foreground, StringComparison.Ordinal)) return;
        _foreground = name;
        try { _onForeground?.Invoke(name); }
        catch { /* Isoler l'abonné. */ }
    }

    private ulong ReadCardinal(nuint window, nuint atom)
    {
        if (atom == 0) return 0;
        var status = XGetWindowProperty(_display, window, atom, 0, 1, false, 0, out _, out var format, out var count, out _, out var data);
        if (status != 0 || count == 0 || data == 0) return 0;
        try
        {
            return format == 32 ? (ulong)Marshal.ReadInt64(data) : (uint)Marshal.ReadInt32(data);
        }
        finally { XFree(data); }
    }

    private bool ReadEvent(byte[] buffer)
    {
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { XNextEvent(_display, pin.AddrOfPinnedObject()); return true; }
        finally { pin.Free(); }
    }

    private bool PeekEvent(byte[] buffer)
    {
        var pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try { XPeekEvent(_display, pin.AddrOfPinnedObject()); return true; }
        finally { pin.Free(); }
    }

    private static int OnNativeError(IntPtr display, IntPtr error)
    {
        var session = _handling;
        if (session is not null && Marshal.ReadByte(error, ErrorCodeOffset) == BadAccess)
            Volatile.Write(ref session._badAccess, 1);
        return 0;
    }

    private readonly record struct GrabbedKey(byte Keycode, uint Modifiers, HotkeyBinding Binding);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int XErrorHandler(IntPtr display, IntPtr error);

    [DllImport("libX11.so.6")] private static extern int XInitThreads();
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(IntPtr display);
    [DllImport("libX11.so.6")] private static extern nuint XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")] private static extern int XSelectInput(IntPtr display, nuint window, long mask);
    [DllImport("libX11.so.6")] private static extern int XPending(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XNextEvent(IntPtr display, IntPtr eventPointer);
    [DllImport("libX11.so.6")] private static extern int XPeekEvent(IntPtr display, IntPtr eventPointer);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);
    [DllImport("libX11.so.6")] private static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, nuint window, bool ownerEvents, int pointerMode, int keyboardMode);
    [DllImport("libX11.so.6")] private static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, nuint window);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern IntPtr XSetErrorHandler(IntPtr handler);
    [DllImport("libX11.so.6")] private static extern int XGetWindowProperty(IntPtr display, nuint window, nuint property, nint offset, nint length, bool delete, nuint type, out nuint actualType, out int actualFormat, out nuint itemCount, out nuint bytesAfter, out IntPtr data);
    [DllImport("libX11.so.6")] private static extern int XFree(IntPtr data);
}
