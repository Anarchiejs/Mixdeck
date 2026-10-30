using System.Collections.Concurrent;
using System.Text;
using Mixdeck.Core.Model;

namespace Mixdeck.FlStudio;

/// <summary>Script FL en mémoire : mêmes trames que device_Mixdeck.py, sans accès aux ports Windows.</summary>
public sealed class SimulatedFlStudio : IMidiPorts
{
    private readonly object _gate = new();
    private readonly Dictionary<int, DawTrack> _tracks = new()
    {
        [0] = new(0, "Master", .8f, false, true, []),
        [1] = new(1, "Voix", .8f, false, true, [new(0, true), new(1, true), new(2, false)]),
        [2] = new(2, "Musique", .65f, false, true, []),
        [3] = new(3, "FX robot", .8f, false, true, [new(0, true)]),
    };
    private readonly HashSet<int> _watched = [];
    private readonly List<byte[]> _sent = [];
    private Input? _input;
    private int _selected;
    private volatile bool _portsAvailable = true, _respond = true;

    public bool PortsAvailable { get => _portsAvailable; set => _portsAvailable = value; }
    public bool Respond { get => _respond; set => _respond = value; }
    public IReadOnlyList<byte[]> SentMessages { get { lock (_gate) return _sent.Select(m => m.ToArray()).ToArray(); } }
    public IReadOnlyDictionary<(int Track, int Slot), string> SlotNames { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<(int, int), string>(new Dictionary<(int, int), string>
        {
            [(1, 0)] = "Pro-Q", [(1, 1)] = "Compresseur", [(1, 2)] = "Pitcher", [(3, 0)] = "Robot",
        });
    public IReadOnlyList<string> OutputNames() => PortsAvailable ? ["Mixdeck to FL"] : [];
    public IReadOnlyList<string> InputNames() => PortsAvailable ? ["FL to Mixdeck"] : [];
    private static void RequirePort(IReadOnlyList<string> ports, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || !ports.Any(p => p.Contains(name, StringComparison.OrdinalIgnoreCase)))
            throw new MidiPortException($"Port MIDI introuvable : {name}");
    }
    public IMidiOutput OpenOutput(string name)
    {
        RequirePort(OutputNames(), name);
        return new Output(this);
    }
    public IMidiInput OpenInput(string name, Action<byte[]> onSysex)
    {
        RequirePort(InputNames(), name);
        lock (_gate)
        {
            if (_input is { IsDisposed: false }) throw new MidiPortException("Entrée simulée déjà ouverte.");
            return _input = new Input(onSysex);
        }
    }

    /// <summary>Simule une modification faite dans le mixeur FL sur une piste suivie.</summary>
    public void ChangeTrack(DawTrack track)
    {
        lock (_gate)
        {
            _tracks[track.Index] = track with { Slots = track.Slots.ToArray() };
            if (_watched.Contains(track.Index)) SendTrack(track.Index);
        }
    }
    public void RemoveTrack(int track) { lock (_gate) _tracks.Remove(track); }
    public void SelectTrack(int track)
    {
        lock (_gate) { _selected = track; Reply(FlProtocol.ReplySelected, U14(track)); }
    }
    public void EmitSysex(byte[] message) { lock (_gate) { if (Respond) _input?.Post(message.ToArray()); } }

    private void Reply(byte command, params byte[] payload)
    {
        if (Respond && PortsAvailable) _input?.Post([0xF0, 0x7D, 0x4D, 0x58, command, .. payload, 0xF7]);
    }
    private static byte[] U14(int value) => [(byte)(value >> 7 & 0x7F), (byte)(value & 0x7F)];
    private static int U14(byte[] data, int index) => data[index] << 7 | data[index + 1];
    private void Error(byte command, FlErrorCode code) => Reply(FlProtocol.ReplyError, command, (byte)code);
    private void SendTrack(int index)
    {
        var t = _tracks[index];
        var valid = 0; var muted = 0;
        foreach (var slot in t.Slots) { valid |= 1 << slot.Index; if (!slot.Enabled) muted |= 1 << slot.Index; }
        var name = Encoding.ASCII.GetBytes(t.Name.Length > 32 ? t.Name[..32] : t.Name);
        Reply(FlProtocol.ReplyTrack, [.. U14(index), .. U14((int)Math.Round(t.Volume * 16383)),
            (byte)(t.Muted ? 1 : 0), (byte)(t.SlotsEnabled ? 1 : 0), .. U14(valid), .. U14(muted), (byte)name.Length, .. name,
            .. t.Slots.OrderBy(s => s.Index).SelectMany(s =>
            {
                var plugin = Encoding.ASCII.GetBytes(SlotNames.GetValueOrDefault((index, s.Index), ""));
                return (byte[])[(byte)plugin.Length, .. plugin];
            })]);
    }

