using Mixdeck.Core.Model;

namespace Mixdeck.Platform.Hotkeys;

public static class HotkeyFormatter
{
    public static string Format(HotkeyBinding binding)
    {
        List<string> parts = [];
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Maj");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(binding.VirtualKey));
        return string.Join("+", parts);
    }

    public static bool IsValidCombination(HotkeyModifiers modifiers, int vk) =>
        (modifiers & ~(HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win)) == 0 &&
        vk is > 0 and < 255 && vk is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5) &&
        (modifiers != HotkeyModifiers.None || vk is >= 0x7C and <= 0x87 or >= 0xAD and <= 0xB3);

    private static string KeyName(int vk) => vk switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        >= 0x60 and <= 0x69 => $"Num {vk - 0x60}",
        0x25 => "←", 0x26 => "↑", 0x27 => "→", 0x28 => "↓",
        0x08 => "Retour arrière", 0x09 => "Tab", 0x0D => "Entrée", 0x1B => "Échap", 0x20 => "Espace",
        0x21 => "Page précédente", 0x22 => "Page suivante", 0x23 => "Fin", 0x24 => "Début",
        0x2D => "Inser", 0x2E => "Suppr", 0x6A => "Num *", 0x6B => "Num +", 0x6C => "Num séparateur",
        0x6D => "Num -", 0x6E => "Num ,", 0x6F => "Num /",
        0xAD => "Muet", 0xAE => "Volume −", 0xAF => "Volume +", 0xB0 => "Piste suivante",
        0xB1 => "Piste précédente", 0xB2 => "Arrêt", 0xB3 => "Lecture/Pause", _ => $"VK {vk:X2}"
    };
}
