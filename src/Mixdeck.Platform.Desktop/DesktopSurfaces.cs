using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Desktop.Audio;
using Mixdeck.Platform.Desktop.Foreground;
using Mixdeck.Platform.Desktop.Hotkeys;
using Mixdeck.Platform.Desktop.Native;
using Mixdeck.Platform.Desktop.Processes;
using Mixdeck.Platform.Desktop.Startup;

namespace Mixdeck.Platform.Desktop;

public sealed class DesktopSurfaces : IDisposable
{
    private readonly X11Session? _x11;

    private DesktopSurfaces(
        ISystemAudio audio,
        IHotkeyService hotkeys,
        IProcessWatcher processes,
        IForegroundWatcher foreground,
        IAutoStart autoStart,
        DesktopCapabilities capabilities,
        X11Session? x11)
    {
        Audio = audio;
        Hotkeys = hotkeys;
        Processes = processes;
        Foreground = foreground;
        AutoStart = autoStart;
        Capabilities = capabilities;
        _x11 = x11;
    }

    public ISystemAudio Audio { get; }
    public IHotkeyService Hotkeys { get; }
    public IProcessWatcher Processes { get; }
    public IForegroundWatcher Foreground { get; }
    public IAutoStart AutoStart { get; }
    public DesktopCapabilities Capabilities { get; }

    public static DesktopSurfaces Create(ILogger? logger = null)
    {
        var processes = new PollingProcessWatcher();
        if (OperatingSystem.IsLinux()) return Linux(processes, logger);
        if (OperatingSystem.IsMacOS()) return Mac(processes, logger);
        return Other(processes);
    }

    private static DesktopSurfaces Linux(PollingProcessWatcher processes, ILogger? logger)
    {
        var audio = PulseAudioBackend.Create();
        var session = X11Session.TryOpen(out var x11Reason);
        if (session is null) logger?.LogInformation("X11 indisponible : {Reason}", x11Reason);
        var hotkeys = new X11HotkeyService(session, x11Reason);
        var foreground = new X11ForegroundWatcher(session, x11Reason);
        var capabilities = new DesktopCapabilities(
            "Linux",
            Voicemeeter: false,
            SystemDevices: true,
            PerAppVolume: true,
            PerAppRouting: true,
            GlobalHotkeys: session is not null,
            ForegroundApps: session is not null,
            FlStudio: false,
            audio.UnavailableReason ?? "Uses pactl against PulseAudio or pipewire-pulse.",
            x11Reason,
            x11Reason,
            "FL Studio has no Linux version.");
        return new DesktopSurfaces(audio, hotkeys, processes, foreground, XdgAutoStart.ForCurrentProcess(), capabilities, session);
    }

    private static DesktopSurfaces Mac(PollingProcessWatcher processes, ILogger? logger)
    {
        var audio = CoreAudioBackend.Create();
        IHotkeyService hotkeys;
        try { hotkeys = new MacHotkeyService(); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            logger?.LogWarning(exception, "Raccourcis Carbon indisponibles.");
            hotkeys = new UnsupportedHotkeyService("Carbon RegisterEventHotKey is not available.");
        }
        var foreground = new MacForegroundWatcher();
        var hotkeyBackend = hotkeys as IHotkeyBackend;
        var capabilities = new DesktopCapabilities(
            "macOS",
            Voicemeeter: false,
            SystemDevices: true,
            PerAppVolume: false,
            PerAppRouting: false,
            GlobalHotkeys: hotkeyBackend?.IsSupported != false,
            ForegroundApps: true,
            FlStudio: true,
            "Core Audio devices, default device, volume, and mute. Per-app volume is not a public Core Audio control.",
            hotkeyBackend?.UnavailableReason,
            null,
            "Core MIDI. Create no loopMIDI port: Mixdeck opens an IAC port of that name, or creates a virtual port.");
        return new DesktopSurfaces(audio, hotkeys, processes, foreground, LaunchAgentAutoStart.ForCurrentProcess(), capabilities, null);
    }

    private static DesktopSurfaces Other(PollingProcessWatcher processes)
    {
        const string windowsApp = "On Windows, system audio, hotkeys, and the tray live in the WinUI app.";
        var capabilities = new DesktopCapabilities(
            OperatingSystem.IsWindows() ? "Windows" : "Unknown",
            Voicemeeter: OperatingSystem.IsWindows(),
            SystemDevices: false,
            PerAppVolume: false,
            PerAppRouting: false,
            GlobalHotkeys: false,
            ForegroundApps: false,
            FlStudio: OperatingSystem.IsWindows(),
            windowsApp,
            windowsApp,
            windowsApp,
            OperatingSystem.IsWindows() ? null : "FL Studio is not available on this operating system.");
        return new DesktopSurfaces(
            new UnavailableSystemAudio(windowsApp),
            new UnsupportedHotkeyService(windowsApp),
            processes,
            new IdleForegroundWatcher(windowsApp),
            new UnsupportedAutoStart(windowsApp),
            capabilities,
            null);
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        Foreground.Dispose();
        Processes.Dispose();
        Audio.Dispose();
        _x11?.Dispose();
    }
}
