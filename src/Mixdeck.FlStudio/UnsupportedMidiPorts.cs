namespace Mixdeck.FlStudio;

/// <summary>Transport MIDI absent sur ce système. Aucun port n'est ouvert et FL Studio n'est pas simulé.</summary>
public sealed class UnsupportedMidiPorts(string reason) : IMidiPorts, IMidiAvailability
{
    public bool IsSupported => false;

    public string Reason { get; } = reason;

    public IReadOnlyList<string> OutputNames() => [];

    public IReadOnlyList<string> InputNames() => [];

    public IMidiOutput OpenOutput(string name) => throw new MidiPortException(Reason);

    public IMidiInput OpenInput(string name, Action<byte[]> onSysex) => throw new MidiPortException(Reason);
}

/// <summary>Choisit le transport MIDI du système. L'appli Windows continue d'instancier <see cref="WinMidiPorts"/> directement.</summary>
public static class MidiPorts
{
    public static IMidiPorts CreatePlatform()
    {
        if (OperatingSystem.IsWindows()) return new WinMidiPorts();
        if (OperatingSystem.IsMacOS()) return new CoreMidiPorts();
        return new UnsupportedMidiPorts("FL Studio MIDI is available on Windows and macOS. Mixdeck does not open a MIDI port here.");
    }
}
