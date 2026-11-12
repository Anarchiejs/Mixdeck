using Mixdeck.Core.Model;

namespace Mixdeck.Platform.Desktop.Hotkeys;

/// <summary>
/// Les raccourcis sont stockés en codes virtuels Win32 (le format déjà enregistré dans settings.json).
/// Cette table les traduit vers les keysyms X11 et les keycodes Carbon, pour les touches que Mixdeck propose.
/// </summary>
public static class VirtualKeyMap
{
    public const uint X11Shift = 1;
    public const uint X11Lock = 2;
    public const uint X11Control = 4;
    public const uint X11Alt = 8;
    public const uint X11NumLock = 16;
    public const uint X11Super = 64;

    public const uint MacCommand = 1 << 8;
    public const uint MacShift = 1 << 9;
    public const uint MacOption = 1 << 11;
    public const uint MacControl = 1 << 12;

    private static readonly Dictionary<int, int> MacKeyCodes = new()
    {
        [0x41] = 0x00, [0x42] = 0x0B, [0x43] = 0x08, [0x44] = 0x02, [0x45] = 0x0E,
        [0x46] = 0x03, [0x47] = 0x05, [0x48] = 0x04, [0x49] = 0x22, [0x4A] = 0x26,
        [0x4B] = 0x28, [0x4C] = 0x25, [0x4D] = 0x2E, [0x4E] = 0x2D, [0x4F] = 0x1F,
        [0x50] = 0x23, [0x51] = 0x0C, [0x52] = 0x0F, [0x53] = 0x01, [0x54] = 0x11,
        [0x55] = 0x20, [0x56] = 0x09, [0x57] = 0x0D, [0x58] = 0x07, [0x59] = 0x10,
        [0x5A] = 0x06,
        [0x30] = 0x1D, [0x31] = 0x12, [0x32] = 0x13, [0x33] = 0x14, [0x34] = 0x15,
        [0x35] = 0x17, [0x36] = 0x16, [0x37] = 0x1A, [0x38] = 0x1C, [0x39] = 0x19,
        [0x70] = 0x7A, [0x71] = 0x78, [0x72] = 0x63, [0x73] = 0x76, [0x74] = 0x60,
        [0x75] = 0x61, [0x76] = 0x62, [0x77] = 0x64, [0x78] = 0x65, [0x79] = 0x6D,
        [0x7A] = 0x67, [0x7B] = 0x6F,
        [0x25] = 0x7B, [0x26] = 0x7E, [0x27] = 0x7C, [0x28] = 0x7D,
        [0x20] = 0x31, [0x0D] = 0x24, [0x1B] = 0x35, [0x09] = 0x30, [0x08] = 0x33,
        [0x2E] = 0x75, [0x24] = 0x73, [0x23] = 0x77, [0x21] = 0x74, [0x22] = 0x79,
    };

    public static bool TryX11Keysym(int virtualKey, out uint keysym)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            keysym = (uint)(virtualKey + 0x20);
            return true;
        }
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            keysym = (uint)virtualKey;
            return true;
        }
        if (virtualKey is >= 0x70 and <= 0x87)
        {
            keysym = 0xffbe + (uint)(virtualKey - 0x70);
            return true;
        }
        keysym = virtualKey switch
        {
            0x25 => 0xff51,
            0x26 => 0xff52,
            0x27 => 0xff53,
            0x28 => 0xff54,
            0x20 => 0x20,
            0x0D => 0xff0d,
            0x1B => 0xff1b,
            0x09 => 0xff09,
            0x08 => 0xff08,
            0x2E => 0xffff,
            0x24 => 0xff50,
            0x23 => 0xff57,
            0x21 => 0xff55,
            0x22 => 0xff56,
            0x2D => 0xff63,
            _ => 0,
        };
        return keysym != 0;
    }

    public static bool TryMacKeyCode(int virtualKey, out uint keyCode)
    {
        if (!MacKeyCodes.TryGetValue(virtualKey, out var code))
        {
            keyCode = 0;
            return false;
        }
        keyCode = (uint)code;
        return true;
    }

    public static uint X11Modifiers(HotkeyModifiers modifiers)
    {
        uint mask = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) mask |= X11Shift;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) mask |= X11Control;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) mask |= X11Alt;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) mask |= X11Super;
        return mask;
    }

    public static IReadOnlyList<uint> X11GrabMasks(HotkeyModifiers modifiers)
    {
        var mask = X11Modifiers(modifiers);
        return [mask, mask | X11Lock, mask | X11NumLock, mask | X11Lock | X11NumLock];
    }

    public static uint MacModifiers(HotkeyModifiers modifiers)
    {
        uint mask = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) mask |= MacShift;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) mask |= MacControl;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) mask |= MacOption;
        if (modifiers.HasFlag(HotkeyModifiers.Win)) mask |= MacCommand;
        return mask;
    }

    public static string Format(HotkeyBinding binding)
    {
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add(OperatingSystem.IsMacOS() ? "Option" : "Alt");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win))
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : OperatingSystem.IsLinux() ? "Super" : "Win");
        parts.Add(KeyName(binding.VirtualKey));
        return string.Join("+", parts);
    }

    public static string KeyName(int virtualKey) => virtualKey switch
    {
        >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => $"F{virtualKey - 0x6F}",
        0x25 => "Left",
        0x26 => "Up",
        0x27 => "Right",
        0x28 => "Down",
        0x20 => "Space",
        0x0D => "Enter",
        0x1B => "Esc",
        0x09 => "Tab",
        0x08 => "Backspace",
        0x2E => "Delete",
        _ => $"VK {virtualKey:X2}",
    };
}
