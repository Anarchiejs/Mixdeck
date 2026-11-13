using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Desktop.Audio;

/// <summary>
/// Périphériques, volume et sourdine via Core Audio (API publique). macOS n'a pas d'équivalent
/// de ISimpleAudioVolume : le volume par application n'est pas exposé ici.
/// </summary>
public sealed class CoreAudioBackend : ISystemAudio
{
    private const string AudioLibrary = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const uint SystemDevice = 1;
    private const uint ElementMain = 0;

    private static readonly uint Devices = FourCC("dev#");
    private static readonly uint DefaultOutput = FourCC("dOut");
    private static readonly uint DefaultInput = FourCC("dIn ");
    private static readonly uint Name = FourCC("lnam");
    private static readonly uint Uid = FourCC("uid ");
    private static readonly uint Mute = FourCC("mute");
    private static readonly uint Volume = FourCC("volm");
    private static readonly uint Streams = FourCC("stm#");
    private static readonly uint VirtualVolume = FourCC("vmvc");
    private static readonly uint ScopeGlobal = FourCC("glob");
    private static readonly uint ScopeOutput = FourCC("outp");
    private static readonly uint ScopeInput = FourCC("inpt");

    private readonly object _gate = new();
    private Dictionary<string, uint> _objectIds = new(StringComparer.Ordinal);
    private readonly ListenerProc _listener;
    private CancellationTokenSource? _lifetime;
    private SystemAudioSnapshot _snapshot = SystemAudioSnapshot.Empty;
    private string? _reason;
    private bool _listening;

    public CoreAudioBackend() => _listener = OnProperty;

    public static ISystemAudio Create() => OperatingSystem.IsMacOS()
        ? new CoreAudioBackend()
        : new UnavailableSystemAudio("Core Audio is only available on macOS.");

    public bool IsAvailable { get; private set; }
    public string? UnavailableReason => _reason;
    public bool CanControlAppVolume => false;
    public bool CanRouteApps => false;
    public event EventHandler? Changed;
    public SystemAudioSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public IReadOnlyList<AppAudioSession> Sessions { get; } = [];

