using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Audio;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;

namespace Mixdeck.Core.Tests;

public sealed class LocalizedCoreTextTests
{
    [Fact]
    public void TranslatingPresetsChangesOnlyTheirNamesAndDescriptions()
    {
        var translated = MicPresets.GetAll(new TestTexts());

        translated.Should().HaveCount(MicPresets.All.Count);
        foreach (var preset in translated)
        {
            var original = MicPresets.All.Single(p => p.Preset == preset.Preset);
            preset.Name.Should().Be($"translated:Core_Preset{preset.Preset}Name");
            preset.Description.Should().Be($"translated:Core_Preset{preset.Preset}Description");
            (preset.Gate, preset.Comp, preset.ColorX, preset.ColorY)
                .Should().Be((original.Gate, original.Comp, original.ColorX, original.ColorY));
        }
    }

    [Fact]
    public async Task MicrophoneShortcutUsesTheInjectedTextProviderForBothStates()
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        using var automation = Coordinator(rig);
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd,
            NullLogger<ActionDispatcher>.Instance, automation, new TestTexts());
        var binding = new HotkeyBinding(HotkeyAction.ToggleMic, HotkeyModifiers.None, 0);

        await dispatcher.HandleAsync(new(binding, true));
        await dispatcher.HandleAsync(new(binding, true));

        osd.Messages.Select(m => m.Text).Should().Equal("translated:Core_MicMuted", "translated:Core_MicOn");
        osd.Messages.Select(m => m.Warning).Should().Equal(true, false);
    }

    [Theory]
    [InlineData("fr-FR", "A1 : −6,0 dB")]
    [InlineData("en-US", "A1 : −6.0 dB")]
    public async Task VolumeShortcutUsesTheSelectedDecimalCultureAndPreservesHardwareNames(string language, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
            using var rig = new TestRig();
            var osd = new FakeOsd();
            using var automation = Coordinator(rig);
            var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd,
                NullLogger<ActionDispatcher>.Instance, automation, new TestTexts());

            await dispatcher.HandleAsync(new(new(HotkeyAction.VolumeDown, HotkeyModifiers.None, 0, StepDb: 6), true));

            osd.Messages.Should().ContainSingle().Which.Text.Should().Be(expected);
            rig.Mixer.Floats.Should().ContainSingle().Which.Should().Be((VmParam.BusGain(0), -6f));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public async Task ApplyingProfileUsesLocalizedFormatAndKeepsTheUsersProfileName()
    {
        using var rig = new TestRig();
        await rig.Profiles.SaveAsync(new() { Id = "music", Name = "Ma musique", Icon = "♫" });
        var osd = new FakeOsd();
        using var automation = Coordinator(rig);
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd,
            NullLogger<ActionDispatcher>.Instance, automation, new TestTexts());

        await dispatcher.HandleAsync(new(new(HotkeyAction.ApplyProfile, HotkeyModifiers.None, 0, ProfileId: "music"), true));

        osd.Messages.Should().ContainSingle().Which.Text.Should().Be("Profile: Ma musique");
    }

    private static AutomationCoordinator Coordinator(TestRig rig) => new(new FakeProcesses(), new FakeForeground(),
        rig.Profiles, rig.Mixer, rig.Settings, new FakeOsd(), NullLogger<AutomationCoordinator>.Instance);

    private sealed class TestTexts : ITextProvider
    {
        public string Get(string key) => key switch
        {
            "Core_Profile" => "Profile: {0}",
            "Core_Gain" => "{0} : {1} dB",
            _ => "translated:" + key
        };
    }
}
