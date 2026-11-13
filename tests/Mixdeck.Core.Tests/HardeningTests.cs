using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Audio;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Diagnostics;
using Mixdeck.Core.Model;
using Mixdeck.Core.Profiles;
using Mixdeck.Core.Rules;
using Mixdeck.Core.Settings;
using Mixdeck.Core.Setup;

namespace Mixdeck.Core.Tests;

public sealed class HardeningTests
{
    [Fact]
    public void Hotkey_conflicts_match_the_same_chord()
    {
        var mute = new HotkeyBinding(HotkeyAction.ToggleMic, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D);
        var flyout = new HotkeyBinding(HotkeyAction.OpenFlyout, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x4D);
        var other = new HotkeyBinding(HotkeyAction.VolumeUp, HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x26);
        var conflicts = HotkeyConflicts.Find([mute, flyout, other], flyout);
        Assert.Equal(HotkeyAction.ToggleMic, Assert.Single(conflicts).Action);
        Assert.Empty(HotkeyConflicts.Find([mute, other], mute));
    }

    [Theory]
    [InlineData("Chrome", "chrome.exe", "chr", true)]
    [InlineData("Spotify", "Spotify.exe", "vlc", false)]
    [InlineData("Anything", "app.exe", "  ", true)]
    public void App_filter_matches_display_or_process(string display, string process, string query, bool expected) =>
        Assert.Equal(expected, AppSessionFilter.Matches(display, process, query));

    [Fact]
    public void Crash_copy_hides_the_user_profile()
    {
        var report = "boom at C:\\Users\\ada\\AppData\\Local\\Mixdeck\\logs\\mixdeck.log";
        var redacted = CrashReportRedactor.Redact(report, "C:\\Users\\ada");
        Assert.DoesNotContain("ada", redacted);
        Assert.Contains("<user>", redacted);
    }

    [Fact]
    public async Task Empty_profile_folder_receives_four_presets()
    {
        using var rig = new TestRig();
        await rig.Profiles.LoadAsync();
        Assert.Equal(
            [ProfilePresets.CallId, ProfilePresets.GameId, ProfilePresets.MusicId, ProfilePresets.StreamId],
            rig.Profiles.Profiles.Select(profile => profile.Id).ToArray());
        Assert.Equal(5, rig.Profiles.Profiles[0].Strips.Count);
        Assert.True(File.Exists(Path.Combine(rig.Paths.ProfilesDirectory, ProfilePresets.CallId + ".json")));
    }

    [Fact]
    public async Task Existing_profiles_are_not_replaced_by_presets()
    {
        using var rig = new TestRig();
        await rig.Profiles.SaveAsync(new Profile { Id = "mine", Name = "Mine" });
        await rig.Profiles.LoadAsync();
        Assert.Equal("mine", Assert.Single(rig.Profiles.Profiles).Id);
    }

    [Fact]
    public void Rule_engine_applies_and_reverts_bus_strip_and_route()
    {
        var engine = new RuleEngine();
        engine.SetBaselineProfile("base");
        engine.SetRules(
        [
            new Rule { Id = "bus", Name = "Bus", ProcessName = "game", Action = RuleActionKind.SetBusGain, BusIndex = 1, GainDb = -6, RevertWhenInactive = true },
            new Rule { Id = "strip", Name = "Strip", ProcessName = "game", Action = RuleActionKind.SetStripMute, StripIndex = 2, MicMuted = true, RevertWhenInactive = true },
            new Rule { Id = "route", Name = "Route", ProcessName = "game", Action = RuleActionKind.RouteApp, OutputEndpointId = "speakers", RevertWhenInactive = true },
        ]);
        var on = engine.OnProcessStarted("game.exe");
        Assert.True(on.BusGainChanged);
        Assert.Equal(-6, on.BusGainRule!.GainDb);
        Assert.True(on.StripMuteChanged);
        Assert.Equal(2, on.StripMuteRule!.StripIndex);
        Assert.True(on.RouteChanged);
        Assert.Equal("speakers", on.RouteRule!.OutputEndpointId);
        var stay = engine.Evaluate();
        Assert.False(stay.BusGainChanged);
        var off = engine.OnProcessExited("game");
        Assert.True(off.BusGainChanged);
        Assert.Null(off.BusGainRule);
        Assert.True(off.StripMuteChanged);
        Assert.Null(off.StripMuteRule);
        Assert.True(off.RouteChanged);
        Assert.Null(off.RouteRule);
    }

