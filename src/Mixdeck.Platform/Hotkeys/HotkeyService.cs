using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Platform.Interop;

namespace Mixdeck.Platform.Hotkeys;

public sealed class HotkeyService : IHotkeyService
{
    private const uint PttMessage = 0x8002;
    private readonly ILogger<HotkeyService> _logger;
    private readonly MessageWindow _window;
    private readonly Native.KeyboardProc _callback;
    private readonly Dictionary<int, HotkeyBinding> _registered = [];
    private PttEntry[] _ptt = [];
    private IReadOnlyList<HotkeyBinding> _failed = Array.Empty<HotkeyBinding>();
    private nint _hook;
    private int _nextId;
    private int _disposed;

    private sealed class PttEntry(int id, HotkeyBinding binding)
    {
        internal readonly int Id = id;
        internal readonly HotkeyBinding Binding = binding;
        internal readonly PushToTalkState State = new(binding);
        internal bool Delivered;
    }

    public HotkeyService(ILogger<HotkeyService>? logger = null)
    {
        _logger = logger ?? NullLogger<HotkeyService>.Instance;
        _callback = KeyboardCallback;
        _window = new MessageWindow("Mixdeck raccourcis", OnMessage);
    }

    public event EventHandler<HotkeyEventArgs>? Triggered;
    public IReadOnlyList<HotkeyBinding> Failed => Volatile.Read(ref _failed);

    public void Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        var snapshot = bindings.ToArray();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _window.Invoke(() =>
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ApplyCore(snapshot);
        });
    }

    private void ApplyCore(HotkeyBinding[] bindings)
    {
        Clear();
        List<HotkeyBinding> failed = [];
        List<PttEntry> ptt = [];
        foreach (var binding in bindings)
        {
            if (!HotkeyFormatter.IsValidCombination(binding.Modifiers, binding.VirtualKey)) { Fail(binding, failed); continue; }
            int id = ++_nextId;
            if (binding.Action == HotkeyAction.PushToTalk) ptt.Add(new PttEntry(id, binding));
            else if (Native.RegisterHotKey(_window.Handle, id, (uint)binding.Modifiers | 0x4000, (uint)binding.VirtualKey)) _registered.Add(id, binding);
            else Fail(binding, failed);
        }
        _ptt = ptt.ToArray();
        if (_ptt.Length != 0)
        {
            _hook = Native.SetWindowsHookEx(13, Marshal.GetFunctionPointerForDelegate(_callback), Native.GetModuleHandle(null), 0);
            if (_hook == 0)
            {
                foreach (var entry in _ptt) Fail(entry.Binding, failed);
                _ptt = [];
            }
        }
        Volatile.Write(ref _failed, failed.AsReadOnly());
    }

    private void Fail(HotkeyBinding binding, List<HotkeyBinding> failed)
    {
        failed.Add(binding);
        _logger.LogWarning("Impossible d'enregistrer le raccourci {Hotkey} (erreur {Error}).", HotkeyFormatter.Format(binding), Marshal.GetLastPInvokeError());
    }

    private nint? OnMessage(uint message, nuint wParam, nint lParam)
    {
        if (message == 0x0312 && _registered.TryGetValue((int)wParam, out var binding)) Raise(binding, true);
        if (message == PttMessage)
        {
            foreach (var entry in _ptt)
            {
                if (entry.Id != (int)wParam) continue;
                entry.Delivered = lParam != 0;
                Raise(entry.Binding, entry.Delivered);
                break;
            }
        }
        return null;
    }

    private unsafe nint KeyboardCallback(int code, nuint wParam, nint lParam)
    {
        if (code >= 0 && (wParam is 0x100 or 0x101 or 0x104 or 0x105))
        {
            int key = (int)((Native.KeyboardData*)lParam)->VirtualKey;
            bool down = wParam is 0x100 or 0x104;
            var modifiers = CurrentModifiers(key, down);
            foreach (var entry in _ptt)
            {
                if (!entry.State.Update(key, down, modifiers)) continue;
                Native.PostMessage(_window.Handle, PttMessage, (nuint)entry.Id, entry.State.Pressed ? 1 : 0);
            }
        }
        return Native.CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static HotkeyModifiers CurrentModifiers(int key, bool down)
    {
        static bool IsDown(int vk, int changed, bool pressed) => vk == changed ? pressed : Native.GetAsyncKeyState(vk) < 0;
        HotkeyModifiers result = 0;
        if (IsDown(0xA2, key, down) || IsDown(0xA3, key, down)) result |= HotkeyModifiers.Control;
        if (IsDown(0xA4, key, down) || IsDown(0xA5, key, down)) result |= HotkeyModifiers.Alt;
        if (IsDown(0xA0, key, down) || IsDown(0xA1, key, down)) result |= HotkeyModifiers.Shift;
        if (IsDown(0x5B, key, down) || IsDown(0x5C, key, down)) result |= HotkeyModifiers.Win;
        return result;
    }

    private void Raise(HotkeyBinding binding, bool pressed)
    {
        try { Triggered?.Invoke(this, new HotkeyEventArgs(binding, pressed)); }
        catch (Exception ex) { _logger.LogError(ex, "Erreur du gestionnaire de raccourci."); }
    }

    private void Clear()
    {
        if (_hook != 0) { Native.UnhookWindowsHookEx(_hook); _hook = 0; }
        foreach (int id in _registered.Keys) Native.UnregisterHotKey(_window.Handle, id);
        _registered.Clear();
        var previous = _ptt;
        _ptt = [];
        foreach (var entry in previous) if (entry.Delivered) Raise(entry.Binding, false);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _window.Invoke(Clear); }
        finally { _window.Dispose(); }
        GC.KeepAlive(_callback);
    }
}
