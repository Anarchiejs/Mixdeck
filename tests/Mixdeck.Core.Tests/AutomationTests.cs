using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;
namespace Mixdeck.Core.Tests;
public sealed class AutomationTests
{
    [Fact]
    public async Task TogglePttVolumeBoundsAndFrenchOsd()
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd, NullLogger<ActionDispatcher>.Instance);
        Task Send(HotkeyAction action, bool pressed = true, float step = 2, int bus = 0) => dispatcher.HandleAsync(new(new(action, HotkeyModifiers.None, 0, bus, step), pressed));
        await Send(HotkeyAction.ToggleMic);
        Assert.True(rig.Mixer.Snapshot.Strips[0].Mute);
        Assert.Equal(("Micro coupé", "\uF781", true), osd.Messages.Last());
        await Send(HotkeyAction.ToggleMic, false);
        Assert.True(rig.Mixer.Snapshot.Strips[0].Mute);
        await Send(HotkeyAction.PushToTalk);
        Assert.False(rig.Mixer.Snapshot.Strips[0].Mute);
        await Send(HotkeyAction.PushToTalk, false);
        Assert.True(rig.Mixer.Snapshot.Strips[0].Mute);
        await Send(HotkeyAction.VolumeDown, step: 6.5f);
        Assert.Equal(("A1 : −6,5 dB", "\uE767", false), osd.Messages.Last());
        await Send(HotkeyAction.VolumeDown, step: 100);
        Assert.Equal(-60, rig.Mixer.Snapshot.Buses[0].Gain);
        await Send(HotkeyAction.VolumeUp, step: 100);
        Assert.Equal(12, rig.Mixer.Snapshot.Buses[0].Gain);
        var count = rig.Mixer.Floats.Count;
        await Send(HotkeyAction.VolumeUp, bus: 9);
        Assert.Equal(count, rig.Mixer.Floats.Count);
        var flyouts = 0;
        dispatcher.FlyoutRequested += (_, _) => flyouts++;
        await Send(HotkeyAction.OpenFlyout);
        Assert.Equal(1, flyouts);
        var messages = osd.Messages.Count;
        await rig.Settings.UpdateAsync(s => s with { ShowOsd = false });
        await Send(HotkeyAction.ToggleMic);
        Assert.Equal(messages, osd.Messages.Count);
    }
    [Fact]
    public async Task ProfileShortcutsCycleAndShowProfileIcon()
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        await rig.Profiles.SaveAsync(new() { Id = "a", Name = "Musique", Icon = "\uE8D6" });
        await rig.Profiles.SaveAsync(new() { Id = "b", Name = "Jeu" });
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd, NullLogger<ActionDispatcher>.Instance);
        await dispatcher.HandleAsync(new(new(HotkeyAction.NextProfile, HotkeyModifiers.None, 0), true));
        Assert.Equal("a", rig.Profiles.Active?.Id);
        Assert.Equal("Profil : Musique", osd.Messages.Last().Text);
        await dispatcher.HandleAsync(new(new(HotkeyAction.PreviousProfile, HotkeyModifiers.None, 0), true));
        Assert.Equal("b", rig.Profiles.Active?.Id);
    }
    [Fact]
    public async Task CoordinatorAppliesInitialRulesReloadsAndPersistsManualBaseline()
    {
        using var rig = new TestRig();
        var processes = new FakeProcesses { Running = ["FL64.exe"] };
        var foreground = new FakeForeground();
        var osd = new FakeOsd();
        await rig.Profiles.SaveAsync(new() { Id = "base", Name = "Base" });
        await rig.Profiles.SaveAsync(new() { Id = "music", Name = "Musique" });
        var rule = new Rule { Id = "r", Name = "Musique", ProcessName = "FL64.exe", ProfileId = "music" };
        await rig.Settings.UpdateAsync(s => s with { LastProfileId = "base", Rules = [rule] });
        using var coordinator = new AutomationCoordinator(processes, foreground, rig.Profiles, rig.Mixer, rig.Settings, osd, NullLogger<AutomationCoordinator>.Instance);
        coordinator.Start();
        coordinator.Start();
        await TestRig.UntilAsync(() => osd.Messages.Any(m => m.Text == "Profil : Musique (FL64.exe)"));
        Assert.Equal("music", rig.Profiles.Active?.Id);
        processes.ExitProcess("fl64");
        await TestRig.UntilAsync(() => rig.Profiles.Active?.Id == "base");
        await coordinator.ApplyManualProfileAsync("music");
        Assert.Equal("music", rig.Settings.Current.LastProfileId);
        await rig.Settings.UpdateAsync(s => s with { Rules = [rule with { Action = RuleActionKind.SetMicMuted, MicMuted = true, Trigger = RuleTrigger.ProcessForeground }] });
        foreground.Change("fl64");
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Strips[0].Mute);
        foreground.Change(null);
        await TestRig.UntilAsync(() => !rig.Mixer.Snapshot.Strips[0].Mute);
        coordinator.Dispose();
        var count = rig.Mixer.Floats.Count;
        processes.StartProcess("FL64.exe");
        foreground.Change("fl64");
        Assert.Equal(count, rig.Mixer.Floats.Count);
        Assert.Throws<ObjectDisposedException>(() => coordinator.Start());
    }
    [Fact]
    public async Task CoordinatorSerializesBurstsAndContinuesAfterFailedProfile()
    {
        using var rig = new TestRig();
        var processes = new FakeProcesses();
        var foreground = new FakeForeground();
        await rig.Profiles.SaveAsync(new() { Id = "base", Name = "Base" });
        await rig.Settings.UpdateAsync(s => s with { LastProfileId = "base", Rules = [
            new() { Id = "bad", Name = "Absent", ProcessName = "bad", ProfileId = "missing" },
            new() { Id = "mic", Name = "Micro", ProcessName = "mic", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        using var coordinator = new AutomationCoordinator(processes, foreground, rig.Profiles, rig.Mixer, rig.Settings, new FakeOsd(), NullLogger<AutomationCoordinator>.Instance);
        coordinator.Start();
        processes.StartProcess("bad");
        for (var i = 0; i < 30; i++) { processes.StartProcess("mic"); processes.ExitProcess("mic"); }
        await coordinator.ApplyManualProfileAsync("base");
        Assert.False(rig.Mixer.Snapshot.Strips[0].Mute);
        Assert.Equal(60, rig.Mixer.Floats.Count);
    }
    [Fact]
    public async Task MissingProfileDoesNotPreventMicrophoneDecision()
    {
        using var rig = new TestRig();
        var processes = new FakeProcesses { Running = ["app"] };
        await rig.Settings.UpdateAsync(s => s with { Rules = [
            new() { Id = "profile", Name = "Profil absent", ProcessName = "app", ProfileId = "missing" },
            new() { Id = "mic", Name = "Micro", ProcessName = "app", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        using var coordinator = new AutomationCoordinator(processes, new FakeForeground(), rig.Profiles, rig.Mixer, rig.Settings, new FakeOsd(), NullLogger<AutomationCoordinator>.Instance);
        coordinator.Start();
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Strips[0].Mute);
    }
}
