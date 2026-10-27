using Mixdeck.Core.Model;

namespace Mixdeck.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Nom, icône et visibilité d'une tranche ou d'un bus, choisis par l'utilisateur.</summary>
/// <param name="DisplayName">null = libellé de Voicemeeter, sinon nom par défaut.</param>
/// <param name="Icon">Glyphe Segoe Fluent Icons ; null = icône par défaut.</param>
public sealed record ChannelCustomization(int Index, string? DisplayName = null, string? Icon = null, bool ShowInFlyout = true);

/// <summary>Réglages de l'application, enregistrés dans settings.json.</summary>
public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public ThemeMode Theme { get; init; } = ThemeMode.System;

    /// <summary>"#RRGGBB" ; null = couleur d'accent de Windows.</summary>
    public string? AccentColor { get; init; }

    public bool StartWithWindows { get; init; } = true;
    public bool AutoLaunchVoicemeeter { get; init; }
    public bool ShowOsd { get; init; } = true;
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Tranche pilotée par le gros bouton « Micro » et les raccourcis micro.</summary>
    public int MicStripIndex { get; init; }

    /// <summary>Durée du fondu des gains lors d'un changement de profil.</summary>
    public int FadeMilliseconds { get; init; } = 300;

    public IReadOnlyList<ChannelCustomization> Strips { get; init; } = [];
    public IReadOnlyList<ChannelCustomization> Buses { get; init; } = [];

    public IReadOnlyList<HotkeyBinding> Hotkeys { get; init; } = DefaultHotkeys;
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Dernier profil choisi à la main ; base de retour quand aucune règle n'est active.</summary>
    public string? LastProfileId { get; init; }

    public static IReadOnlyList<HotkeyBinding> DefaultHotkeys { get; } =
    [
        // Ctrl+Alt+M : couper / activer le micro
        new(HotkeyAction.ToggleMic, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D),
        // Ctrl+Alt+→ / ← : profil suivant / précédent
        new(HotkeyAction.NextProfile, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x27),
        new(HotkeyAction.PreviousProfile, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x25),
        // Ctrl+Alt+↑ / ↓ : volume A1
        new(HotkeyAction.VolumeUp, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x26),
        new(HotkeyAction.VolumeDown, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x28),
    ];

    public ChannelCustomization StripCustomization(int index) =>
        Strips.FirstOrDefault(c => c.Index == index) ?? new ChannelCustomization(index);

    public ChannelCustomization BusCustomization(int index) =>
        Buses.FirstOrDefault(c => c.Index == index) ?? new ChannelCustomization(index);
}
