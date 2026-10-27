using Mixdeck.Core.Model;

namespace Mixdeck.Core.Tests;

public sealed class MixerLayoutTests
{
    [Theory]
    [InlineData(1, 2, 1, 3, 2, 12, 16, "A1,B1", "0,2,4")]
    [InlineData(2, 3, 3, 5, 5, 22, 40, "A1,A2,A3,B1,B2", "0,2,4,6,14")]
    [InlineData(3, 5, 5, 8, 8, 34, 64, "A1,A2,A3,A4,A5,B1,B2,B3", "0,2,4,6,8,10,18,26")]
    public void LayoutMatchesRemoteChannelAssignment(int type, int physicalStrips, int physicalBuses,
        int strips, int buses, int inputs, int outputs, string busNames, string offsets)
    {
        var layout = MixerLayout.ForType(type);
        Assert.Equal(physicalStrips, layout.PhysicalStripCount);
        Assert.Equal(physicalBuses, layout.PhysicalBusCount);
        Assert.Equal(strips, layout.StripCount);
        Assert.Equal(buses, layout.BusCount);
        Assert.Equal(inputs, layout.InputLevelChannels);
        Assert.Equal(outputs, layout.OutputLevelChannels);
        Assert.Equal(busNames, string.Join(',', layout.BusNames));
        Assert.Equal(offsets, string.Join(',', Enumerable.Range(0, strips).Select(layout.InputChannelOffset)));
        Assert.Equal(Enumerable.Range(0, buses).Select(i => i * 8), Enumerable.Range(0, buses).Select(layout.OutputChannelOffset));
        Assert.False(layout.IsVirtualStrip(physicalStrips - 1));
        Assert.True(layout.IsVirtualStrip(physicalStrips));
        Assert.False(layout.IsVirtualBus(physicalBuses - 1));
        Assert.True(layout.IsVirtualBus(physicalBuses));
        Assert.Equal(strips, layout.DefaultStripLabels.Count);
        Assert.Equal($"Strip[0].B1", VmParam.StripRoute(0, physicalBuses, layout));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EmptySnapshotAndLevelsCarryLayout(int type)
    {
        var layout = MixerLayout.ForType(type);
        var snapshot = MixerSnapshot.EmptyFor(layout);
        var levels = LevelFrame.SilentFor(layout);
        Assert.Same(layout, snapshot.Layout);
        Assert.Same(layout, levels.Layout);
        Assert.Equal(layout.StripCount, snapshot.Strips.Count);
        Assert.Equal(layout.BusCount, snapshot.Buses.Count);
        Assert.Equal(layout.StripCount, levels.Strips.Count);
        Assert.Equal(layout.BusCount, levels.Buses.Count);
        Assert.All(snapshot.Strips, s =>
        {
            Assert.Same(layout, s.Layout);
            Assert.Equal(layout.BusCount, s.Routes.Count);
            Assert.Equal(layout.IsVirtualStrip(s.Index), s.IsVirtual);
            Assert.Equal(s.IsVirtual ? 3 : 0, s.EqGains.Count);
        });
        Assert.All(snapshot.Buses, b =>
        {
            Assert.Same(layout, b.Layout);
            Assert.Equal(layout.BusNames[b.Index], b.Name);
            Assert.Equal(layout.IsVirtualBus(b.Index), b.IsVirtual);
        });
        Assert.All(levels.Strips.Concat(levels.Buses), level => Assert.Equal(default, level));
    }

    [Fact]
    public void InvalidTypeAndOffsetsAreRejectedAndBananaRemainsDefault()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MixerLayout.ForType(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => MixerLayout.Standard.InputChannelOffset(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => MixerLayout.Potato.OutputChannelOffset(-1));
        Assert.Same(MixerLayout.Banana, MixerSnapshot.Empty.Layout);
        Assert.Same(MixerLayout.Banana, LevelFrame.Silent.Layout);
        Assert.Equal(-60, MixerLayout.Standard.ClampGain(-100));
        Assert.Equal(12, MixerLayout.Potato.ClampGain(100));
    }
}
