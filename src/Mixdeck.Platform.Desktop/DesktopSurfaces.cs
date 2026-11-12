using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Desktop.Audio;
using Mixdeck.Platform.Desktop.Foreground;
using Mixdeck.Platform.Desktop.Hotkeys;
using Mixdeck.Platform.Desktop.Processes;
using Mixdeck.Platform.Desktop.Startup;

namespace Mixdeck.Platform.Desktop;

public sealed class DesktopSurfaces : IDisposable
{
    private DesktopSurfaces(
        ISystemAudio audio,
        IHotkeyService hotkeys,
        IProcessWatcher processes,
        IForegroundWatcher foreground,
        IAutoStart autoStart,
        DesktopCapabilities capabilities)
    {
        Audio = audio;
        Hotkeys = hotkeys;
        Processes = processes;
        Foreground = foreground;
        AutoStart = autoStart;
        Capabilities = capabilities;
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
        if (OperatingSystem.IsMacOS()) return Mac(processes, logger);
        return Other(processes);
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
        return new DesktopSurfaces(audio, hotkeys, processes, foreground, LaunchAgentAutoStart.ForCurrentProcess(), capabilities);
    }

    private static DesktopSurfaces Other(PollingProcessWatcher processes)
    {
        var windowsApp = OperatingSystem.IsWindows()
            ? "On Windows, system audio, hotkeys, and the tray live in the WinUI app."
            : "This desktop build runs on macOS.";
        var capabilities = new DesktopCapabilities(
            OperatingSystem.IsWindows() ? "Windows" : "Unsupported",
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
            capabilities);
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        Foreground.Dispose();
        Processes.Dispose();
        Audio.Dispose();
    }
}
