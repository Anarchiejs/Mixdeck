using Mixdeck.Core.Model;

namespace Mixdeck.Core.Settings;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Préréglages simples du micro (porte de bruit, compresseur, clarté de la voix).</summary>
public enum MicPreset
{
    /// <summary>Aucun traitement.</summary>
    Off,
    /// <summary>Porte de bruit douce, compression légère.</summary>
    Light,
    /// <summary>Réglage conseillé pour parler (jeu, appels).</summary>
    Voice,
    /// <summary>Pièce bruyante : porte de bruit forte, voix plus présente.</summary>
    Strong,
}

/// <summary>Atténuation automatique : baisser une entrée (musique) quand une autre a du signal.</summary>
public sealed record DuckingSettings
{
    public bool Enabled { get; init; }

    /// <summary>Entrée écoutée (ex. la tranche d'un appel).</summary>
    public int SourceStrip { get; init; } = 3;

    /// <summary>Entrée baissée (ex. la tranche de la musique).</summary>
    public int TargetStrip { get; init; } = 4;

    /// <summary>Niveau au-dessus duquel la source « parle », en dBFS.</summary>
    public float ThresholdDb { get; init; } = -40f;

    /// <summary>Baisse appliquée à la cible, en dB (négatif).</summary>
    public float AmountDb { get; init; } = -15f;

    public int AttackMilliseconds { get; init; } = 80;

    /// <summary>Temps passé sous le seuil avant de remonter.</summary>
    public int HoldMilliseconds { get; init; } = 500;

    public int ReleaseMilliseconds { get; init; } = 800;
}

/// <summary>Nom, icône et visibilité d'une tranche ou d'un bus, choisis par l'utilisateur.</summary>
/// <param name="DisplayName">null = libellé de Voicemeeter, sinon nom par défaut.</param>
/// <param name="Icon">Glyphe Segoe Fluent Icons ; null = icône par défaut.</param>
/// <param name="ShowInConsole">null = automatique : affichée dans la console si elle est virtuelle ou a un périphérique.</param>
public sealed record ChannelCustomization(int Index, string? DisplayName = null, string? Icon = null, bool ShowInFlyout = true, bool? ShowInConsole = null);

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

    /// <summary>Sortie où « M'écouter » renvoie le micro (0 = A1).</summary>
    public int MonitorBusIndex { get; init; }

    /// <summary>Durée du fondu des gains lors d'un changement de profil.</summary>
    public int FadeMilliseconds { get; init; } = 300;

    public IReadOnlyList<ChannelCustomization> Strips { get; init; } = [];
    public IReadOnlyList<ChannelCustomization> Buses { get; init; } = [];

    public IReadOnlyList<HotkeyBinding> Hotkeys { get; init; } = DefaultHotkeys;
    public IReadOnlyList<Rule> Rules { get; init; } = [];

    /// <summary>Dernier profil choisi à la main ; base de retour quand aucune règle n'est active.</summary>
    public string? LastProfileId { get; init; }

    /// <summary>Profil appliqué au démarrage de Mixdeck ; null = ne rien appliquer.</summary>
    public string? StartupProfileId { get; init; }

    /// <summary>Garder l'icône visible près de l'horloge (et pas derrière la flèche ^).</summary>
    public bool AlwaysShowTrayIcon { get; init; } = true;

    /// <summary>
    /// Mixdeck a déjà sorti son icône de la zone cachée une fois. Si l'utilisateur la range ensuite derrière la
    /// flèche ^, on respecte son choix et on ne la ressort plus.
    /// </summary>
    public bool TrayIconPromoted { get; init; }

    /// <summary>Cacher la fenêtre de Voicemeeter quand Mixdeck le lance (Command.Show = 0).</summary>
    public bool HideVoicemeeterWindow { get; init; } = true;

    /// <summary>Dernier préréglage du micro appliqué depuis Mixdeck (affichage seulement : Voicemeeter fait foi).</summary>
    public MicPreset MicPreset { get; init; } = MicPreset.Off;

    public DuckingSettings Ducking { get; init; } = new();

    /// <summary>Pilotage de FL Studio par loopMIDI.</summary>
    public FlStudioSettings FlStudio { get; init; } = new();

    /// <summary>Petit badge permanent à l'écran tant que le micro est coupé.</summary>
    public bool ShowMicBadge { get; init; }

    /// <summary>Son bref de confirmation quand le micro est coupé ou activé.</summary>
    public bool PlayMicSounds { get; init; }

    /// <summary>Volume par application dans le panneau rapide.</summary>
    public bool ShowAppVolumes { get; init; } = true;

    /// <summary>Langue de l'interface ("fr-FR", "en-US") ; null = langue de Windows.</summary>
    public string? Language { get; init; }

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
