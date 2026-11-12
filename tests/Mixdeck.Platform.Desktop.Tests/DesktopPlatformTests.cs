using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Desktop.Hotkeys;
using Mixdeck.Platform.Desktop.Instance;
using Mixdeck.Platform.Desktop.Startup;

namespace Mixdeck.Platform.Desktop.Tests;

public sealed class VirtualKeyMapTests
{
    [Fact]
    public void Translates_the_default_chords_to_mac_keycodes()
    {
        VirtualKeyMap.TryMacKeyCode(0x4D, out var macM).Should().BeTrue();
        macM.Should().Be(0x2E);
        VirtualKeyMap.TryMacKeyCode(0x25, out var left).Should().BeTrue();
        left.Should().Be(0x7B);
        VirtualKeyMap.TryMacKeyCode(0x70, out var f1).Should().BeTrue();
        f1.Should().Be(0x7A);
        VirtualKeyMap.MacModifiers(HotkeyModifiers.Control | HotkeyModifiers.Alt)
            .Should().Be(VirtualKeyMap.MacControl | VirtualKeyMap.MacOption);
        VirtualKeyMap.Format(AppSettings.DefaultHotkeys[0]).Should().Contain("Ctrl").And.Contain("M");
    }
}

public sealed class AutoStartTests
{
    [Fact]
    public void Launch_agent_escapes_the_executable()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-test-" + Guid.NewGuid().ToString("N"));
        var start = new LaunchAgentAutoStart(root, "/Users/me/Mixdeck & Co");
        start.SetEnabled(true);
        var text = File.ReadAllText(Path.Combine(root, "com.mixdeck.desktop.plist"));
        text.Should().Contain("/Users/me/Mixdeck &amp; Co");
        start.SetEnabled(false);
        start.IsEnabled.Should().BeFalse();
    }
}

public sealed class InstanceGateTests
{
    [Fact]
    public async Task Second_instance_asks_the_first_to_show()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-instance-" + Guid.NewGuid().ToString("N"));
        using var first = InstanceGate.Acquire(root);
        using var second = InstanceGate.Acquire(root);
        first.IsFirst.Should().BeTrue();
        second.IsFirst.Should().BeFalse();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.ShowRequested += () => shown.TrySetResult();
        second.SignalShow();
        var completed = await Task.WhenAny(shown.Task, Task.Delay(2000));
        completed.Should().Be(shown.Task);
    }
}
