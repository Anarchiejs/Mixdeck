using System.Text;
using Mixdeck.Core.Model;

namespace Mixdeck.FlStudio;

/// <summary>
/// Messages SysEx échangés avec fl/Mixdeck/device_Mixdeck.py (les deux fichiers évoluent ensemble) :
/// F0 7D 4D 58 &lt;commande&gt; &lt;données 7 bits&gt; F7. Pistes et volumes sur deux octets de 7 bits (14 bits).
/// </summary>
public static class FlProtocol
{
    public const int ProtocolVersion = 3;
    public const int SlotCount = 10;
    public const int MaxTrack = 16383;

    private static readonly byte[] Header = [0xF0, 0x7D, 0x4D, 0x58];

    internal const byte CmdPing = 0x01, CmdSelect = 0x02, CmdMute = 0x03, CmdVolume = 0x04, CmdSlot = 0x05,
        CmdPreset = 0x06, CmdSlotsEnabled = 0x07, CmdGetTrack = 0x08, CmdGetCount = 0x09, CmdList = 0x0A;

    internal const byte ReplyPong = 0x41, ReplyTrack = 0x48, ReplyCount = 0x49, ReplyListBegin = 0x4A, ReplyListEnd = 0x4B,
        ReplySelected = 0x4C, ReplyError = 0x7F;

    public static byte[] Ping() => Build(CmdPing);
    public static byte[] GetCount() => Build(CmdGetCount);
    public static byte[] List() => Build(CmdList);
    public static byte[] GetTrack(int track) => Build(CmdGetTrack, U14(track));
    public static byte[] Select(int track) => Build(CmdSelect, U14(track));
    public static byte[] Mute(int track, bool muted) => Build(CmdMute, [.. U14(track), Bit(muted)]);

    /// <param name="volume">0 à 1, borné.</param>
    public static byte[] Volume(int track, float volume) =>
        Build(CmdVolume, [.. U14(track), .. U14((int)Math.Round(Math.Clamp(float.IsFinite(volume) ? volume : 0f, 0f, 1f) * 16383))]);

    public static byte[] SlotsEnabled(int track, bool enabled) => Build(CmdSlotsEnabled, [.. U14(track), Bit(enabled)]);
    public static byte[] Slot(int track, int slot, bool enabled) => Build(CmdSlot, [.. U14(track), SlotByte(slot), Bit(enabled)]);
    public static byte[] Preset(int track, int slot, bool next) => Build(CmdPreset, [.. U14(track), SlotByte(slot), Bit(next)]);

    /// <summary>Décode un message du script ; null si ce n'est pas un message Mixdeck valide.</summary>
    public static FlMessage? Decode(ReadOnlySpan<byte> m)
    {
        if (m.Length < 6 || !m[..4].SequenceEqual(Header) || m[^1] != 0xF7) return null;
        var d = m[5..^1];
        foreach (var b in d) if (b > 0x7F) return null;
        switch (m[4])
        {
            case ReplyPong when d.Length >= 2:
                return new FlPong(U14(d, 0), d.Length >= 3 ? d[2] : 0);
            case ReplyCount when d.Length >= 2:
                return new FlTrackCount(U14(d, 0));
            case ReplyListBegin:
                return new FlListBegin();
            case ReplyListEnd when d.Length >= 2:
                return new FlListEnd(U14(d, 0));
            case ReplySelected when d.Length >= 2:
                return new FlSelectedTrack(U14(d, 0));
            case ReplyError when d.Length >= 2:
                return new FlError(d[0], (FlErrorCode)d[1]);
            case ReplyTrack when d.Length >= 11 && d.Length >= 11 + d[10]:
                var valid = U14(d, 6);
                var muted = U14(d, 8);
                var name = Encoding.ASCII.GetString(d.Slice(11, d[10]));
                // Protocole 2 : après le nom de la piste, le nom du plugin de chaque slot occupé (longueur puis texte).
                var rest = d[(11 + d[10])..];
                var slots = new List<DawSlot>();
                for (var s = 0; s < SlotCount; s++)
                {
                    if ((valid >> s & 1) == 0) continue;
                    var plugin = "";
                    if (rest.Length > 0 && rest.Length > rest[0])
                    {
                        plugin = Encoding.ASCII.GetString(rest.Slice(1, rest[0]));
                        rest = rest[(1 + rest[0])..];
                    }
                    slots.Add(new DawSlot(s, (muted >> s & 1) == 0, plugin));
                }
                return new FlTrackState(new DawTrack(U14(d, 0), name, U14(d, 2) / 16383f, d[4] == 1, d[5] == 1, slots));
            default:
                return null;
        }
    }

    private static byte[] Build(byte command, params byte[] data) => [.. Header, command, .. data, 0xF7];

    private static byte[] U14(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxTrack);
        return [(byte)(value >> 7), (byte)(value & 0x7F)];
    }

    private static int U14(ReadOnlySpan<byte> d, int i) => d[i] << 7 | d[i + 1];

    private static byte Bit(bool value) => value ? (byte)1 : (byte)0;

    private static byte SlotByte(int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotCount);
        return (byte)slot;
    }
}

public enum FlErrorCode : byte
{
    UnknownCommand = 1,
    NoSuchTrack = 2,
    EmptySlot = 3,
    TooShort = 4,
}

public abstract record FlMessage;

/// <param name="ApiVersion">Version de l'API de script de FL (38 pour FL Studio 2025).</param>
/// <param name="ProtocolVersion">Version du protocole du script ; 0 = prototype sans numéro.</param>
public sealed record FlPong(int ApiVersion, int ProtocolVersion) : FlMessage;

public sealed record FlTrackCount(int Count) : FlMessage;

/// <summary>Début d'une liste complète (réponse à List, ou envoi spontané quand les pistes utilisées changent dans FL).</summary>
public sealed record FlListBegin : FlMessage;

/// <summary>Fin de la réponse à List : <paramref name="Count"/> états de piste ont précédé.</summary>
public sealed record FlListEnd(int Count) : FlMessage;

public sealed record FlSelectedTrack(int Track) : FlMessage;

public sealed record FlError(byte Command, FlErrorCode Code) : FlMessage;

public sealed record FlTrackState(DawTrack Track) : FlMessage;
