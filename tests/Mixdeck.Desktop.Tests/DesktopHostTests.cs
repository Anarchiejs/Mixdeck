using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;
using Mixdeck.Desktop;
using Mixdeck.FlStudio;

namespace Mixdeck.Desktop.Tests;

public sealed class DesktopTextTests
{
    [Fact]
    public void Every_key_has_english_and_french()
    {
        DesktopText.Keys.Should().OnlyHaveUniqueItems();
        foreach (var key in DesktopText.Keys)
            DesktopText.HasText(key).Should().BeTrue(key);
        DesktopText.Apply("en-US");
        DesktopText.Get("Core_MicMuted").Should().Be("Microphone muted");
        DesktopText.Apply("fr-FR");
        DesktopText.Get("Core_MicMuted").Should().Be("Micro coupé");
    }
}

public sealed class DesktopOptionsTests
{
    [Fact]
    public void Parses_demo_and_self_check()
    {
        var options = DesktopOptions.Parse(["--demo=potato", "--self-check", "--minimized"]);
        options.Demo.Should().BeTrue();
        options.DemoLayout.Should().Be(MixerLayout.Potato);
        options.SelfCheck.Should().BeTrue();
        options.Minimized.Should().BeTrue();
    }
}

public sealed class DesktopHostTests
{
    [Fact]
    public async Task Starts_and_stops_without_a_window()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-host-" + Guid.NewGuid().ToString("N"));
        var host = DesktopHost.Create(new DesktopOptions(false, true, true, false), new MixdeckPaths(root));
        try
        {
            await host.StartAsync();
            if (!OperatingSystem.IsWindows())
                host.Mixer.State.Should().Be(MixerConnectionState.NotInstalled);
            if (OperatingSystem.IsLinux())
            {
                host.Surfaces.Capabilities.Voicemeeter.Should().BeFalse();
                host.Surfaces.Capabilities.FlStudio.Should().BeFalse();
                host.Midi.Should().BeOfType<UnsupportedMidiPorts>();
            }
            if (OperatingSystem.IsMacOS())
                host.Midi.Should().BeOfType<CoreMidiPorts>();
        }
        finally
        {
            await host.DisposeAsync();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
