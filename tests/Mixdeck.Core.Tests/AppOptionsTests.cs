using Mixdeck.App;
using Mixdeck.Core.Model;

namespace Mixdeck.Core.Tests;

public sealed class AppOptionsTests
{
    [Theory]
    [InlineData("--demo", 2)]
    [InlineData("--demo=banana", 2)]
    [InlineData("--demo=standard", 1)]
    [InlineData("--demo=potato", 3)]
    [InlineData(" --DEMO=POTATO ", 3)]
    public void DemoVariantAndDevicesPageAreParsed(string arg, int type)
    {
        var options = AppOptions.Parse([arg, "--page=Devices", "--flyout"]);
        Assert.True(options.Demo);
        Assert.Same(MixerLayout.ForType(type), options.DemoLayout);
        Assert.Equal("devices", options.Page);
        Assert.True(options.Flyout);
    }

    [Fact]
    public void NormalLaunchKeepsBananaAsDemoDefault()
    {
        var options = AppOptions.Parse([]);
        Assert.False(options.Demo);
        Assert.Same(MixerLayout.Banana, options.DemoLayout);
    }
}
