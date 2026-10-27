using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Tests;

public sealed class DuckingTests
{
    private sealed class Rig : IDisposable
    {
        public TestRig Core { get; } = new();
        public DuckingController Controller { get; }
        public Rig()
        {
            Core.Settings.UpdateAsync(s => s with { Ducking = new() { Enabled = true, SourceStrip = 0, TargetStrip = 1,
                ThresholdDb = -20, AmountDb = -12, AttackMilliseconds = 100, HoldMilliseconds = 200, ReleaseMilliseconds = 200 } }).GetAwaiter().GetResult();
            Controller = new(Core.Mixer, Core.Settings, Core.Time, NullLogger<DuckingController>.Instance);
            Controller.Start();
        }
        public float Gain => Core.Mixer.Snapshot.Strips[1].Gain;
        public void Frame(float peak, int milliseconds = 50)
        {
            Core.Time.Advance(TimeSpan.FromMilliseconds(milliseconds));
            Core.Mixer.PublishLevels(LevelFrame.Silent with { Strips = [new(0, peak), default, default, default, default] });
        }
        public void Attenuate() { Frame(1, 0); Frame(1); Frame(1); Assert.Equal(-12, Gain); }
        public void External(float gain) => Core.Mixer.PublishSnapshot(Core.Mixer.Snapshot with
        { Strips = Core.Mixer.Snapshot.Strips.Select(s => s.Index == 1 ? s with { Gain = gain } : s).ToArray() });
        public void Dispose() { Controller.Dispose(); Core.Dispose(); }
    }

    [Fact]
    public void AttackHoldAndReleaseUseStereoPeakAndDecibels()
    {
        using var r = new Rig();
        r.Frame(0.09f, 0); // -20,9 dBFS : sous le seuil, aucun gain écrit.
        r.Frame(0.09f);
        Assert.Empty(r.Core.Mixer.Floats);
        r.Frame(0.11f); // -19,2 dBFS, canal droit uniquement.
        r.Frame(0.11f);
        Assert.Equal(-6, r.Gain);
        r.Frame(0.11f);
        Assert.Equal(-12, r.Gain);
        r.Frame(0);
        for (var i = 0; i < 3; i++) r.Frame(0);
        Assert.Equal(-12, r.Gain); // 150 ms de silence : maintien encore actif.
        r.Frame(0);
        Assert.Equal(-12, r.Gain); // 200 ms : début du relâchement.
        r.Frame(0);
        Assert.Equal(-9, r.Gain);
        for (var i = 0; i < 3; i++) r.Frame(0);
        Assert.Equal(0, r.Gain);
    }

