using System.Runtime.InteropServices;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Desktop.Hotkeys;
using Mixdeck.Platform.Desktop.Instance;
using Mixdeck.Platform.Desktop.Native;
using Mixdeck.Platform.Desktop.Startup;

namespace Mixdeck.Platform.Desktop.Tests;

public sealed class VirtualKeyMapTests
{
    [Fact]
    public void Translates_the_default_chords()
    {
        VirtualKeyMap.TryX11Keysym(0x4D, out var letter).Should().BeTrue();
        letter.Should().Be(0x6D);
        VirtualKeyMap.TryX11Keysym(0x26, out var up).Should().BeTrue();
        up.Should().Be(0xff52);
        VirtualKeyMap.TryX11Keysym(0x7C, out var f13).Should().BeTrue();
        f13.Should().Be(0xffca);

        VirtualKeyMap.TryMacKeyCode(0x4D, out var macM).Should().BeTrue();
        macM.Should().Be(0x2E);
        VirtualKeyMap.TryMacKeyCode(0x25, out var left).Should().BeTrue();
        left.Should().Be(0x7B);
        VirtualKeyMap.TryMacKeyCode(0x70, out var f1).Should().BeTrue();
        f1.Should().Be(0x7A);

        var masks = VirtualKeyMap.X11GrabMasks(HotkeyModifiers.Control | HotkeyModifiers.Alt);
        masks.Should().Equal(new uint[] { 12, 14, 28, 30 });
        VirtualKeyMap.Format(AppSettings.DefaultHotkeys[0]).Should().Contain("Ctrl").And.Contain("M");
    }
}

public sealed class AutoStartTests
{
    [Fact]
    public void Xdg_entry_is_created_and_removed()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-autostart-" + Guid.NewGuid().ToString("N"));
        var start = new XdgAutoStart(root, "/usr/bin/mixdeck");
        start.IsEnabled.Should().BeFalse();
        start.SetEnabled(true);
        var text = File.ReadAllText(Path.Combine(root, "mixdeck.desktop"));
        text.Should().Contain("Exec=\"/usr/bin/mixdeck\" --minimized");
        text.Should().NotContain("\r");
        start.IsEnabled.Should().BeTrue();
        start.SetEnabled(false);
        File.Exists(Path.Combine(root, "mixdeck.desktop")).Should().BeFalse();
    }

    [Fact]
    public void Launch_agent_escapes_the_executable()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-agent-" + Guid.NewGuid().ToString("N"));
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

public sealed class X11SessionTests
{
    [Fact]
    public async Task Grab_receives_a_synthetic_key_when_a_display_is_available()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return;
        var session = X11Session.TryOpen(out var reason);
        if (session is null)
            return;

        using (session)
        {
            var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var binding = new HotkeyBinding(HotkeyAction.ToggleMic, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D);
            session.SetHotkeys([binding], (_, pressed) =>
            {
                if (pressed) seen.TrySetResult(true);
            }, failed =>
            {
                if (failed.Count > 0) seen.TrySetException(new Xunit.Sdk.XunitException("X11 refused the grab: " + reason));
            });

            await Task.Delay(400);
            XTest.TryTap(0x6d, control: true, alt: true).Should().BeTrue("XTest should send Ctrl+Alt+M");

            var completed = await Task.WhenAny(seen.Task, Task.Delay(2000));
            completed.Should().Be(seen.Task, "the X11 grab should see Ctrl+Alt+M");
            (await seen.Task).Should().BeTrue();
        }
    }

    private static class XTest
    {
        public static bool TryTap(uint keysym, bool control, bool alt)
        {
            try
            {
                if (!NativeLibrary.TryLoad("libX11.so.6", out var x11)) return false;
                if (!NativeLibrary.TryLoad("libXtst.so.6", out var xtst)) return false;
                var open = Marshal.GetDelegateForFunctionPointer<OpenDisplay>(NativeLibrary.GetExport(x11, "XOpenDisplay"));
                var close = Marshal.GetDelegateForFunctionPointer<CloseDisplay>(NativeLibrary.GetExport(x11, "XCloseDisplay"));
                var keycode = Marshal.GetDelegateForFunctionPointer<KeysymToKeycode>(NativeLibrary.GetExport(x11, "XKeysymToKeycode"));
                var fake = Marshal.GetDelegateForFunctionPointer<FakeKeyEvent>(NativeLibrary.GetExport(xtst, "XTestFakeKeyEvent"));
                var flush = Marshal.GetDelegateForFunctionPointer<FlushDisplay>(NativeLibrary.GetExport(x11, "XFlush"));
                var display = open(Environment.GetEnvironmentVariable("DISPLAY"));
                if (display == 0) return false;
                try
                {
                    var key = keycode(display, keysym);
                    var ctrl = keycode(display, 0xffe3);
                    var option = keycode(display, 0xffe9);
                    if (key == 0) return false;
                    if (control) fake(display, ctrl, true, 0);
                    if (alt) fake(display, option, true, 0);
                    flush(display);
                    Thread.Sleep(40);
                    fake(display, key, true, 0);
                    flush(display);
                    Thread.Sleep(80);
                    fake(display, key, false, 0);
                    if (alt) fake(display, option, false, 0);
                    if (control) fake(display, ctrl, false, 0);
                    flush(display);
                    return true;
                }
                finally { close(display); }
            }
            catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr OpenDisplay(string? name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CloseDisplay(IntPtr display);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte KeysymToKeycode(IntPtr display, nuint keysym);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private delegate int FakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int FlushDisplay(IntPtr display);
    }
}
