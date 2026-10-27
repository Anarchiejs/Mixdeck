namespace Mixdeck.Core.Model;

/// <summary>Noms des paramètres de l'API Remote de Voicemeeter. Un seul endroit pour les construire.</summary>
public static class VmParam
{
    public static string StripGain(int strip) => $"Strip[{strip}].Gain";
    public static string StripMute(int strip) => $"Strip[{strip}].Mute";
    public static string StripSolo(int strip) => $"Strip[{strip}].Solo";
    public static string StripPanX(int strip) => $"Strip[{strip}].Pan_x";
    public static string StripLabel(int strip) => $"Strip[{strip}].Label";

    /// <summary>Routage d'une entrée vers un bus : Strip[i].A1 … Strip[i].B2.</summary>
    public static string StripRoute(int strip, int bus) => $"Strip[{strip}].{BananaLayout.BusNames[bus]}";

    /// <summary>Gain d'EQ d'une tranche virtuelle (band 0..2 → EQGain1..3).</summary>
    public static string StripEqGain(int strip, int band) => $"Strip[{strip}].EQGain{band + 1}";

    public static string BusGain(int bus) => $"Bus[{bus}].Gain";
    public static string BusMute(int bus) => $"Bus[{bus}].Mute";
    public static string BusMono(int bus) => $"Bus[{bus}].Mono";
    public static string BusEqOn(int bus) => $"Bus[{bus}].EQ.on";
    public static string BusLabel(int bus) => $"Bus[{bus}].Label";

    /// <summary>Appareil d'une entrée physique, ex. Strip[0].device.wdm = "Micro (USB)" ; "" pour le retirer.</summary>
    public static string StripDevice(int strip, AudioDeviceDriver driver) => $"Strip[{strip}].device.{DriverKey(driver)}";
    public static string StripDeviceName(int strip) => $"Strip[{strip}].device.name";

    /// <summary>Appareil d'un bus physique, ex. Bus[0].device.wdm = "Casque (Realtek)".</summary>
    public static string BusDevice(int bus, AudioDeviceDriver driver) => $"Bus[{bus}].device.{DriverKey(driver)}";
    public static string BusDeviceName(int bus) => $"Bus[{bus}].device.name";

    public const string CommandRestart = "Command.Restart";
    public const string CommandShow = "Command.Show";

    public static string DriverKey(AudioDeviceDriver driver) => driver switch
    {
        AudioDeviceDriver.Mme => "mme",
        AudioDeviceDriver.Wdm => "wdm",
        AudioDeviceDriver.Ks => "ks",
        AudioDeviceDriver.Asio => "asio",
        _ => throw new ArgumentOutOfRangeException(nameof(driver)),
    };

    public static float FromBool(bool value) => value ? 1f : 0f;
}
