using System.Runtime.InteropServices;
using System.Text;

namespace Mixdeck.FlStudio;

/// <summary>
/// Ports MIDI macOS (CoreMIDI). Un port IAC déjà nommé est réutilisé. Sinon Mixdeck crée une source virtuelle
/// (« Mixdeck to FL », visible comme entrée dans FL) et une destination virtuelle (« FL to Mixdeck », visible comme sortie).
/// </summary>
public sealed class CoreMidiPorts : IMidiPorts, IMidiAvailability
{
    private const uint Utf8 = 0x08000100;
    private const string MidiLibrary = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private readonly object _gate = new();
    private uint _client;
    private IntPtr _displayName;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public IReadOnlyList<string> OutputNames() => Names(destinations: true);

    public IReadOnlyList<string> InputNames() => Names(destinations: false);

    public IMidiOutput OpenOutput(string name)
    {
        RequireMac();
        var destination = Find(destinations: true, name);
        if (destination != 0)
        {
            EnsureOutputPort();
            return new DestinationOutput(destination);
        }
        var source = Find(destinations: false, name);
        if (source == 0) source = CreateSource(name);
        return new SourceOutput(source);
    }

    public IMidiInput OpenInput(string name, Action<byte[]> onSysex)
    {
        RequireMac();
        var source = Find(destinations: false, name);
        if (source != 0)
        {
            var input = new SourceInput(EnsureInputPort(), source, onSysex);
            Check(MIDIPortConnectSource(input.Port, source, (IntPtr)input.Handle), "MIDIPortConnectSource");
            return input;
        }
        if (Find(destinations: true, name) != 0)
            throw new MidiPortException($"Le port « {name} » est une destination : Mixdeck ne peut pas y lire.");
        return CreateDestination(name, onSysex);
    }

    private static void RequireMac()
    {
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Core MIDI is only available on macOS.");
    }

    private IReadOnlyList<string> Names(bool destinations)
    {
        if (!OperatingSystem.IsMacOS()) return [];
        lock (_gate)
        {
            var count = destinations ? MIDIGetNumberOfDestinations() : MIDIGetNumberOfSources();
            var names = new List<string>(count);
            for (var i = 0; i < count; i++)
            {
                var endpoint = destinations ? MIDIGetDestination(i) : MIDIGetSource(i);
                var endpointName = NameOf(endpoint);
                if (!string.IsNullOrWhiteSpace(endpointName)) names.Add(endpointName);
            }
            return names;
        }
    }

    private uint Find(bool destinations, string name)
    {
        lock (_gate)
        {
            var count = destinations ? MIDIGetNumberOfDestinations() : MIDIGetNumberOfSources();
            uint partial = 0;
            for (var i = 0; i < count; i++)
            {
                var endpoint = destinations ? MIDIGetDestination(i) : MIDIGetSource(i);
                var endpointName = NameOf(endpoint);
                if (string.IsNullOrEmpty(endpointName)) continue;
                if (endpointName.Equals(name, StringComparison.OrdinalIgnoreCase)) return endpoint;
                if (partial == 0 && endpointName.Contains(name, StringComparison.OrdinalIgnoreCase)) partial = endpoint;
            }
            return partial;
        }
    }

    private uint CreateSource(string name)
    {
        var label = StringRef(name);
        try
        {
            Check(MIDISourceCreate(Client(), label, out var endpoint), "MIDISourceCreate");
            return endpoint;
        }
        finally { CFRelease(label); }
    }

    private DestinationInput CreateDestination(string name, Action<byte[]> onSysex)
    {
        var input = new DestinationInput(onSysex);
        var label = StringRef(name);
        try
        {
            Check(MIDIDestinationCreate(Client(), label, ReadCallback, (IntPtr)input.Handle, out _), "MIDIDestinationCreate");
            return input;
        }
        finally { CFRelease(label); }
    }

    private uint EnsureOutputPort()
    {
        lock (_gate)
        {
            if (_outputPort != 0) return _outputPort;
            var label = StringRef("Mixdeck output");
            try
            {
                Check(MIDIOutputPortCreate(Client(), label, out _outputPort), "MIDIOutputPortCreate");
                return _outputPort;
            }
            finally { CFRelease(label); }
        }
    }

    private uint _outputPort;
    private uint _inputPort;

