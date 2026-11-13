using System.Runtime.InteropServices;

namespace Mixdeck.FlStudio;

/// <summary>Accès minimal à l'API MIDI de Windows (winmm) : lister les ports, envoyer et recevoir du SysEx.</summary>
public sealed class WinMidiPorts : IMidiPorts
{
    private const uint MhdrDone = 1;
    private const uint CallbackEvent = 0x50000;
    private const int BufferSize = 4096;

    public IReadOnlyList<string> OutputNames() => Outputs().Select(p => p.Name).ToArray();
    public IReadOnlyList<string> InputNames() => Inputs().Select(p => p.Name).ToArray();

    private static List<(uint Id, string Name)> Outputs()
    {
        var names = new List<(uint, string)>();
        for (uint i = 0; i < midiOutGetNumDevs(); i++)
            if (midiOutGetDevCapsW(i, out var caps, (uint)Marshal.SizeOf<MidiOutCaps>()) == 0) names.Add((i, caps.Name));
        return names;
    }

    private static List<(uint Id, string Name)> Inputs()
    {
        var names = new List<(uint, string)>();
        for (uint i = 0; i < midiInGetNumDevs(); i++)
            if (midiInGetDevCapsW(i, out var caps, (uint)Marshal.SizeOf<MidiInCaps>()) == 0) names.Add((i, caps.Name));
        return names;
    }

    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<MidiHdr>();
    private static uint Find(List<(uint Id, string Name)> ports, string name)
    {
        var matches = ports.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 1) return matches[0].Id;
        throw new MidiPortException(matches.Length == 0
            ? $"Port MIDI introuvable : {name}"
            : $"Plusieurs ports MIDI portent le nom « {name} ».");
    }
    public IMidiOutput OpenOutput(string name) => new Output(Find(Outputs(), name));
    public IMidiInput OpenInput(string name, Action<byte[]> onSysex) => new Input(Find(Inputs(), name), onSysex);

    private sealed class Output : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly AutoResetEvent _sent = new(false);
        private nint _handle;
        public Output(uint port) => Check(midiOutOpen(out _handle, port, _sent.SafeWaitHandle.DangerousGetHandle(), 0, CallbackEvent), "midiOutOpen");
        public void SendSysex(byte[] message)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_handle == 0, this);
                var header = NewHeader(message);
                var prepared = false;
                try
                {
                    Check(midiOutPrepareHeader(_handle, header, HeaderSize), "midiOutPrepareHeader");
                    prepared = true;
                    _sent.Reset();
                    Check(midiOutLongMsg(_handle, header, HeaderSize), "midiOutLongMsg");
                    if (!_sent.WaitOne(1000))
                    {
                        Check(midiOutReset(_handle), "midiOutReset");
                        throw new MidiPortException("Délai d'envoi MIDI dépassé (1 s).");
                    }
                }
                finally
                {
                    // Ne jamais libérer un tampon encore possédé par le pilote.
                    if (!prepared || midiOutUnprepareHeader(_handle, header, HeaderSize) == 0) FreeHeader(header);
                }
            }
        }
        public void Dispose()
        {
            lock (_gate)
            {
                if (_handle == 0) return;
                midiOutReset(_handle);
                midiOutClose(_handle);
                _handle = 0;
                _sent.Dispose();
            }
        }
    }

    private sealed class Input : IMidiInput
    {
        private nint _handle;
        private readonly List<nint> _headers = [];
        private readonly Action<byte[]> _receive;
        private readonly Thread _thread;
        private volatile bool _stopping;
        // Signalé par le pilote à chaque tampon rempli : pas d'interrogation en boucle au repos.
        private readonly AutoResetEvent _signal = new(false);
        private readonly SysexAssembler _assembler = new();
        public Input(uint port, Action<byte[]> receive)
        {
            _receive = receive;
            _thread = new Thread(Run) { IsBackground = true, Name = "Mixdeck MIDI" };
            Check(midiInOpen(out _handle, port, _signal.SafeWaitHandle.DangerousGetHandle(), 0, CallbackEvent), "midiInOpen");
            try
            {
                for (var i = 0; i < 4; i++)
                {
                    var header = NewHeader(new byte[BufferSize]);
                    _headers.Add(header);
                    Check(midiInPrepareHeader(_handle, header, HeaderSize), "midiInPrepareHeader");
                    Check(midiInAddBuffer(_handle, header, HeaderSize), "midiInAddBuffer");
                }
                Check(midiInStart(_handle), "midiInStart");
                _thread.Start();
            }
            catch { Close(); _signal.Dispose(); throw; }
        }
        private void Run()
        {
            try
            {
                while (!_stopping)
                {
                    // L'ordre de remise en file est celui des tampons donnés au pilote.
                    var header = _headers[0];
                    if ((Flags(header) & MhdrDone) == 0) { _signal.WaitOne(250); continue; }
                    var hdr = Marshal.PtrToStructure<MidiHdr>(header);
                    var data = new byte[hdr.BytesRecorded];
                    Marshal.Copy(hdr.Data, data, 0, data.Length);
                    Marshal.WriteInt32(header, (int)Marshal.OffsetOf<MidiHdr>(nameof(MidiHdr.BytesRecorded)), 0);
                    Check(midiInAddBuffer(_handle, header, HeaderSize), "midiInAddBuffer");
                    _headers.RemoveAt(0);
                    _headers.Add(header);
                    foreach (var message in _assembler.Push(data))
                    {
                        try { _receive(message); } catch { /* Isoler les abonnés. */ }
                    }
                }
            }
            catch (MidiPortException) { /* La perte sera détectée par les pings du client. */ }
            finally { Close(); }
        }
        private void Close()
        {
            if (_handle == 0) return;
            midiInStop(_handle);
            midiInReset(_handle);
            foreach (var header in _headers)
                if (midiInUnprepareHeader(_handle, header, HeaderSize) == 0) FreeHeader(header);
            _headers.Clear();
            midiInClose(_handle);
            _handle = 0;
        }
        public void Dispose()
        {
            _stopping = true;
            _signal.Set();
            if (Thread.CurrentThread != _thread && _thread.IsAlive) _thread.Join();
            _signal.Dispose();
        }
    }
    private static nint NewHeader(byte[] data)
    {
        var buffer = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, buffer, data.Length);
        var header = Marshal.AllocHGlobal(Marshal.SizeOf<MidiHdr>());
        Marshal.StructureToPtr(new MidiHdr { Data = buffer, BufferLength = (uint)data.Length, Reserved2 = new nint[8] }, header, false);
        return header;
    }

    private static void FreeHeader(nint header)
    {
        Marshal.FreeHGlobal(Marshal.PtrToStructure<MidiHdr>(header).Data);
        Marshal.FreeHGlobal(header);
    }

    private static uint Flags(nint header) =>
        (uint)Marshal.ReadInt32(header, (int)Marshal.OffsetOf<MidiHdr>(nameof(MidiHdr.Flags)));

    private static void Check(uint result, string call)
    {
        if (result != 0) throw new MidiPortException($"{call} a échoué (code MMSYSERR {result}). Le port est peut-être ouvert ailleurs.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MidiHdr
    {
        public nint Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public nint User;
        public uint Flags;
        public nint Next;
        public nint Reserved;
        public uint Offset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public nint[] Reserved2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiOutCaps
    {
        public ushort Mid, Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public ushort Technology, Voices, Notes, ChannelMask;
        public uint Support;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MidiInCaps
    {
        public ushort Mid, Pid;
        public uint DriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Support;
    }

    [DllImport("winmm.dll")] private static extern uint midiOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint midiOutGetDevCapsW(nuint id, out MidiOutCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutOpen(out nint handle, uint id, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiOutPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutLongMsg(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiOutReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiOutClose(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint midiInGetDevCapsW(nuint id, out MidiInCaps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInOpen(out nint handle, uint id, nint callback, nint instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint midiInPrepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInAddBuffer(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInUnprepareHeader(nint handle, nint header, uint size);
    [DllImport("winmm.dll")] private static extern uint midiInStart(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInStop(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInReset(nint handle);
    [DllImport("winmm.dll")] private static extern uint midiInClose(nint handle);
}
