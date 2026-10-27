namespace Mixdeck.Core.Model;

/// <summary>Disposition immuable des entrées, bus et canaux de l'API Remote.</summary>
public sealed record MixerLayout
{
    public static MixerLayout Standard { get; } = new(1, 2, 1, ["VAIO"]);
    public static MixerLayout Banana { get; } = new(2, 3, 3, ["VAIO", "AUX"]);
    public static MixerLayout Potato { get; } = new(3, 5, 5, ["VAIO", "AUX", "VAIO 3"]);

    public const float MinGainDb = -60f;
    public const float MaxGainDb = 12f;
    public const int VirtualStripEqBands = 3;

    private MixerLayout(int type, int physicalStrips, int physicalBuses, string[] virtualLabels)
    {
        VoicemeeterType = type;
        PhysicalStripCount = physicalStrips;
        PhysicalBusCount = physicalBuses;
        StripCount = physicalStrips + virtualLabels.Length;
        BusCount = physicalBuses + virtualLabels.Length;
        BusNames = Array.AsReadOnly(Enumerable.Range(1, physicalBuses).Select(i => $"A{i}")
            .Concat(Enumerable.Range(1, virtualLabels.Length).Select(i => $"B{i}")).ToArray());
        DefaultStripLabels = Array.AsReadOnly(Enumerable.Range(1, physicalStrips).Select(i => $"Entrée {i}")
            .Concat(virtualLabels).ToArray());
    }

    public int VoicemeeterType { get; }
    public int PhysicalStripCount { get; }
    public int PhysicalBusCount { get; }
    public int StripCount { get; }
    public int BusCount { get; }
    public IReadOnlyList<string> BusNames { get; }
    public IReadOnlyList<string> DefaultStripLabels { get; }
    public int InputLevelChannels => PhysicalStripCount * 2 + (StripCount - PhysicalStripCount) * 8;
    public int OutputLevelChannels => BusCount * 8;

    public static MixerLayout ForType(int type) => type switch
    {
        1 => Standard,
        2 => Banana,
        3 => Potato,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Type de Voicemeeter inconnu."),
    };

    public bool IsVirtualStrip(int strip) => strip >= PhysicalStripCount && strip < StripCount;
    public bool IsVirtualBus(int bus) => bus >= PhysicalBusCount && bus < BusCount;

    /// <summary>Entrées physiques stéréo, puis entrées virtuelles de huit canaux (SDK Remote, GetLevel).</summary>
    public int InputChannelOffset(int strip)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(strip);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(strip, StripCount);
        return strip < PhysicalStripCount ? strip * 2 : PhysicalStripCount * 2 + (strip - PhysicalStripCount) * 8;
    }

    public int OutputChannelOffset(int bus)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bus);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(bus, BusCount);
        return bus * 8;
    }

    public float ClampGain(float db) => Math.Clamp(db, MinGainDb, MaxGainDb);
}
