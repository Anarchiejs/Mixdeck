using System.Runtime.InteropServices;
using Mixdeck.FlStudio;

namespace Mixdeck.FlStudio.Tests;

public sealed class SysexSendLeaseTests
{
    [Fact]
    public void Completion_releases_the_request_once_even_if_abort_races()
    {
        var lease = SysexSendLease.Allocate(7, [0xF0, 0x00, 0xF7], IntPtr.Zero);
        lease.RequestAbort();
        Marshal.ReadByte(lease.Request, 20).Should().Be(1);

        lease.CompleteFromCallback();
        lease.ReleaseCount.Should().Be(1);
        lease.Wait(TimeSpan.Zero).Should().BeTrue();

        lease.RequestAbort();
        lease.CompleteFromCallback();
        lease.WaiterFinished();
        lease.WaiterFinished();
        lease.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public void A_send_that_never_reaches_coremidi_is_released_immediately()
    {
        var lease = SysexSendLease.Allocate(1, [0xF0, 0xF7], IntPtr.Zero);
        lease.ReleaseBecauseNotSent();
        lease.ReleaseBecauseNotSent();
        lease.ReleaseCount.Should().Be(1);
    }

    [Fact]
    public void Late_completion_still_releases_after_the_waiter_has_given_up()
    {
        var lease = SysexSendLease.Allocate(3, [0xF0, 0xF7], IntPtr.Zero);
        lease.Wait(TimeSpan.Zero).Should().BeFalse();
        lease.RequestAbort();
        lease.WaiterFinished();
        lease.ReleaseCount.Should().Be(0);

        SysexSendLease.OnComplete(lease.Request);
        lease.ReleaseCount.Should().Be(1);
        lease.CompleteFromCallback();
        lease.ReleaseCount.Should().Be(1);
    }
}
