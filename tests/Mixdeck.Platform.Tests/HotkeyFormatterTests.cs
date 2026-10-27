using Mixdeck.Core.Model;
using Mixdeck.Platform.Hotkeys;

namespace Mixdeck.Platform.Tests;

public sealed class HotkeyFormatterTests
{
    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D, "Ctrl+Alt+M")]
    [InlineData(HotkeyModifiers.Shift | HotkeyModifiers.Win, 0x78, "Maj+Win+F9")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x27, "Ctrl+Alt+→")]
    [InlineData(HotkeyModifiers.Control, 0x25, "Ctrl+←")]
    [InlineData(HotkeyModifiers.Control, 0x26, "Ctrl+↑")]
    [InlineData(HotkeyModifiers.Control, 0x28, "Ctrl+↓")]
    [InlineData(HotkeyModifiers.Alt, 0x65, "Alt+Num 5")]
    [InlineData(HotkeyModifiers.None, 0x87, "F24")]
    public void FormatsFrenchNames(HotkeyModifiers modifiers, int key, string expected) =>
        HotkeyFormatter.Format(new HotkeyBinding(HotkeyAction.ToggleMic, modifiers, key)).Should().Be(expected);

    [Fact]
    public void FormatsAllFunctionAndNumpadKeys()
    {
        for (int i = 1; i <= 24; i++) HotkeyFormatter.Format(new HotkeyBinding(HotkeyAction.ToggleMic, 0, 0x6F + i)).Should().Be($"F{i}");
        for (int i = 0; i < 10; i++) HotkeyFormatter.Format(new HotkeyBinding(HotkeyAction.ToggleMic, 0, 0x60 + i)).Should().Be($"Num {i}");
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0x4D, false)]
    [InlineData(HotkeyModifiers.None, 0x7B, false)]
    [InlineData(HotkeyModifiers.None, 0x7C, true)]
    [InlineData(HotkeyModifiers.None, 0x87, true)]
    [InlineData(HotkeyModifiers.None, 0xAD, true)]
    [InlineData(HotkeyModifiers.None, 0xB3, true)]
    [InlineData(HotkeyModifiers.None, 0xB4, false)]
    [InlineData(HotkeyModifiers.Control, 0x4D, true)]
    [InlineData(HotkeyModifiers.Control, 0x10, false)]
    [InlineData(HotkeyModifiers.Control, 0x11, false)]
    [InlineData(HotkeyModifiers.Control, 0x12, false)]
    [InlineData(HotkeyModifiers.Control, 0x5B, false)]
    [InlineData(HotkeyModifiers.Control, 0x5C, false)]
    [InlineData(HotkeyModifiers.Control, 0xA0, false)]
    [InlineData(HotkeyModifiers.Control, 0xA5, false)]
    [InlineData(HotkeyModifiers.Control, 0, false)]
    [InlineData(HotkeyModifiers.Control, 255, false)]
    [InlineData((HotkeyModifiers)16, 0x4D, false)]
    public void ValidatesCombinations(HotkeyModifiers modifiers, int key, bool expected) => HotkeyFormatter.IsValidCombination(modifiers, key).Should().Be(expected);
}