    [Fact]
    public void SignalDuringHoldRestartsTheWholeHold()
    {
        using var r = new Rig();
        r.Attenuate();
        r.Frame(0); r.Frame(0); r.Frame(0);
        r.Frame(1);
        r.Frame(0); r.Frame(0); r.Frame(0); r.Frame(0);
        Assert.Equal(-12, r.Gain);
        r.Frame(0); r.Frame(0);
        Assert.Equal(-9, r.Gain);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalGainBecomesTheNewBaseAndDisableRestoresIt(bool publish)
    {
        using var r = new Rig();
        r.Attenuate();
        if (publish) r.External(-4);
        else r.Core.Mixer.Snapshot = r.Core.Mixer.Snapshot with
        { Strips = r.Core.Mixer.Snapshot.Strips.Select(s => s.Index == 1 ? s with { Gain = -4 } : s).ToArray() };
        r.Frame(1); r.Frame(1); r.Frame(1);
        Assert.Equal(-16, r.Gain);
        r.Frame(0);
        for (var i = 0; i < 8; i++) r.Frame(0);
        Assert.Equal(-4, r.Gain);
        r.Frame(1); r.Frame(1); r.Frame(1);
        Assert.Equal(-16, r.Gain);
        await r.Core.Settings.UpdateAsync(s => s with { Ducking = s.Ducking with { Enabled = false } });
        Assert.Equal(-4, r.Gain);
        Assert.Equal(0, r.Core.Mixer.LevelRequests);
    }

    [Fact]
    public void DisposeRestoresAndUnsubscribes()
    {
        using var r = new Rig();
        r.Attenuate();
        r.Controller.Dispose(); r.Controller.Dispose();
        Assert.Equal(0, r.Gain);
        Assert.Equal(0, r.Core.Mixer.LevelRequests);
        var count = r.Core.Mixer.Floats.Count;
        r.Frame(1);
        Assert.Equal(count, r.Core.Mixer.Floats.Count);
        Assert.Throws<ObjectDisposedException>(r.Controller.Start);
    }

    [Fact]
    public async Task DisconnectionPreservesRestorationEvenWhenDisabledOffline()
    {
        using var r = new Rig();
        r.Attenuate();
        r.Core.Mixer.SetState(MixerConnectionState.Disconnected);
        var writes = r.Core.Mixer.Floats.Count;
        r.Frame(1);
        await r.Core.Settings.UpdateAsync(s => s with { Ducking = s.Ducking with { Enabled = false } });
        Assert.Equal(writes, r.Core.Mixer.Floats.Count);
        r.Core.Mixer.SetState(MixerConnectionState.Connected);
        r.Core.Mixer.PublishSnapshot(r.Core.Mixer.Snapshot);
        Assert.Equal(0, r.Gain);
        Assert.Equal(0, r.Core.Mixer.LevelRequests);
    }

    [Fact]
    public void ReconnectionUsesAnExternalGainInsteadOfAnObsoleteBase()
    {
        using var r = new Rig();
        r.Attenuate();
        r.Core.Mixer.SetState(MixerConnectionState.Disconnected);
        r.Core.Mixer.SetState(MixerConnectionState.Connected);
        r.External(-8);
        r.Frame(1); r.Frame(1); r.Frame(1);
        Assert.Equal(-20, r.Gain);
        r.Controller.Dispose();
        Assert.Equal(-8, r.Gain);
    }

    [Fact]
    public void FramesAndWritesAreLimitedAndSteadyStateDoesNotWrite()
    {
        using var r = new Rig();
        r.Frame(1, 0);
        for (var i = 0; i < 100; i++) r.Frame(1, 1);
        Assert.Equal(-12, r.Gain);
        Assert.Equal(2, r.Core.Mixer.Floats.Count);
        for (var i = 0; i < 1000; i++) r.Frame(1, 1);
        Assert.Equal(2, r.Core.Mixer.Floats.Count);
    }

    [Fact]
    public void DeferredAndCoalescedAcknowledgementsDoNotChangeTheBase()
    {
        using var r = new Rig();
        r.Core.Mixer.DeferWrites = true;
        r.Frame(1, 0); r.Frame(1); r.Frame(1);
        Assert.Equal(2, r.Core.Mixer.Floats.Count);
        r.Core.Mixer.FlushWrites();
        Assert.Equal(-12, r.Gain);
        r.Frame(1);
        r.Controller.Dispose();
        r.Core.Mixer.FlushWrites();
        Assert.Equal(0, r.Gain);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(-1, 1)]
    [InlineData(0, 5)]
    [InlineData(5, 1)]
    public async Task InvalidIndicesAreIgnoredAndReleaseTheLease(int source, int target)
    {
        using var r = new Rig();
        r.Attenuate();
        await r.Core.Settings.UpdateAsync(s => s with { Ducking = s.Ducking with { SourceStrip = source, TargetStrip = target } });
        Assert.Equal(0, r.Gain);
        Assert.Equal(0, r.Core.Mixer.LevelRequests);
        r.Frame(1);
        Assert.Equal(0, r.Gain);
    }

    [Fact]
    public async Task ChangingTargetRestoresOldTargetAndAcquiresOnlyOneLease()
    {
        using var r = new Rig();
        r.Attenuate();
        r.Controller.Start();
        await r.Core.Settings.UpdateAsync(s => s with { Ducking = s.Ducking with { TargetStrip = 2, AttackMilliseconds = 0 } });
        Assert.Equal(0, r.Gain);
        Assert.Equal(1, r.Core.Mixer.LevelRequests);
        r.Frame(1);
        Assert.Equal(-12, r.Core.Mixer.Snapshot.Strips[2].Gain);
        await r.Core.Settings.UpdateAsync(s => s with { ShowOsd = false });
        Assert.Equal(-12, r.Core.Mixer.Snapshot.Strips[2].Gain);
    }

    [Fact]
    public async Task ProfileFadeSuspendsDuckingThenUsesTheFinalGainAsBase()
    {
        using var r = new Rig();
        await r.Core.Profiles.SaveAsync(new() { Id = "p", Name = "Profil", Strips = [new(1, -5, false, [])] });
        await r.Core.Settings.UpdateAsync(s => s with { FadeMilliseconds = 100 });
        using var coordinator = new AutomationCoordinator(new FakeProcesses(), new FakeForeground(), r.Core.Profiles,
            r.Core.Mixer, r.Core.Settings, new FakeOsd(), NullLogger<AutomationCoordinator>.Instance, r.Controller);
        r.Attenuate();
        var apply = coordinator.ApplyManualProfileAsync("p");
        await TestRig.UntilAsync(() => r.Core.Profiles.Active?.Id == "p");
        var count = r.Core.Mixer.Floats.Count;
        r.Core.Mixer.PublishLevels(LevelFrame.Silent with { Strips = [new(1, 1), default, default, default, default] });
        Assert.Equal(count, r.Core.Mixer.Floats.Count);
        await TestRig.UntilAsync(() => { r.Core.Time.Advance(TimeSpan.FromMilliseconds(16)); return apply.IsCompleted; });
        await apply;
        Assert.Equal(-5, r.Gain);
        r.Frame(1); r.Frame(1); r.Frame(1);
        Assert.Equal(-17, r.Gain);
        r.Controller.Dispose();
        Assert.Equal(-5, r.Gain);
    }

    [Fact]
    public async Task PotatoCanAttenuateTheLastStripAndStandardReleasesItsLevelLease()
    {
        using var core = new TestRig();
        core.Mixer.Snapshot = MixerSnapshot.EmptyFor(MixerLayout.Potato);
        await core.Settings.UpdateAsync(s => s with { Ducking = new()
        {
            Enabled = true, SourceStrip = 6, TargetStrip = 7, ThresholdDb = -20,
            AmountDb = -12, AttackMilliseconds = 0,
        } });
        using var controller = new DuckingController(core.Mixer, core.Settings, core.Time, NullLogger<DuckingController>.Instance);
        controller.Start();
        Assert.Equal(1, core.Mixer.LevelRequests);
        core.Mixer.PublishLevels(LevelFrame.SilentFor(MixerLayout.Potato) with
        {
            Strips = [default, default, default, default, default, default, new(1, 1), default],
        });
        Assert.Equal(-12, core.Mixer.Snapshot.Strips[7].Gain);
        Assert.Equal(("Strip[7].Gain", -12f), Assert.Single(core.Mixer.Floats));

        core.Mixer.SetState(MixerConnectionState.Disconnected);
        core.Mixer.Snapshot = MixerSnapshot.EmptyFor(MixerLayout.Standard);
        core.Mixer.SetState(MixerConnectionState.Connected);
        core.Mixer.PublishSnapshot(core.Mixer.Snapshot);
        core.Time.Advance(TimeSpan.FromMilliseconds(50));
        core.Mixer.PublishLevels(LevelFrame.SilentFor(MixerLayout.Standard));
        Assert.Equal(0, core.Mixer.LevelRequests);
        Assert.Single(core.Mixer.Floats); // Pas de restauration de Strip[7] sur Standard.
        Assert.All(core.Mixer.Snapshot.Strips, s => Assert.Equal(0, s.Gain));

        core.Mixer.SetState(MixerConnectionState.Disconnected);
        core.Mixer.Snapshot = MixerSnapshot.EmptyFor(MixerLayout.Potato);
        core.Mixer.SetState(MixerConnectionState.Connected);
        core.Mixer.PublishSnapshot(core.Mixer.Snapshot);
        Assert.Equal(1, core.Mixer.LevelRequests); // Réévaluation sans changer les réglages.
    }

    [Fact]
    public async Task LayoutChangeDoesNotRestoreAnOldBaseOnAnExistingIndex()
    {
        using var r = new Rig();
        r.Attenuate();
        r.Core.Mixer.SetState(MixerConnectionState.Disconnected);
        r.Core.Mixer.Snapshot = MixerSnapshot.EmptyFor(MixerLayout.Standard) with
        {
            Strips = MixerSnapshot.EmptyFor(MixerLayout.Standard).Strips
                .Select(s => s.Index == 1 ? s with { Gain = -6 } : s).ToArray(),
        };
        var writes = r.Core.Mixer.Floats.Count;
        r.Core.Mixer.SetState(MixerConnectionState.Connected);
        r.Core.Mixer.PublishSnapshot(r.Core.Mixer.Snapshot);
        Assert.Equal(writes, r.Core.Mixer.Floats.Count);
        Assert.Equal(-6, r.Gain);
        await r.Core.Settings.UpdateAsync(s => s with { Ducking = s.Ducking with { Enabled = false } });
        Assert.Equal(-6, r.Gain);
        Assert.Equal(0, r.Core.Mixer.LevelRequests);
    }
}
