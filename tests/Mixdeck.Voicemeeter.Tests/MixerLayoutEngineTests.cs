using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mixdeck.Core.Model;

namespace Mixdeck.Voicemeeter.Tests;

public sealed class MixerLayoutEngineTests
{
    [Theory]
    [InlineData(1, new[] { 0, 2, 4 }, new[] { 0, 8 })]
    [InlineData(2, new[] { 0, 2, 4, 6, 14 }, new[] { 0, 8, 16, 24, 32 })]
    [InlineData(3, new[] { 0, 2, 4, 6, 8, 10, 18, 26 }, new[] { 0, 8, 16, 24, 32, 40, 48, 56 })]
    public async Task SimulatorConnectsAndSamplesEachLayout(int type, int[] inputOffsets, int[] outputOffsets)
    {
        var layout = MixerLayout.ForType(type);
        var api = new SimulatedVoicemeeterApi(42, layout);
        await using var mixer = new VoicemeeterMixer(api, NullLogger<VoicemeeterMixer>.Instance, new FakeTimeProvider());
        var connectedLayouts = new List<MixerLayout>();
        mixer.StateChanged += (_, state) =>
        {
            if (state == MixerConnectionState.Connected) connectedLayouts.Add(mixer.Layout);
        };
        mixer.LevelsEnabled = true;
        mixer.Tick();

        mixer.State.Should().Be(MixerConnectionState.Connected);
        mixer.Layout.Should().BeSameAs(layout);
        connectedLayouts.Should().Equal(layout);
        mixer.Snapshot.Layout.Should().BeSameAs(layout);
        mixer.Snapshot.Strips.Should().HaveCount(inputOffsets.Length);
        mixer.Snapshot.Buses.Should().HaveCount(outputOffsets.Length);
        mixer.Snapshot.Strips.Should().OnlyContain(s => s.Routes.Count == outputOffsets.Length && s.Layout == layout);
        mixer.Snapshot.Strips.Select(s => s.IsVirtual).Should().Equal(
            Enumerable.Range(0, inputOffsets.Length).Select(i => i >= layout.PhysicalStripCount));
        mixer.Snapshot.Buses.Select(b => b.Name).Should().Equal(layout.BusNames);
        mixer.Levels.Layout.Should().BeSameAs(layout);
        mixer.Levels.Strips.Should().HaveCount(inputOffsets.Length);
        mixer.Levels.Buses.Should().HaveCount(outputOffsets.Length);
        api.Calls.Where(c => c.Method == "GetLevel").Select(c => (c.Type, c.Channel)).Should().Equal(
            inputOffsets.SelectMany(i => new (int?, int?)[] { (1, i), (1, i + 1) })
                .Concat(outputOffsets.SelectMany(i => new (int?, int?)[] { (3, i), (3, i + 1) })));
        mixer.Levels.Strips.Concat(mixer.Levels.Buses).Should().OnlyContain(l => l.Left > 0 && l.Right > 0);
        if (type == 1)
        {
            api.Calls.Should().NotContain(c => c.Method == "GetParameterFloat" && c.Parameter!.EndsWith(".EQ.on", StringComparison.Ordinal));
            mixer.Snapshot.Buses.Should().OnlyContain(b => !b.EqOn);
            api.GetParameterFloat(VmParam.BusEqOn(0), out _).Should().Be(-1);
        }

        api.GetLevel(1, layout.InputLevelChannels - 1, out _).Should().Be(0);
        api.GetLevel(1, layout.InputLevelChannels, out _).Should().Be(-1);
        api.GetLevel(3, layout.OutputLevelChannels - 1, out _).Should().Be(0);
        api.GetLevel(3, layout.OutputLevelChannels, out _).Should().Be(-1);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 1)]
    [InlineData(2, 1)]
    public async Task ReconnectingToAnotherEditionReplacesSnapshotAndSilentFrame(int initialType, int nextType)
    {
        var api = new SimulatedVoicemeeterApi(42, MixerLayout.ForType(initialType));
        var time = new FakeTimeProvider();
        await using var mixer = new VoicemeeterMixer(api, NullLogger<VoicemeeterMixer>.Instance, time);
        mixer.Tick();
        var previous = mixer.Snapshot;
        var snapshots = new List<MixerSnapshot>();
        mixer.SnapshotChanged += (_, snapshot) => snapshots.Add(snapshot);
        api.SimulateEngineStopped();
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        mixer.State.Should().Be(MixerConnectionState.VoicemeeterNotRunning);
        api.MixerType = nextType;
        api.SimulateEngineStarted();
        time.Advance(TimeSpan.FromSeconds(2)); mixer.Tick();

        var layout = MixerLayout.ForType(nextType);
        mixer.Layout.Should().BeSameAs(layout);
        mixer.Snapshot.Revision.Should().Be(previous.Revision + 1);
        snapshots.Should().ContainSingle().Which.Layout.Should().BeSameAs(layout);
        mixer.Snapshot.Strips.Should().HaveCount(layout.StripCount);
        mixer.Snapshot.Buses.Should().HaveCount(layout.BusCount);
        mixer.Levels.Layout.Should().BeSameAs(layout);
        mixer.Levels.Strips.Should().HaveCount(layout.StripCount).And.OnlyContain(l => l.Max == 0);
        mixer.Levels.Buses.Should().HaveCount(layout.BusCount).And.OnlyContain(l => l.Max == 0);

        var lastStrip = layout.StripCount - 1;
        var lastBus = layout.BusCount - 1;
        mixer.SetFloat(VmParam.StripRoute(lastStrip, lastBus, layout), 0);
        mixer.SetFloat(VmParam.StripGain(lastStrip), -18);
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        mixer.Snapshot.Strips[lastStrip].Routes[lastBus].Should().BeFalse();
        mixer.Snapshot.Strips[lastStrip].Gain.Should().Be(-18);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task SimulatedFactoryAcceptsLayout(int type)
    {
        await using var mixer = VoicemeeterMixer.CreateSimulated(NullLoggerFactory.Instance, MixerLayout.ForType(type));
        mixer.Tick();
        mixer.Layout.VoicemeeterType.Should().Be(type);
        mixer.State.Should().Be(MixerConnectionState.Connected);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task LaunchUsesTheLastConnectedEdition(int type)
    {
        var api = new SimulatedVoicemeeterApi(42, MixerLayout.ForType(type)) { Run64Result = -1 };
        var time = new FakeTimeProvider();
        await using var mixer = new VoicemeeterMixer(api, NullLogger<VoicemeeterMixer>.Instance, time);
        mixer.Tick();
        api.SimulateEngineStopped();
        time.Advance(TimeSpan.FromMilliseconds(33)); mixer.Tick();
        var launch = mixer.LaunchVoicemeeterAsync();
        mixer.Tick();
        (await launch).Should().BeTrue();
        api.Calls.Where(c => c.Method == "RunVoicemeeter").Select(c => c.Type).Should().Equal(type + 3, type);
    }
}