    public void Start()
    {
        if (!OperatingSystem.IsMacOS() || _lifetime is not null) return;
        Listen();
        _lifetime = new CancellationTokenSource();
        var token = _lifetime.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await RefreshAsync(token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                try { await Task.Delay(TimeSpan.FromSeconds(1.5), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }, token);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS()) return Task.CompletedTask;
        try
        {
            var outputs = new List<SystemAudioDevice>();
            var inputs = new List<SystemAudioDevice>();
            var ids = new Dictionary<string, uint>(StringComparer.Ordinal);
            foreach (var device in DeviceIds())
            {
                var key = DeviceKey(device);
                ids[key] = device;
                var label = ReadString(device, Name, ScopeGlobal) ?? key;
                if (HasStreams(device, ScopeOutput))
                    outputs.Add(new SystemAudioDevice(key, label, SystemAudioFlow.Output, ReadVolume(device, ScopeOutput), ReadMute(device, ScopeOutput)));
                if (HasStreams(device, ScopeInput))
                    inputs.Add(new SystemAudioDevice(key, label, SystemAudioFlow.Input, ReadVolume(device, ScopeInput), ReadMute(device, ScopeInput)));
            }
            var defaultOutput = KeyFor(ReadUInt(SystemDevice, DefaultOutput, ScopeGlobal), ids);
            var defaultInput = KeyFor(ReadUInt(SystemDevice, DefaultInput, ScopeGlobal), ids);
            var snapshot = new SystemAudioSnapshot(outputs, inputs, defaultOutput, defaultInput);
            var changed = false;
            lock (_gate)
            {
                _objectIds = ids;
                changed = !Same(_snapshot, snapshot);
                _snapshot = snapshot;
                IsAvailable = true;
                _reason = null;
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _reason = exception is DllNotFoundException or EntryPointNotFoundException
                ? "Core Audio is not available on this Mac."
                : "Core Audio could not be read on this Mac.";
            IsAvailable = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return Task.CompletedTask;
    }

    public Task<bool> SetDefaultAsync(SystemAudioFlow flow, string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(WithDevice(id, device =>
        {
            var selector = flow == SystemAudioFlow.Output ? DefaultOutput : DefaultInput;
            return WriteUInt(SystemDevice, selector, ScopeGlobal, ElementMain, device);
        }));

    public Task<bool> SetDeviceVolumeAsync(SystemAudioFlow flow, string id, float volume, CancellationToken cancellationToken = default)
    {
        var scope = flow == SystemAudioFlow.Output ? ScopeOutput : ScopeInput;
        var clamped = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0;
        var wrote = WithDevice(id, device => WriteVolume(device, scope, clamped));
        if (wrote) RefreshAsync(cancellationToken);
        return Task.FromResult(wrote);
    }

    public Task<bool> SetDeviceMuteAsync(SystemAudioFlow flow, string id, bool mute, CancellationToken cancellationToken = default)
    {
        var scope = flow == SystemAudioFlow.Output ? ScopeOutput : ScopeInput;
        var wrote = WithDevice(id, device => WriteUInt(device, Mute, scope, ElementMain, mute ? 1u : 0u));
        if (wrote) RefreshAsync(cancellationToken);
        return Task.FromResult(wrote);
    }

    public Task<bool> SetSessionVolumeAsync(string sessionId, float volume, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> SetSessionMuteAsync(string sessionId, bool mute, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task<bool> MoveProcessAsync(int processId, string? outputId, CancellationToken cancellationToken = default) => Task.FromResult(false);

    public void Dispose()
    {
        Unlisten();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
    }

    private void Listen()
    {
        if (_listening) return;
        foreach (var selector in new[] { Devices, DefaultOutput, DefaultInput })
        {
            var address = new AudioObjectPropertyAddress(selector, ScopeGlobal, ElementMain);
            AudioObjectAddPropertyListener(SystemDevice, ref address, _listener, 0);
        }
        _listening = true;
    }

    private void Unlisten()
    {
        if (!_listening) return;
        foreach (var selector in new[] { Devices, DefaultOutput, DefaultInput })
        {
            var address = new AudioObjectPropertyAddress(selector, ScopeGlobal, ElementMain);
            AudioObjectRemovePropertyListener(SystemDevice, ref address, _listener, 0);
        }
        _listening = false;
    }

    private int OnProperty(uint objectId, uint number, IntPtr addresses, IntPtr client)
    {
        try { RefreshAsync(); }
        catch (Exception) { /* Le sondage reprendra la lecture. */ }
        return 0;
    }

    private bool WithDevice(string id, Func<uint, bool> action)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        uint device;
        lock (_gate)
        {
            if (!_objectIds.TryGetValue(id, out device)) return false;
        }
        try { return action(device); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException) { return false; }
    }

    private static IEnumerable<uint> DeviceIds()
    {
        var address = Address(Devices, ScopeGlobal, ElementMain);
        if (AudioObjectGetPropertyDataSize(SystemDevice, ref address, 0, 0, out var size) != 0 || size < 4) yield break;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (AudioObjectGetPropertyData(SystemDevice, ref address, 0, 0, ref size, buffer) != 0) yield break;
            var count = (int)(size / 4);
            for (var i = 0; i < count; i++) yield return (uint)Marshal.ReadInt32(buffer, i * 4);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string DeviceKey(uint device) => ReadString(device, Uid, ScopeGlobal)
        ?? device.ToString(CultureInfo.InvariantCulture);

    private static bool HasStreams(uint device, uint scope)
    {
        var address = Address(Streams, scope, ElementMain);
        if (!HasProperty(device, ref address)) return false;
        return AudioObjectGetPropertyDataSize(device, ref address, 0, 0, out var size) == 0 && size > 0;
    }

    private static float ReadVolume(uint device, uint scope)
    {
        foreach (var (selector, element) in VolumeElements())
        {
            if (TryGetFloat(device, selector, scope, element, out var value)) return Math.Clamp(value, 0, 1);
        }
        return 1;
    }

    private static bool WriteVolume(uint device, uint scope, float volume)
    {
        foreach (var (selector, element) in VolumeElements())
        {
            if (TrySetFloat(device, selector, scope, element, volume)) return true;
        }
        return false;
    }

    private static IEnumerable<(uint Selector, uint Element)> VolumeElements()
    {
        yield return (Volume, ElementMain);
        yield return (Volume, 1);
        yield return (VirtualVolume, ElementMain);
    }

    private static bool ReadMute(uint device, uint scope) =>
        TryGetUInt(device, Mute, scope, ElementMain, out var value) && value != 0;

    private static uint? ReadUInt(uint device, uint selector, uint scope) =>
        TryGetUInt(device, selector, scope, ElementMain, out var value) ? value : null;

    private static string? KeyFor(uint? device, Dictionary<string, uint> ids)
    {
        if (device is not uint id) return null;
        foreach (var pair in ids)
            if (pair.Value == id) return pair.Key;
        return null;
    }

    private static bool TryGetUInt(uint device, uint selector, uint scope, uint element, out uint value)
    {
        var address = Address(selector, scope, element);
        value = 0;
        if (!HasProperty(device, ref address)) return false;
        uint size = 4;
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            if (AudioObjectGetPropertyData(device, ref address, 0, 0, ref size, buffer) != 0) return false;
            value = (uint)Marshal.ReadInt32(buffer);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool TryGetFloat(uint device, uint selector, uint scope, uint element, out float value)
    {
        var address = Address(selector, scope, element);
        value = 0;
        if (!HasProperty(device, ref address)) return false;
        uint size = 4;
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            if (AudioObjectGetPropertyData(device, ref address, 0, 0, ref size, buffer) != 0) return false;
            var bytes = new byte[4];
            Marshal.Copy(buffer, bytes, 0, 4);
            value = BitConverter.ToSingle(bytes, 0);
            return float.IsFinite(value);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool TrySetFloat(uint device, uint selector, uint scope, uint element, float value)
    {
        var address = Address(selector, scope, element);
        if (!HasProperty(device, ref address)) return false;
        var bytes = BitConverter.GetBytes(value);
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.Copy(bytes, 0, buffer, 4);
            return AudioObjectSetPropertyData(device, ref address, 0, 0, 4, buffer) == 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool WriteUInt(uint device, uint selector, uint scope, uint element, uint value)
    {
        var address = Address(selector, scope, element);
        if (!HasProperty(device, ref address) && device != SystemDevice) return false;
        var buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, (int)value);
            return AudioObjectSetPropertyData(device, ref address, 0, 0, 4, buffer) == 0;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? ReadString(uint device, uint selector, uint scope)
    {
        var address = Address(selector, scope, ElementMain);
        if (!HasProperty(device, ref address)) return null;
        uint size = (uint)IntPtr.Size;
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            if (AudioObjectGetPropertyData(device, ref address, 0, 0, ref size, buffer) != 0) return null;
            var cf = Marshal.ReadIntPtr(buffer);
            if (cf == 0) return null;
            try { return CfString(cf); }
            finally { CFRelease(cf); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static string? CfString(IntPtr value)
    {
        var length = CFStringGetLength(value);
        var buffer = new byte[Math.Max(8, (length + 1) * 4)];
        if (!CFStringGetCString(value, buffer, buffer.Length, Utf8)) return null;
        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    private static bool Same(SystemAudioSnapshot left, SystemAudioSnapshot right) =>
        left.DefaultOutputId == right.DefaultOutputId
        && left.DefaultInputId == right.DefaultInputId
        && SameDevices(left.Outputs, right.Outputs)
        && SameDevices(left.Inputs, right.Inputs);

    private static bool SameDevices(IReadOnlyList<SystemAudioDevice> left, IReadOnlyList<SystemAudioDevice> right)
    {
        if (left.Count != right.Count) return false;
        for (var i = 0; i < left.Count; i++)
        {
            if (left[i] != right[i]) return false;
        }
        return true;
    }

    private static bool HasProperty(uint device, ref AudioObjectPropertyAddress address) =>
        AudioObjectHasProperty(device, ref address) != 0;

    private static AudioObjectPropertyAddress Address(uint selector, uint scope, uint element) => new(selector, scope, element);

    private static uint FourCC(string value)
    {
        var bytes = new byte[4];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = i < value.Length ? (byte)value[i] : (byte)' ';
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct AudioObjectPropertyAddress(uint selector, uint scope, uint element)
    {
        public readonly uint Selector = selector;
        public readonly uint Scope = scope;
        public readonly uint Element = element;
    }

    [DllImport(AudioLibrary)] private static extern byte AudioObjectHasProperty(uint objectId, ref AudioObjectPropertyAddress address);
    [DllImport(AudioLibrary)] private static extern int AudioObjectGetPropertyDataSize(uint objectId, ref AudioObjectPropertyAddress address, uint qualifierSize, IntPtr qualifier, out uint dataSize);
    [DllImport(AudioLibrary)] private static extern int AudioObjectGetPropertyData(uint objectId, ref AudioObjectPropertyAddress address, uint qualifierSize, IntPtr qualifier, ref uint dataSize, IntPtr data);
    [DllImport(AudioLibrary)] private static extern int AudioObjectSetPropertyData(uint objectId, ref AudioObjectPropertyAddress address, uint qualifierSize, IntPtr qualifier, uint dataSize, IntPtr data);
    [DllImport(AudioLibrary)] private static extern int AudioObjectAddPropertyListener(uint objectId, ref AudioObjectPropertyAddress address, ListenerProc listener, IntPtr client);
    [DllImport(AudioLibrary)] private static extern int AudioObjectRemovePropertyListener(uint objectId, ref AudioObjectPropertyAddress address, ListenerProc listener, IntPtr client);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ListenerProc(uint objectId, uint number, IntPtr addresses, IntPtr client);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(IntPtr value);
    [DllImport(CoreFoundation)] private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint capacity, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
