using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.Platform.Desktop.Hotkeys;

/// <summary>Raccourcis globaux macOS via RegisterEventHotKey (Carbon), y compris le relâchement pour le push-to-talk.</summary>
public sealed class MacHotkeyService : IHotkeyService, IHotkeyBackend
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint EventClassKeyboard = 0x6B657962;
    private const uint HotKeyPressed = 5;
    private const uint HotKeyReleased = 6;
    private const uint ParamDirectObject = 0x2D2D2D2D;
    private const uint TypeHotKeyId = 0x686B6964;
    private const uint Signature = 0x4D58444B;

    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly EventHandlerProc _handler;
    private readonly Thread _thread;
    private readonly List<IntPtr> _registrations = [];
    private HotkeyBinding[] _bindings = [];
    private int _stop;

    public MacHotkeyService()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Carbon hotkeys are only available on macOS.");
        IsSupported = true;
        _handler = OnEvent;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Mixdeck hotkeys" };
        _thread.Start();
    }

    public bool IsSupported { get; }
    public string? UnavailableReason => null;
    public IReadOnlyList<HotkeyBinding> Failed { get; private set; } = [];
    public event EventHandler<HotkeyEventArgs>? Triggered;

    public void Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        var copy = bindings.ToArray();
        _commands.Enqueue(() => Failed = Register(copy));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stop, 1) != 0) return;
        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    private void Loop()
    {
        var target = GetEventDispatcherTarget();
        EventTypeSpec[] types =
        [
            new(EventClassKeyboard, HotKeyPressed),
            new(EventClassKeyboard, HotKeyReleased),
        ];
        InstallEventHandler(target, _handler, types.Length, types, 0, out var handler);
        var mode = CFStringCreateWithCString(0, "kCFRunLoopDefaultMode", 0x08000100);
        _ready.Set();
        try
        {
            while (Volatile.Read(ref _stop) == 0)
            {
                while (_commands.TryDequeue(out var command)) command();
                CFRunLoopRunInMode(mode, 0.05, false);
            }
        }
        finally
        {
            foreach (var registration in _registrations) UnregisterEventHotKey(registration);
            if (handler != 0) RemoveEventHandler(handler);
            if (mode != 0) CFRelease(mode);
        }
    }

    private IReadOnlyList<HotkeyBinding> Register(IReadOnlyList<HotkeyBinding> bindings)
    {
        foreach (var registration in _registrations) UnregisterEventHotKey(registration);
        _registrations.Clear();
        _bindings = bindings.ToArray();
        var failed = new List<HotkeyBinding>();
        var target = GetEventDispatcherTarget();
        for (var i = 0; i < _bindings.Length; i++)
        {
            var binding = _bindings[i];
            if (!VirtualKeyMap.TryMacKeyCode(binding.VirtualKey, out var keyCode))
            {
                failed.Add(binding);
                continue;
            }
            var status = RegisterEventHotKey(keyCode, VirtualKeyMap.MacModifiers(binding.Modifiers),
                new EventHotKeyId(Signature, (uint)i), target, 0, out var reference);
            if (status != 0 || reference == 0) failed.Add(binding);
            else _registrations.Add(reference);
        }
        return failed;
    }

    private int OnEvent(IntPtr next, IntPtr eventRef, IntPtr userData)
    {
        var kind = GetEventKind(eventRef);
        if (GetEventParameter(eventRef, ParamDirectObject, TypeHotKeyId, 0, 8, 0, out var id) != 0) return 0;
        if (id.Signature != Signature || id.Id >= _bindings.Length) return 0;
        var pressed = kind == HotKeyPressed;
        if (kind is not (HotKeyPressed or HotKeyReleased)) return 0;
        try { Triggered?.Invoke(this, new HotkeyEventArgs(_bindings[id.Id], pressed)); }
        catch { /* Isoler l'abonné. */ }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EventTypeSpec(uint eventClass, uint eventKind)
    {
        public readonly uint EventClass = eventClass;
        public readonly uint EventKind = eventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct EventHotKeyId(uint signature, uint id)
    {
        public readonly uint Signature = signature;
        public readonly uint Id = id;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerProc(IntPtr next, IntPtr eventRef, IntPtr userData);

    [DllImport(Carbon)] private static extern IntPtr GetEventDispatcherTarget();
    [DllImport(Carbon)] private static extern int InstallEventHandler(IntPtr target, EventHandlerProc handler, int count, EventTypeSpec[] types, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] private static extern int RemoveEventHandler(IntPtr handler);
    [DllImport(Carbon)] private static extern uint GetEventKind(IntPtr eventRef);
    [DllImport(Carbon)] private static extern int GetEventParameter(IntPtr eventRef, uint name, uint desiredType, IntPtr actualType, nuint size, IntPtr actualSize, out EventHotKeyId data);
    [DllImport(Carbon)] private static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr hotKey);
    [DllImport(Carbon)] private static extern int UnregisterEventHotKey(IntPtr hotKey);
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern int CFRunLoopRunInMode(IntPtr mode, double seconds, bool returnAfterSource);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
