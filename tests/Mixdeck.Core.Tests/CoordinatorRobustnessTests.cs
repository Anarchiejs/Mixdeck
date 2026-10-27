using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;
namespace Mixdeck.Core.Tests;
public sealed class CoordinatorRobustnessTests
{
    private static AutomationCoordinator Coordinator(TestRig rig, FakeProcesses processes, FakeOsd? osd = null) =>
        new(processes, new FakeForeground(), rig.Profiles, rig.Mixer, rig.Settings, osd ?? new FakeOsd(), NullLogger<AutomationCoordinator>.Instance);
    private static Profile BusProfile(string id, float gain) => new() { Id = id, Name = id, Buses = [new BusProfile(0, gain, false)] };
    [Fact]
    public async Task DecisionsTakenWhileDisconnectedAreReplayedOnReconnect()
    {
        using var rig = new TestRig();
        await rig.Profiles.SaveAsync(BusProfile("game", -10));
        await rig.Settings.UpdateAsync(s => s with { Rules = [
            new() { Id = "p", Name = "Jeu", ProcessName = "game", ProfileId = "game" },
            new() { Id = "m", Name = "Micro", ProcessName = "game", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        rig.Mixer.SetState(MixerConnectionState.VoicemeeterNotRunning);
        var processes = new FakeProcesses { Running = ["game.exe"] };
        using var coordinator = Coordinator(rig, processes);
        coordinator.Start();
        await Task.Delay(100);
        Assert.Empty(rig.Mixer.Floats);
        rig.Mixer.SetState(MixerConnectionState.Connected);
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Strips[0].Mute && rig.Mixer.Snapshot.Buses[0].Gain == -10);
        Assert.Equal("game", rig.Profiles.Active?.Id);
        // Rien n'est rejoué deux fois : une seconde reconnexion n'écrit plus rien.
        var writes = rig.Mixer.Floats.Count;
        rig.Mixer.SetState(MixerConnectionState.Disconnected);
        rig.Mixer.SetState(MixerConnectionState.Connected);
        await coordinator.ApplyManualProfileAsync("game");
        Assert.Equal(writes + 2, rig.Mixer.Floats.Count); // Bus[0].Mute et Bus[0].Gain du profil manuel, rien d'autre
        var afterManual = rig.Mixer.Floats.Count;
        processes.ExitProcess("game");
        await TestRig.UntilAsync(() => !rig.Mixer.Snapshot.Strips[0].Mute);
        Assert.Equal(afterManual + 1, rig.Mixer.Floats.Count);
    }
    [Fact]
    public async Task OfflineDecisionsKeepOnlyTheLatestState()
    {
        using var rig = new TestRig();
        await rig.Settings.UpdateAsync(s => s with { Rules = [
            new() { Id = "m", Name = "Micro", ProcessName = "mic", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        rig.Mixer.SetState(MixerConnectionState.Disconnected);
        var processes = new FakeProcesses();
        using var coordinator = Coordinator(rig, processes);
        coordinator.Start();
        processes.StartProcess("mic");
        processes.ExitProcess("mic");
        processes.StartProcess("mic");
        await Task.Delay(50);
        rig.Mixer.SetState(MixerConnectionState.Connected);
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Strips[0].Mute);
        Assert.Single(rig.Mixer.Floats);
    }
    [Fact]
    public async Task RunningFadeDoesNotDelayLaterDecisions()
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        await rig.Profiles.SaveAsync(BusProfile("a", -20));
        await rig.Profiles.SaveAsync(BusProfile("b", -30));
        await rig.Settings.UpdateAsync(s => s with { FadeMilliseconds = 300, Rules = [
            new() { Id = "a", Name = "A", ProcessName = "a", ProfileId = "a" },
            new() { Id = "b", Name = "B", ProcessName = "b", ProfileId = "b" },
            new() { Id = "m", Name = "Micro", ProcessName = "mic", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        var processes = new FakeProcesses();
        using var coordinator = Coordinator(rig, processes, osd);
        coordinator.Start();
        processes.StartProcess("a");
        processes.StartProcess("b");
        processes.StartProcess("mic");
        // Le temps simulé n'avance pas : aucun fondu ne peut se terminer, la coupure micro doit passer quand même.
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Strips[0].Mute);
        Assert.Equal("b", rig.Profiles.Active?.Id);
        await TestRig.UntilAsync(() => { rig.Time.Advance(TimeSpan.FromMilliseconds(16)); return rig.Mixer.Snapshot.Buses[0].Gain == -30; });
        await TestRig.UntilAsync(() => osd.Messages.Any(m => m.Text == "Profil : b (b)"));
        Assert.DoesNotContain(osd.Messages, m => m.Text.StartsWith("Profil : a", StringComparison.Ordinal));
    }
    [Fact]
    public async Task MicDecisionWinsOverTheMuteRestoredAtTheEndOfTheFade()
    {
        using var rig = new TestRig();
        rig.Mixer.SetFloat("Strip[0].Mute", 1);
        await rig.Profiles.SaveAsync(new() { Id = "talk", Name = "Parler", Strips = [new StripProfile(0, -6, true, [true, false, false, false, false])] });
        await rig.Settings.UpdateAsync(s => s with { FadeMilliseconds = 100, Rules = [
            new() { Id = "p", Name = "Profil", ProcessName = "talk", ProfileId = "talk" },
            new() { Id = "m", Name = "Micro", ProcessName = "talk", Action = RuleActionKind.SetMicMuted, MicMuted = false }] });
        var processes = new FakeProcesses();
        using var coordinator = Coordinator(rig, processes);
        coordinator.Start();
        processes.StartProcess("talk");
        await TestRig.UntilAsync(() => !rig.Mixer.Snapshot.Strips[0].Mute);
        await TestRig.UntilAsync(() => { rig.Time.Advance(TimeSpan.FromMilliseconds(16)); return rig.Mixer.Snapshot.Strips[0].Gain == -6; });
        await TestRig.UntilAsync(() => rig.Mixer.Floats.Last() == ("Strip[0].Mute", 0f));
        Assert.False(rig.Mixer.Snapshot.Strips[0].Mute);
    }

    [Theory]
    [InlineData(3, 7, true)]
    [InlineData(1, 4, false)]
    public async Task MicrophoneRulesRespectTheCurrentLayout(int type, int micIndex, bool expectedWrite)
    {
        using var rig = new TestRig();
        rig.Mixer.Snapshot = MixerSnapshot.EmptyFor(MixerLayout.ForType(type));
        await rig.Profiles.SaveAsync(new() { Id = "barrier", Name = "Vide" });
        await rig.Settings.UpdateAsync(s => s with { MicStripIndex = micIndex, Rules = [
            new() { Id = "m", Name = "Micro", ProcessName = "mic", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        var processes = new FakeProcesses { Running = ["mic"] };
        using var coordinator = Coordinator(rig, processes);
        coordinator.Start();
        // L'action manuelle passe après l'initialisation dans la même file.
        await coordinator.ApplyManualProfileAsync("barrier");
        if (expectedWrite)
        {
            Assert.Equal(($"Strip[{micIndex}].Mute", 1f), Assert.Single(rig.Mixer.Floats));
            Assert.True(rig.Mixer.Snapshot.Strips[micIndex].Mute);
        }
        else Assert.Empty(rig.Mixer.Floats);
    }
}
