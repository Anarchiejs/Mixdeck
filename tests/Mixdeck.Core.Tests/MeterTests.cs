using Mixdeck.Core.Audio;
namespace Mixdeck.Core.Tests;
public sealed class MeterTests
{
    [Fact]
    public void AttackReleasePeakAndClipHolds()
    {
        var meter = new MeterBallistics();
        meter.Update(1, TimeSpan.FromMilliseconds(100));
        Assert.InRange(meter.LevelDb, -0.001f, 0);
        Assert.Equal(0, meter.PeakHoldDb);
        Assert.True(meter.Clipped);
        meter.Update(0, TimeSpan.FromMilliseconds(500));
        Assert.InRange(meter.LevelDb, -13.001f, -13);
        Assert.Equal(0, meter.PeakHoldDb);
        Assert.True(meter.Clipped);
        meter.Update(0, TimeSpan.FromMilliseconds(500));
        Assert.False(meter.Clipped);
        meter.Update(0, TimeSpan.FromMilliseconds(1000));
        Assert.Equal(-13, meter.PeakHoldDb);
        meter.Update(0, TimeSpan.FromSeconds(10));
        Assert.Equal(-60, meter.LevelDb);
        meter.Reset();
        Assert.Equal(-60, meter.PeakHoldDb);
        Assert.False(meter.Clipped);
    }
    [Fact]
    public void ConversionFloorStereoAndAllocationFreeUpdates()
    {
        Assert.Equal(-60, MeterBallistics.ToDb(0));
        Assert.Equal(-60, MeterBallistics.ToDb(float.NaN));
        Assert.Equal(-60, MeterBallistics.ToDb(-1));
        Assert.Equal(0, MeterBallistics.ToDb(1));
        Assert.InRange(MeterBallistics.ToDb(0.5f), -6.021f, -6.020f);
        var stereo = new StereoMeterBallistics(attackMs: 0);
        stereo.Update(1, 0.5f, TimeSpan.FromMilliseconds(16));
        Assert.Equal(0, stereo.Left.LevelDb);
        Assert.False(stereo.Right.Clipped);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++) stereo.Update(0, 0, TimeSpan.FromMilliseconds(16));
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        stereo.Reset();
        Assert.Equal(-60, stereo.Left.LevelDb);
    }
}