    private uint EnsureInputPort()
    {
        lock (_gate)
        {
            if (_inputPort != 0) return _inputPort;
            var label = StringRef("Mixdeck input");
            try
            {
                Check(MIDIInputPortCreate(Client(), label, ReadCallback, 0, out _inputPort), "MIDIInputPortCreate");
                return _inputPort;
            }
            finally { CFRelease(label); }
        }
    }

    private uint Client()
    {
        lock (_gate)
        {
            if (_client != 0) return _client;
            var label = StringRef("Mixdeck");
            try
            {
                Check(MIDIClientCreate(label, 0, 0, out _client), "MIDIClientCreate");
                return _client;
            }
            finally { CFRelease(label); }
        }
    }

    private string? NameOf(uint endpoint)
    {
        if (endpoint == 0) return null;
        var property = DisplayName();
        if (MIDIObjectGetStringProperty(endpoint, property, out var value) != 0 || value == 0) return null;
        try { return ReadString(value); }
        finally { CFRelease(value); }
    }

    private IntPtr DisplayName()
    {
        if (_displayName != 0) return _displayName;
        var library = NativeLibrary.Load(MidiLibrary);
        _displayName = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kMIDIPropertyDisplayName"));
        return _displayName;
    }

    private static void Check(int status, string call)
    {
        if (status != 0) throw new MidiPortException($"{call} a échoué (OSStatus {status}).");
    }

    private static IntPtr StringRef(string value)
    {
        var created = CFStringCreateWithCString(0, value, Utf8);
        if (created == 0) throw new MidiPortException("CFStringCreateWithCString a échoué.");
        return created;
    }

    private static string? ReadString(IntPtr value)
    {
        var length = CFStringGetLength(value);
        var buffer = new byte[Math.Max(8, (length + 1) * 4)];
        if (!CFStringGetCString(value, buffer, buffer.Length, Utf8)) return null;
        var end = Array.IndexOf(buffer, (byte)0);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
    }

    private static readonly MidiReadProc ReadCallback = OnRead;

    private static void OnRead(IntPtr packetList, IntPtr refCon, IntPtr connectionRefCon)
    {
        var which = connectionRefCon != 0 ? connectionRefCon : refCon;
        if (which == 0) return;
        var handle = GCHandle.FromIntPtr(which);
        if (handle.Target is IPacketReceiver receiver) receiver.Receive(packetList);
    }

    private interface IPacketReceiver
    {
        void Receive(IntPtr packetList);
    }

    private sealed class DestinationOutput(uint destination) : IMidiOutput
    {
        private static readonly MidiCompletion Completion = SysexSendLease.OnComplete;
        private static readonly IntPtr CompletionPointer = Marshal.GetFunctionPointerForDelegate(Completion);

        public void SendSysex(byte[] message)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var pending = SysexSendLease.Allocate(destination, message, CompletionPointer);
            var accepted = false;
            try
            {
                Check(MIDISendSysex(pending.Request), "MIDISendSysex");
                accepted = true;
                if (!pending.Wait(TimeSpan.FromSeconds(1)))
                {
                    pending.RequestAbort();
                    if (!pending.Wait(TimeSpan.FromMilliseconds(500)))
                        throw new MidiPortException($"Délai d'envoi MIDI dépassé (1 s). Envois encore ouverts : {SysexSendLease.OpenCount}.");
                }
            }
            finally
            {
                if (accepted) pending.WaiterFinished();
                else pending.ReleaseBecauseNotSent();
            }
        }

        public void Dispose() => Volatile.Write(ref _disposed, 1);

