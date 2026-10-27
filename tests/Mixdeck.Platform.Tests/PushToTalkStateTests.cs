using Mixdeck.Core.Model;
using Mixdeck.Platform.Hotkeys;

namespace Mixdeck.Platform.Tests;

public sealed class PushToTalkStateTests
{
    [Fact]
    public void EmitsOnePressAndOneReleaseDespiteAutoRepeat()
    {
        var state = new PushToTalkState(new HotkeyBinding(HotkeyAction.PushToTalk, 0, 0x7C));
        state.Update(0x7C, true, 0).Should().BeTrue();
        state.Pressed.Should().BeTrue();
        for (int i = 0; i < 20; i++) state.Update(0x7C, true, 0).Should().BeFalse();
        state.Update(0x7C, false, HotkeyModifiers.Control).Should().BeTrue();
        state.Pressed.Should().BeFalse();
        state.Update(0x7C, false, 0).Should().BeFalse();
    }

    [Fact]
    public void ReleasingModifierStopsPttWithoutRetriggeringHeldKey()
    {
        var modifiers = HotkeyModifiers.Control | HotkeyModifiers.Alt;
        var state = new PushToTalkState(new HotkeyBinding(HotkeyAction.PushToTalk, modifiers, 0x4D));
        state.Update(0x4D, true, modifiers).Should().BeTrue();
        state.Update(0xA2, false, HotkeyModifiers.Alt).Should().BeTrue();
        state.Update(0xA2, true, modifiers).Should().BeFalse();
        state.Update(0x4D, true, modifiers).Should().BeFalse();
        state.Pressed.Should().BeFalse();
        state.Update(0x4D, false, modifiers).Should().BeFalse();
        state.Update(0x4D, true, modifiers).Should().BeTrue();
    }

    [Fact]
    public void AddingModifierToHeldKeyDoesNotCreateAPress()
    {
        var state = new PushToTalkState(new HotkeyBinding(HotkeyAction.PushToTalk, HotkeyModifiers.Control, 0x4D));
        state.Update(0x4D, true, 0).Should().BeFalse();
        state.Update(0xA2, true, HotkeyModifiers.Control).Should().BeFalse();
        state.Update(0x4D, true, HotkeyModifiers.Control).Should().BeFalse();
        state.Update(0x4D, false, HotkeyModifiers.Control).Should().BeFalse();
        state.Update(0x4D, true, HotkeyModifiers.Control).Should().BeTrue();
        state.Update(0x51, true, HotkeyModifiers.Control).Should().BeFalse();
        state.Pressed.Should().BeTrue();
    }
}
