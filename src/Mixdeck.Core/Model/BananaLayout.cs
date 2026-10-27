namespace Mixdeck.Core.Model;

/// <summary>
/// Disposition de Voicemeeter Banana : 5 entrées (3 physiques + 2 virtuelles) et 5 bus (A1-A3 physiques, B1-B2 virtuels).
/// </summary>
public static class BananaLayout
{
    public const int StripCount = 5;
    public const int PhysicalStripCount = 3;
    public const int BusCount = 5;
    public const int PhysicalBusCount = 3;

    /// <summary>Type renvoyé par VBVMR_GetVoicemeeterType pour Banana.</summary>
    public const int VoicemeeterType = 2;

    public const float MinGainDb = -60f;
    public const float MaxGainDb = 12f;

    /// <summary>Nombre de bandes d'EQ exposées par tranche virtuelle (EQGain1..3 = graves, médiums, aigus).</summary>
    public const int VirtualStripEqBands = 3;

    /// <summary>Noms des bus, dans l'ordre des index Bus[0..4] et des paramètres de routage Strip[i].A1..B2.</summary>
    public static IReadOnlyList<string> BusNames { get; } = ["A1", "A2", "A3", "B1", "B2"];

    public static IReadOnlyList<string> DefaultStripLabels { get; } = ["Entrée 1", "Entrée 2", "Entrée 3", "VAIO", "AUX"];

    public static bool IsVirtualStrip(int strip) => strip >= PhysicalStripCount;

    public static bool IsVirtualBus(int bus) => bus >= PhysicalBusCount;

    /// <summary>Nombre total de canaux d'entrée pour VBVMR_GetLevel (types 0-2) : 3×2 + 2×8 = 22.</summary>
    public const int InputLevelChannels = PhysicalStripCount * 2 + (StripCount - PhysicalStripCount) * 8;

    /// <summary>Nombre total de canaux de sortie pour VBVMR_GetLevel (type 3) : 5×8 = 40.</summary>
    public const int OutputLevelChannels = BusCount * 8;

    /// <summary>Premier canal (gauche) d'une entrée pour VBVMR_GetLevel ; le canal droit est le suivant.</summary>
    public static int InputChannelOffset(int strip) =>
        strip < PhysicalStripCount ? strip * 2 : PhysicalStripCount * 2 + (strip - PhysicalStripCount) * 8;

    /// <summary>Premier canal (gauche) d'un bus pour VBVMR_GetLevel ; le canal droit est le suivant.</summary>
    public static int OutputChannelOffset(int bus) => bus * 8;

    public static float ClampGain(float db) => Math.Clamp(db, MinGainDb, MaxGainDb);
}
