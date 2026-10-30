namespace Mixdeck.FlStudio;

/// <summary>Accès aux ports MIDI de Windows, remplaçable par un faux dans les tests et en mode démo.</summary>
public interface IMidiPorts
{
    IReadOnlyList<string> OutputNames();

    IReadOnlyList<string> InputNames();

    /// <summary>Ouvre une sortie ; lève <see cref="MidiPortException"/> si le port est introuvable ou occupé.</summary>
    IMidiOutput OpenOutput(string name);

    /// <summary>Ouvre une entrée ; les SysEx reçus sont remis à <paramref name="onSysex"/> sur un thread de fond.</summary>
    IMidiInput OpenInput(string name, Action<byte[]> onSysex);
}

public interface IMidiOutput : IDisposable
{
    void SendSysex(byte[] message);
}

public interface IMidiInput : IDisposable;

public sealed class MidiPortException(string message) : Exception(message);
