using Mixdeck.Core.Model;
using Mixdeck.Voicemeeter.Native;

namespace Mixdeck.Voicemeeter;

internal sealed class LevelSampler(IVoicemeeterApi api, int inputType)
{
    internal LevelFrame Sample(long timestamp)
    {
        var strips = Enumerable.Range(0, BananaLayout.StripCount)
            .Select(i => Read(inputType, BananaLayout.InputChannelOffset(i))).ToArray();
        var buses = Enumerable.Range(0, BananaLayout.BusCount)
            .Select(i => Read(3, BananaLayout.OutputChannelOffset(i))).ToArray();
        return new(Array.AsReadOnly(strips), Array.AsReadOnly(buses), timestamp);
    }

    private StereoLevel Read(int type, int offset)
    {
        var leftResult = api.GetLevel(type, offset, out var left);
        var rightResult = api.GetLevel(type, offset + 1, out var right);
        return new(leftResult == 0 && float.IsFinite(left) ? Math.Max(0, left) : 0,
            rightResult == 0 && float.IsFinite(right) ? Math.Max(0, right) : 0);
    }
}