    [Fact]
    public async Task Coordinator_osd_follows_the_text_provider()
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        var texts = new EnglishTexts();
        using var coordinator = new AutomationCoordinator(new FakeProcesses(), new FakeForeground(), rig.Profiles, rig.Mixer, rig.Settings, osd,
            NullLogger<AutomationCoordinator>.Instance, texts: texts);
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd, NullLogger<ActionDispatcher>.Instance, coordinator, texts);
        await dispatcher.HandleAsync(new HotkeyEventArgs(new HotkeyBinding(HotkeyAction.ToggleMic, HotkeyModifiers.None, 0), true));
        Assert.Equal("Microphone muted", osd.Messages.Last().Text);
    }

    [Fact]
    public async Task Failed_settings_write_returns_false()
    {
        using var rig = new TestRig();
        var store = new SettingsStore(rig.Paths, NullLogger<SettingsStore>.Instance);
        await store.LoadAsync();
        if (File.Exists(rig.Paths.SettingsFile)) File.Delete(rig.Paths.SettingsFile);
        Directory.CreateDirectory(rig.Paths.SettingsFile);
        var saved = await store.UpdateAsync(settings => settings with { ShowOsd = false });
        Assert.False(saved);
        Assert.True(store.Current.ShowOsd);
    }

    [Fact]
    public void Fl_script_copy_lands_in_documents()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-script-" + Guid.NewGuid().ToString("N"));
        var documents = Path.Combine(root, "docs");
        var bundled = Path.Combine(root, "fl", "Mixdeck", FlScriptInstall.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(bundled)!);
        File.WriteAllText(bundled, "print('mixdeck')");
        var found = FlScriptInstall.FindBundledScript(Path.Combine(root, "bin"));
        Assert.Equal(bundled, found);
        var copied = FlScriptInstall.CopyToDocuments(found!, documents);
        Assert.Equal("print('mixdeck')", File.ReadAllText(copied));
        Directory.Delete(root, true);
    }

    [Fact]
    public void Rule_draft_builds_each_action()
    {
        var original = new Rule { Id = "r", Name = "Rule", ProcessName = "game" };
        var bus = RuleDraft.Build(original, "", "game", 0, 3, null, 1, -6.5f, 0, null, true, appendExe: false);
        Assert.Equal(RuleActionKind.SetBusGain, bus.Action);
        Assert.Equal(-6.5f, bus.GainDb);
        Assert.Equal("game", bus.ProcessName);
        var routed = RuleDraft.Build(original, "Route", "chrome", 1, 5, null, 0, 0, 0, "speakers", false, appendExe: true);
        Assert.Equal("chrome.exe", routed.ProcessName);
        Assert.Equal("speakers", routed.OutputEndpointId);
        Assert.Equal(RuleTrigger.ProcessForeground, routed.Trigger);
        Assert.Equal(4, RuleDraft.ActionIndex(original with { Action = RuleActionKind.SetStripMute }));
    }

    [Fact]
    public void View_model_sources_do_not_hardcode_french_osd()
    {
        foreach (var relative in new[]
        {
            "src/Mixdeck.Core/Automation/AutomationCoordinator.cs",
            "src/Mixdeck.App/ViewModels/Pages/RulesPageViewModel.cs",
            "src/Mixdeck.App/ViewModels/Pages/SettingsPageViewModel.cs",
        })
        {
            var text = File.ReadAllText(Find(relative));
            Assert.DoesNotContain("Micro coupé", text);
            Assert.DoesNotContain("Profil : ", text);
        }
    }

    [Fact]
    public void Automation_source_does_not_hardcode_french_osd()
    {
        var file = Find("src/Mixdeck.Core/Automation/AutomationCoordinator.cs");
        var text = File.ReadAllText(file);
        Assert.DoesNotContain("Micro coupé", text);
        Assert.DoesNotContain("Micro activé", text);
        Assert.DoesNotContain("Profil :", text);
    }

    private static string Find(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    private sealed class EnglishTexts : ITextProvider
    {
        public CultureInfo Culture => CultureInfo.GetCultureInfo("en-US");
        public string Get(string key) => key switch
        {
            "Core_MicMuted" => "Microphone muted",
            "Core_MicOn" => "Microphone on",
            "Core_Profile" => "Profile: {0}",
            _ => key,
        };
    }
}
