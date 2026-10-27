using Mixdeck.Core.Model;
namespace Mixdeck.Core.Audio;
public sealed class MeterBallistics(float attackMs = 5, float releaseDbPerSecond = 26, float peakHoldMs = 1500, float clipHoldMs = 1000)
{
    private double peakRemaining, clipRemaining;
    public float LevelDb { get; private set; } = -60;
    public float PeakHoldDb { get; private set; } = -60;
    public bool Clipped { get; private set; }
    public static float ToDb(float linear) => float.IsNaN(linear) || linear <= 0 ? -60 : MathF.Max(-60, 20 * MathF.Log10(linear));
    public void Update(float linearPeak, TimeSpan elapsed)
    {
        var ms = Math.Max(0, elapsed.TotalMilliseconds);
        var target = ToDb(linearPeak);
        LevelDb = target > LevelDb ? LevelDb + (target - LevelDb) * (attackMs <= 0 ? 1 : (float)(1 - Math.Exp(-ms / attackMs)))
            : MathF.Max(target, LevelDb - Math.Max(0, releaseDbPerSecond) * (float)(ms / 1000));
        if (target >= PeakHoldDb) { PeakHoldDb = target; peakRemaining = Math.Max(0, peakHoldMs); }
        else
        {
            var decayMs = Math.Max(0, ms - peakRemaining);
            peakRemaining = Math.Max(0, peakRemaining - ms);
            PeakHoldDb = MathF.Max(LevelDb, PeakHoldDb - Math.Max(0, releaseDbPerSecond) * (float)(decayMs / 1000));
        }
        if (linearPeak >= 1) { Clipped = true; clipRemaining = Math.Max(0, clipHoldMs); }
        else { clipRemaining = Math.Max(0, clipRemaining - ms); Clipped = clipRemaining > 0; }
    }
    public void Reset() { LevelDb = PeakHoldDb = -60; peakRemaining = clipRemaining = 0; Clipped = false; }
}
public sealed class StereoMeterBallistics(float attackMs = 5, float releaseDbPerSecond = 26, float peakHoldMs = 1500, float clipHoldMs = 1000)
{
    public MeterBallistics Left { get; } = new(attackMs, releaseDbPerSecond, peakHoldMs, clipHoldMs);
    public MeterBallistics Right { get; } = new(attackMs, releaseDbPerSecond, peakHoldMs, clipHoldMs);
    public void Update(StereoLevel peak, TimeSpan elapsed) { Left.Update(peak.Left, elapsed); Right.Update(peak.Right, elapsed); }
    public void Update(float left, float right, TimeSpan elapsed) => Update(new StereoLevel(left, right), elapsed);
    public void Reset() { Left.Reset(); Right.Reset(); }
}
