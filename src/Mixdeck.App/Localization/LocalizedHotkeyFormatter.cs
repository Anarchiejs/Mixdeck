using Mixdeck.Core.Model;
using Mixdeck.Platform.Hotkeys;

namespace Mixdeck.App.Localization;

/// <summary>Libellés des raccourcis dans la langue de l'interface ; la validation reste dans Platform.</summary>
public static class LocalizedHotkeyFormatter
{
    public static string Format(HotkeyBinding binding)
    {
        List<string> parts = [];
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add(Strings.Get("Hotkey_Control"));
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add(Strings.Get("Hotkey_Alt"));
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add(Strings.Get("Hotkey_Shift"));
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add(Strings.Get("Hotkey_Windows"));
        parts.Add(KeyName(binding));
        return string.Join("+", parts);
    }

    private static string KeyName(HotkeyBinding binding)
    {
        var key = binding.VirtualKey switch
        {
            0x08 => "Hotkey_Backspace", 0x09 => "Hotkey_Tab", 0x0D => "Hotkey_Enter",
            0x1B => "Hotkey_Escape", 0x20 => "Hotkey_Space", 0x21 => "Hotkey_PageUp",
            0x22 => "Hotkey_PageDown", 0x23 => "Hotkey_End", 0x24 => "Hotkey_Home",
            0x2D => "Hotkey_Insert", 0x2E => "Hotkey_Delete",
            0x6A => "Hotkey_NumMultiply", 0x6B => "Hotkey_NumAdd", 0x6C => "Hotkey_NumSeparator",
            0x6D => "Hotkey_NumSubtract", 0x6E => "Hotkey_NumDecimal", 0x6F => "Hotkey_NumDivide",
            0xAD => "Hotkey_Mute", 0xAE => "Hotkey_VolumeDown", 0xAF => "Hotkey_VolumeUp",
            0xB0 => "Hotkey_NextTrack", 0xB1 => "Hotkey_PreviousTrack", 0xB2 => "Hotkey_Stop",
            0xB3 => "Hotkey_PlayPause", _ => null,
        };
        if (key is not null) return Strings.Get(key);
        if (binding.VirtualKey is >= 0x60 and <= 0x69)
            return Strings.Format("Hotkey_NumDigit", binding.VirtualKey - 0x60);
        // Lettres, chiffres, flèches et touches F sont indépendants de la langue.
        // Ne jamais découper sur « + » : ce signe appartient aussi à la touche Num +.
        return HotkeyFormatter.Format(binding with { Modifiers = HotkeyModifiers.None });
    }
}