        private int _disposed;
    }

    private sealed class SourceOutput(uint source) : IMidiOutput
    {
        public void SendSysex(byte[] message)
        {
            var size = message.Length + 64;
            var list = Marshal.AllocHGlobal(size);
            var pinned = GCHandle.Alloc(message, GCHandleType.Pinned);
            try
            {
                var packet = MIDIPacketListInit(list);
                if (MIDIPacketListAdd(list, (nuint)size, packet, 0, (nuint)message.Length, pinned.AddrOfPinnedObject()) == 0)
                    throw new MidiPortException("MIDIPacketListAdd a échoué.");
                Check(MIDIReceived(source, list), "MIDIReceived");
            }
            finally
            {
                pinned.Free();
                Marshal.FreeHGlobal(list);
            }
        }

        public void Dispose() { }
    }

    private sealed class SourceInput : IMidiInput, IPacketReceiver
    {
        private readonly SysexAssembler _assembler = new();
        private readonly Action<byte[]> _onSysex;
        private readonly uint _source;
        internal readonly uint Port;
        internal readonly GCHandle Handle;

        public SourceInput(uint port, uint source, Action<byte[]> onSysex)
        {
            Port = port;
            _source = source;
            _onSysex = onSysex;
            Handle = GCHandle.Alloc(this);
        }

        public void Receive(IntPtr packetList)
        {
            foreach (var packet in MidiPacketListReader.Read(packetList))
            {
                foreach (var message in _assembler.Push(packet))
                {
                    try { _onSysex(message); }
                    catch { /* Un abonné ne doit pas arrêter le port. */ }
                }
            }
        }

        public void Dispose()
        {
            if (Port != 0 && _source != 0)
            {
                try { MIDIPortDisconnectSource(Port, _source); }
                catch (DllNotFoundException) { /* Le processus s'arrête. */ }
            }
            if (Handle.IsAllocated) Handle.Free();
        }
    }

    /// <summary>La destination virtuelle appelle déjà le callback passé à MIDIDestinationCreate. Cet objet ne fait que garder l'assembleur.</summary>
    private sealed class DestinationInput : IMidiInput, IPacketReceiver
    {
        private readonly SysexAssembler _assembler = new();
        private readonly Action<byte[]> _onSysex;
        internal readonly GCHandle Handle;

        public DestinationInput(Action<byte[]> onSysex)
        {
            _onSysex = onSysex;
            Handle = GCHandle.Alloc(this);
        }

        public void Receive(IntPtr packetList)
        {
            foreach (var packet in MidiPacketListReader.Read(packetList))
            {
                foreach (var message in _assembler.Push(packet))
                {
                    try { _onSysex(message); }
                    catch { /* Isoler les abonnés. */ }
                }
            }
        }

        public void Dispose()
        {
            if (Handle.IsAllocated) Handle.Free();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MidiReadProc(IntPtr packetList, IntPtr refCon, IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void MidiCompletion(IntPtr request);

    [DllImport(MidiLibrary)] private static extern int MIDIGetNumberOfDestinations();
    [DllImport(MidiLibrary)] private static extern int MIDIGetNumberOfSources();
    [DllImport(MidiLibrary)] private static extern uint MIDIGetDestination(int index);
    [DllImport(MidiLibrary)] private static extern uint MIDIGetSource(int index);
    [DllImport(MidiLibrary)] private static extern int MIDIClientCreate(IntPtr name, IntPtr notify, IntPtr refCon, out uint client);
    [DllImport(MidiLibrary)] private static extern int MIDIOutputPortCreate(uint client, IntPtr name, out uint port);
    [DllImport(MidiLibrary)] private static extern int MIDIInputPortCreate(uint client, IntPtr name, MidiReadProc readProc, IntPtr refCon, out uint port);
    [DllImport(MidiLibrary)] private static extern int MIDIPortConnectSource(uint port, uint source, IntPtr refCon);
    [DllImport(MidiLibrary)] private static extern int MIDIPortDisconnectSource(uint port, uint source);
    [DllImport(MidiLibrary)] private static extern int MIDISourceCreate(uint client, IntPtr name, out uint endpoint);
    [DllImport(MidiLibrary)] private static extern int MIDIDestinationCreate(uint client, IntPtr name, MidiReadProc readProc, IntPtr refCon, out uint endpoint);
    [DllImport(MidiLibrary)] private static extern int MIDIReceived(uint source, IntPtr packetList);
    [DllImport(MidiLibrary)] private static extern int MIDISendSysex(IntPtr request);
    [DllImport(MidiLibrary)] private static extern IntPtr MIDIPacketListInit(IntPtr packetList);
    [DllImport(MidiLibrary)] private static extern IntPtr MIDIPacketListAdd(IntPtr packetList, nuint listSize, IntPtr currentPacket, ulong timeStamp, nuint dataCount, IntPtr data);
    [DllImport(MidiLibrary)] private static extern int MIDIObjectGetStringProperty(uint midiObject, IntPtr property, out IntPtr stringValue);

    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern nint CFStringGetLength(IntPtr value);
    [DllImport(CoreFoundation)] private static extern bool CFStringGetCString(IntPtr value, byte[] buffer, nint capacity, uint encoding);
    [DllImport(CoreFoundation)] private static extern void CFRelease(IntPtr value);
}
