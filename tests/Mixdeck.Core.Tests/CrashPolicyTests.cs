using Mixdeck.Core.Diagnostics;

namespace Mixdeck.Core.Tests;

public sealed class CrashPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RestartsAfterFirstCrash() => CrashPolicy.ShouldRestart(Now, null).Should().BeTrue();

    [Fact]
    public void DoesNotRestartWhenCrashesAreClose() =>
        CrashPolicy.ShouldRestart(Now, Now - TimeSpan.FromMinutes(4)).Should().BeFalse();

    [Fact]
    public void RestartsAgainAfterCooldown() =>
        CrashPolicy.ShouldRestart(Now, Now - CrashPolicy.RestartCooldown).Should().BeTrue();

    [Fact]
    public void IgnoresMarkerFromTheFuture() =>
        CrashPolicy.ShouldRestart(Now, Now + TimeSpan.FromHours(1)).Should().BeTrue();

    [Fact]
    public void KeepsOnlyTenMostRecentReports()
    {
        var reports = Enumerable.Range(0, 13)
            .Select(i => Path.Combine("logs", CrashPolicy.ReportFileName(Now + TimeSpan.FromMinutes(i))))
            .Append(Path.Combine("logs", "mixdeck-20261027.log"))
            .Reverse()
            .ToList();

        var deleted = CrashPolicy.ReportsToDelete(reports);

        deleted.Should().BeEquivalentTo(Enumerable.Range(0, 3)
            .Select(i => Path.Combine("logs", CrashPolicy.ReportFileName(Now + TimeSpan.FromMinutes(i)))));
    }
}