    private void Handle(byte[] message)
    {
        lock (_gate)
        {
            if (!PortsAvailable) throw new MidiPortException("Ports simulés retirés.");
            _sent.Add(message.ToArray());
            if (!Respond || message.Length < 6 || !message.AsSpan(0, 4).SequenceEqual(new byte[] { 0xF0, 0x7D, 0x4D, 0x58 }) || message[^1] != 0xF7) return;
            var command = message[4];
            var d = message[5..^1];
            switch (command)
            {
                case FlProtocol.CmdPing: Reply(FlProtocol.ReplyPong, [.. U14(38), FlProtocol.ProtocolVersion]); return;
                case FlProtocol.CmdGetCount: Reply(FlProtocol.ReplyCount, U14(127)); return;
                case FlProtocol.CmdList:
                    var used = _tracks.Values.Where(t => t.Index == 0 || t.Name != $"Insert {t.Index}" || t.Slots.Count > 0)
                        .Select(t => t.Index).Order().ToArray();
                    Reply(FlProtocol.ReplyListBegin);
                    foreach (var index in used) { _watched.Add(index); SendTrack(index); }
                    Reply(FlProtocol.ReplyListEnd, U14(used.Length));
                    Reply(FlProtocol.ReplySelected, U14(_selected));
                    return;
            }
            if (d.Length < 2) { Error(command, FlErrorCode.TooShort); return; }
            var track = U14(d, 0);
            if (track >= 127) { Error(command, FlErrorCode.NoSuchTrack); return; }
            if (!_tracks.TryGetValue(track, out var t))
                _tracks[track] = t = new(track, $"Insert {track}", .8f, false, true, []);
            _watched.Add(track);
            switch (command)
            {
                case FlProtocol.CmdGetTrack: SendTrack(track); return;
                case FlProtocol.CmdSelect: _selected = track; Reply(FlProtocol.ReplySelected, U14(track)); break;
                case FlProtocol.CmdMute when d.Length >= 3: t = t with { Muted = d[2] != 0 }; break;
                case FlProtocol.CmdVolume when d.Length >= 4: t = t with { Volume = U14(d, 2) / 16383f }; break;
                case FlProtocol.CmdSlotsEnabled when d.Length >= 3: t = t with { SlotsEnabled = d[2] != 0 }; break;
                case FlProtocol.CmdSlot or FlProtocol.CmdPreset when d.Length >= 4:
                    if (!t.Slots.Any(s => s.Index == d[2])) { Error(command, FlErrorCode.EmptySlot); return; }
                    if (command == FlProtocol.CmdSlot)
                        t = t with { Slots = t.Slots.Select(s => s.Index == d[2] ? s with { Enabled = d[3] != 0 } : s).ToArray() };
                    break;
                case FlProtocol.CmdMute or FlProtocol.CmdVolume or FlProtocol.CmdSlotsEnabled or FlProtocol.CmdSlot or FlProtocol.CmdPreset:
                    Error(command, FlErrorCode.TooShort); return;
                default: Error(command, FlErrorCode.UnknownCommand); return;
            }
            _tracks[track] = t;
            SendTrack(track);
        }
    }

    private sealed class Output(SimulatedFlStudio owner) : IMidiOutput
    {
        private bool _disposed;
        public void SendSysex(byte[] message) { ObjectDisposedException.ThrowIf(_disposed, this); owner.Handle(message); }
        public void Dispose() => _disposed = true;
    }
    private sealed class Input : IMidiInput
    {
        private readonly BlockingCollection<byte[]> _queue = new();
        private readonly Thread _thread;
        private int _disposed;
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public Input(Action<byte[]> receive)
        {
            _thread = new Thread(() =>
            {
                foreach (var message in _queue.GetConsumingEnumerable())
                    try { receive(message); } catch { /* Isoler les abonnés comme le transport réel. */ }
            }) { IsBackground = true, Name = "Mixdeck FL simulé" };
            _thread.Start();
        }
        public void Post(byte[] message)
        {
            if (!IsDisposed)
                try { _queue.Add(message); } catch (InvalidOperationException) { /* Fermeture concurrente. */ }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _queue.CompleteAdding();
            if (Thread.CurrentThread != _thread) _thread.Join();
        }
    }
}
