using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Audio;

public enum WindowsAudioFlow { Output = 0, Input = 1 }

public sealed record WindowsDefaultDevicesSnapshot(
    IReadOnlyList<AudioEndpoint> Outputs, IReadOnlyList<AudioEndpoint> Inputs,
    string? DefaultOutputId, string? DefaultInputId);

/// <summary>Windows defaults for all three roles; implementations never persist a choice in settings.</summary>
public interface IWindowsDefaultDevices : IDisposable
{
    event EventHandler? Changed;
    Task<WindowsDefaultDevicesSnapshot> ReadAsync();
    Task SetDefaultAsync(WindowsAudioFlow flow, string id);
}

/// <summary>Entirely in memory: demo mode must never instantiate the native service.</summary>
public sealed class SimulatedWindowsDefaultDevices : IWindowsDefaultDevices
{
    private WindowsDefaultDevicesSnapshot snapshot = new(
        [new("demo-speakers", "Speakers (Demo)"), new("demo-headset", "Headset (Demo)")],
        [new("demo-mic", "Microphone (Demo)"), new("demo-usb", "USB Microphone (Demo)")],
        "demo-speakers", "demo-mic");
    public event EventHandler? Changed;
    public Task<WindowsDefaultDevicesSnapshot> ReadAsync() => Task.FromResult(snapshot);
    public Task SetDefaultAsync(WindowsAudioFlow flow, string id)
    {
        var devices = flow switch
        {
            WindowsAudioFlow.Output => snapshot.Outputs,
            WindowsAudioFlow.Input => snapshot.Inputs,
            _ => throw new ArgumentOutOfRangeException(nameof(flow)),
        };
        if (!devices.Any(d => d.Id == id)) throw new ArgumentException("Unknown endpoint.", nameof(id));
        snapshot = flow == WindowsAudioFlow.Output ? snapshot with { DefaultOutputId = id } : snapshot with { DefaultInputId = id };
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
    public void Dispose() { }
}
