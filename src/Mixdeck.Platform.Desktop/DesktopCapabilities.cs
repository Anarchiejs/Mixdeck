namespace Mixdeck.Platform.Desktop;

/// <summary>Ce que cet ordinateur peut vraiment faire. Les drapeaux faux ne sont pas simulés.</summary>
public sealed record DesktopCapabilities(
    string OperatingSystemName,
    bool Voicemeeter,
    bool SystemDevices,
    bool PerAppVolume,
    bool PerAppRouting,
    bool GlobalHotkeys,
    bool ForegroundApps,
    bool FlStudio,
    string? SystemAudioDetail,
    string? HotkeyDetail,
    string? ForegroundDetail,
    string? FlStudioDetail);
