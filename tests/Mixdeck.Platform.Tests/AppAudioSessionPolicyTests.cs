using Microsoft.Extensions.Time.Testing;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Audio;

namespace Mixdeck.Platform.Tests;

public sealed class AppAudioSessionPolicyTests
{
    [Theory]
    [InlineData("Session", "Description", "app.exe", false, "Session")]
    [InlineData("  Session  ", "Description", "app.exe", false, "Session")]
    [InlineData(" ", "Description", "app.exe", false, "Description")]
    [InlineData(null, null, "app.exe", false, "app.exe")]
    [InlineData(null, null, "", false, "Application")]
    [InlineData(null, "Description", "", true, "System sounds")]
    [InlineData("Notification", null, "", true, "Notification")]
    public void ChoosesNameInPriorityOrder(string? name, string? description, string process, bool system, string expected) =>
        AppAudioSessionPolicy.DisplayName(name, description, process, system).Should().Be(expected);

    [Fact]
    public void UngroupedSessionsOfSameProcessKeepSeparateControls()
    {
        var sessions = AppAudioSessionPolicy.Group([Entry("a", 42), Entry("b", 42)]);
        sessions.Select(session => session.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void SharedNonEmptyGroupingCombinesEvenDifferentProcesses()
    {
        var group = Guid.NewGuid();
        var a = Entry("a", 42, group);
        var b = Entry("b", 43, group);
        AppAudioSessionPolicy.Group([b, a]).Should().Equal(a.Session);
        AppAudioSessionPolicy.GroupKey(a).Should().Be(AppAudioSessionPolicy.GroupKey(b));
        AppAudioSessionPolicy.GroupKey(Entry("c", 42)).Should().NotBe(AppAudioSessionPolicy.GroupKey(a));
    }

    [Fact]
    public void DifferentGroupsDoNotMergeAndRemovalChangesRepresentative()
    {
        var group = Guid.NewGuid();
        var a = Entry("a", 42, group);
        var b = Entry("b", 42, group);
        var c = Entry("c", 42, Guid.NewGuid());
        AppAudioSessionPolicy.Group([a, b, c]).Select(session => session.Id).Should().Equal("a", "c");
        AppAudioSessionPolicy.Group([b, c]).Select(session => session.Id).Should().Equal("b", "c");
        AppAudioSessionPolicy.Group([]).Should().BeEmpty();
    }

    [Fact]
    public void LimiterPublishesImmediatelyThenCoalescesAndFlushesLastChange()
    {
        var clock = new FakeTimeProvider();
        var limiter = new AppAudioEventLimiter(clock);
        limiter.DelayMilliseconds.Should().Be(Timeout.Infinite);
        limiter.Take().Should().BeFalse();
        limiter.MarkChanged();
        limiter.Take().Should().BeTrue();
        for (var i = 0; i < 100; i++) limiter.MarkChanged();
        limiter.DelayMilliseconds.Should().Be(100);
        clock.Advance(TimeSpan.FromMilliseconds(99));
        limiter.Take().Should().BeFalse();
        limiter.IsPending.Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(1));
        limiter.Take().Should().BeTrue();
        limiter.Take().Should().BeFalse();
        limiter.DelayMilliseconds.Should().Be(Timeout.Infinite);
    }

    [Fact]
    public void ContinuousBurstStaysAtTenEventsPerSecondWithoutStarving()
    {
        var clock = new FakeTimeProvider();
        var limiter = new AppAudioEventLimiter(clock);
        var count = 0;
        for (var i = 0; i < 1000; i++)
        {
            limiter.MarkChanged();
            if (limiter.Take()) count++;
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        count.Should().Be(10);
        limiter.Take().Should().BeTrue(); // Dernière modification de la rafale, à 1 000 ms.
        limiter.DelayMilliseconds.Should().Be(Timeout.Infinite);
    }

    [Fact]
    public async Task UnstartedServiceConsumesNothingAndDisposalIsIdempotent()
    {
        var service = new AppAudioSessions();
        service.Sessions.Should().BeEmpty();
        service.SetMute("inconnue", true);
        service.SetVolume("inconnue", float.NaN);
        service.Dispose();
        service.Dispose();
        service.Start();
        await service.DisposeAsync();
        service.Sessions.Should().BeEmpty();
    }

    private static SessionEntry Entry(string id, int pid, Guid grouping = default) =>
        new(new AppAudioSession(id, pid, "app.exe", id, null, .5f, false, false), grouping);
}
