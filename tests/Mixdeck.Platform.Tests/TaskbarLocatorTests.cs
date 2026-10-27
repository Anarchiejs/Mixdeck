using Mixdeck.Platform.Taskbar;

namespace Mixdeck.Platform.Tests;

public sealed class TaskbarLocatorTests
{
    [Fact]
    public void PrimaryMonitorHasCoherentBoundsAndDpi()
    {
        var locator = new TaskbarLocator();
        var placement = locator.GetPrimary();
        placement.Dpi.Should().BeGreaterThanOrEqualTo(96);
        placement.MonitorBounds.Width.Should().BePositive();
        placement.MonitorBounds.Height.Should().BePositive();
        placement.WorkArea.Left.Should().BeGreaterThanOrEqualTo(placement.MonitorBounds.Left);
        placement.WorkArea.Top.Should().BeGreaterThanOrEqualTo(placement.MonitorBounds.Top);
        placement.WorkArea.Right.Should().BeLessThanOrEqualTo(placement.MonitorBounds.Right);
        placement.WorkArea.Bottom.Should().BeLessThanOrEqualTo(placement.MonitorBounds.Bottom);
        placement.TaskbarBounds.Width.Should().BeGreaterThanOrEqualTo(0);
        placement.TaskbarBounds.Height.Should().BeGreaterThanOrEqualTo(0);
        locator.GetForPoint(placement.MonitorBounds.Left + 1, placement.MonitorBounds.Top + 1).Should().Be(placement);
    }
}
