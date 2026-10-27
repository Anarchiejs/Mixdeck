namespace Mixdeck.Core.Model;

/// <summary>Modificateurs, mêmes valeurs que MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN de RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public enum HotkeyAction
{
    ToggleMic,
    /// <summary>Micro actif tant que la touche est maintenue (crochet clavier bas niveau).</summary>
    PushToTalk,
    NextProfile,
    PreviousProfile,
    VolumeUp,
    VolumeDown,
    OpenFlyout,
    /// <summary>Applique le profil <see cref="HotkeyBinding.ProfileId"/>.</summary>
    ApplyProfile,
}

/// <param name="VirtualKey">Code de touche virtuelle Win32 (VK_*).</param>
/// <param name="TargetBus">Pour VolumeUp/VolumeDown : bus visé (0 = A1).</param>
/// <param name="StepDb">Pas de volume en dB pour VolumeUp/VolumeDown.</param>
/// <param name="ProfileId">Pour ApplyProfile : identifiant du profil à appliquer.</param>
public sealed record HotkeyBinding(
    HotkeyAction Action,
    HotkeyModifiers Modifiers,
    int VirtualKey,
    int TargetBus = 0,
    float StepDb = 2f,
    string? ProfileId = null);
