using Mixdeck.Core.Model;

namespace Mixdeck.Desktop;

public sealed record DesktopOptions(bool Demo, bool Minimized, bool SelfCheck, bool AfterCrash)
{
    public MixerLayout DemoLayout { get; init; } = MixerLayout.Banana;

    public static DesktopOptions Parse(IEnumerable<string> args)
    {
        var set = args.Select(argument => argument.Trim().ToLowerInvariant()).ToHashSet();
        var demoType = set.Contains("--demo=potato") ? 3 : set.Contains("--demo=standard") ? 1 : 2;
        var demo = set.Contains("--demo") || set.Contains("--demo=banana") || set.Contains("--demo=potato") || set.Contains("--demo=standard");
        return new DesktopOptions(demo, set.Contains("--minimized"), set.Contains("--self-check"), set.Contains("--after-crash"))
        {
            DemoLayout = MixerLayout.ForType(demoType),
        };
    }
}
