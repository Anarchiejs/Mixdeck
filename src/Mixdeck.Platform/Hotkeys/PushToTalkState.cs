using Mixdeck.Core.Model;

namespace Mixdeck.Platform.Hotkeys;

internal sealed class PushToTalkState(HotkeyBinding binding)
{
    private bool _keyDown;
    internal bool Pressed { get; private set; }

    internal bool Update(int key, bool down, HotkeyModifiers modifiers)
    {
        bool previous = Pressed;
        if (key == binding.VirtualKey)
        {
            if (!down) Pressed = false;
            else if (!_keyDown && modifiers == binding.Modifiers) Pressed = true;
            _keyDown = down;
        }
        if (modifiers != binding.Modifiers) Pressed = false;
        return previous != Pressed;
    }
}
