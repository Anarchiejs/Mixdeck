using System.Runtime.InteropServices;

namespace Mixdeck.FlStudio;

/// <summary>
/// Lit une MIDIPacketList CoreMIDI. Le paquet est empaqueté à 4 octets (pack 4) :
/// le premier paquet commence à l'offset 4. Sur ARM, le paquet suivant est aligné sur 4 ;
/// sur les autres architectures il suit immédiatement les données (MIDIPacketNext du SDK).
/// </summary>
internal static class MidiPacketListReader
{
    public static List<byte[]> Read(IntPtr list)
    {
        var messages = new List<byte[]>();
        if (list == 0) return messages;
        var count = Marshal.ReadInt32(list);
        if (count <= 0) return messages;
        if (count > 64) count = 64;
        var packet = list + 4;
        for (var i = 0; i < count; i++)
        {
            var length = (ushort)Marshal.ReadInt16(packet, 8);
            var data = new byte[length];
            if (length > 0) Marshal.Copy(packet + 10, data, 0, length);
            messages.Add(data);
            packet = Next(packet, length);
        }
        return messages;
    }

    public static IntPtr Next(IntPtr packet, ushort length)
    {
        var dataEnd = packet + 10 + length;
        if (RuntimeInformation.ProcessArchitecture is Architecture.Arm or Architecture.Arm64)
            return (IntPtr)(((nint)dataEnd + 3) & ~3);
        return dataEnd;
    }
}
