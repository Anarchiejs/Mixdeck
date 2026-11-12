namespace Mixdeck.FlStudio;

/// <summary>Transport MIDI absent. FL Studio n'existe pas sur Linux : on n'ouvre aucun port et on ne simule pas l'application.</summary>
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
        return new UnsupportedMidiPorts("FL Studio has no Linux build. Mixdeck does not open MIDI ports on Linux.");
    }
}
