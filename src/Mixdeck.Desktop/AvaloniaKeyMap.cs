using Avalonia.Input;
using Mixdeck.Core.Model;

namespace Mixdeck.Desktop;

internal static class AvaloniaKeyMap
{
    public static bool TryVirtualKey(Key key, KeyModifiers modifiers, out int virtualKey, out HotkeyModifiers mapped)
    {
        virtualKey = key switch
        {
            >= Key.A and <= Key.Z => 0x41 + (key - Key.A),
            >= Key.D0 and <= Key.D9 => 0x30 + (key - Key.D0),
            >= Key.F1 and <= Key.F24 => 0x70 + (key - Key.F1),
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Space => 0x20,
            Key.Enter => 0x0D,
            Key.Escape => 0x1B,
            Key.Tab => 0x09,
            Key.Back => 0x08,
            Key.Delete => 0x2E,
            _ => 0,
        };
        mapped = HotkeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control)) mapped |= HotkeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mapped |= HotkeyModifiers.Alt;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mapped |= HotkeyModifiers.Shift;
        if (modifiers.HasFlag(KeyModifiers.Meta)) mapped |= HotkeyModifiers.Win;
        return virtualKey != 0 && mapped != HotkeyModifiers.None;
    }
}
