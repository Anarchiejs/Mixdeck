namespace Mixdeck.Core.Model;

public enum AudioDeviceDirection
{
    /// <summary>Appareils d'entrée (micros) : VBVMR_Input_GetDeviceNumber/DescW.</summary>
    Input,
    /// <summary>Appareils de sortie (casque, enceintes) : VBVMR_Output_GetDeviceNumber/DescW.</summary>
    Output,
}

/// <summary>Pilote d'un appareil, mêmes valeurs que le type renvoyé par VBVMR_Input/Output_GetDeviceDescW.</summary>
public enum AudioDeviceDriver
{
    Mme = 1,
    Wdm = 3,
    Ks = 4,
    Asio = 5,
}

/// <summary>Appareil audio connu de Voicemeeter.</summary>
public sealed record AudioDevice(AudioDeviceDriver Driver, string Name, string HardwareId);
