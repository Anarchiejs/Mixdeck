using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;
using Mixdeck.Platform.Taskbar;

namespace Mixdeck.Platform.Tests;

public sealed class TrayIconPromoterTests : IDisposable
{
    private readonly string _keyPath = @"Software\Mixdeck.Tests\" + Guid.NewGuid().ToString("N");
    private const string Executable = @"C:\Mixdeck Tests\current\Mixdeck.exe";

    private void AddEntry(string name, string path, int? promoted = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath + "\\" + name);
        key.SetValue("ExecutablePath", path);
        if (promoted is { } value) key.SetValue("IsPromoted", value, RegistryValueKind.DWord);
    }

    private object? Promoted(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(_keyPath + "\\" + name);
        return key!.GetValue("IsPromoted");
    }

    [Fact]
    public void PromotesOnlyOurEntriesAndLeavesOthersAlone()
    {
        AddEntry("1", @"C:\Other\App.exe");
        AddEntry("2", @"c:\mixdeck tests\CURRENT\mixdeck.exe");
        AddEntry("3", Executable, promoted: 0);
        var promoter = new TrayIconPromoter(_keyPath, Executable);

        promoter.SetVisible(true).Should().Be(TrayPromotionResult.Changed);

        Promoted("1").Should().BeNull();
        Promoted("2").Should().Be(1);
        Promoted("3").Should().Be(1);
        promoter.SetVisible(true).Should().Be(TrayPromotionResult.Unchanged);
    }

    [Fact]
    public void HidingWritesZero()
    {
        AddEntry("1", Executable, promoted: 1);
        new TrayIconPromoter(_keyPath, Executable).SetVisible(false).Should().Be(TrayPromotionResult.Changed);
        Promoted("1").Should().Be(0);
    }

    [Fact]
    public void ReportsMissingEntryOrKey()
    {
        new TrayIconPromoter(_keyPath, Executable).SetVisible(true).Should().Be(TrayPromotionResult.NotFound);
        AddEntry("1", @"C:\Other\App.exe");
        new TrayIconPromoter(_keyPath, Executable).SetVisible(true).Should().Be(TrayPromotionResult.NotFound);
    }

    [Fact]
    public async Task WaitsForWindowsToCreateTheEntry()
    {
        var time = new FakeTimeProvider();
        var promoter = new TrayIconPromoter(_keyPath, Executable);
        var pending = promoter.SetVisibleWhenReadyAsync(true, TimeSpan.FromSeconds(10), time);
        pending.IsCompleted.Should().BeFalse();

        AddEntry("1", Executable);
        time.Advance(TimeSpan.FromMilliseconds(500));

        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(TrayPromotionResult.Changed);
        Promoted("1").Should().Be(1);
    }

    [Fact]
    public async Task GivesUpAfterTimeout()
    {
        var time = new FakeTimeProvider();
        var pending = new TrayIconPromoter(_keyPath, Executable).SetVisibleWhenReadyAsync(true, TimeSpan.FromSeconds(1), time);
        for (var i = 0; i < 3; i++) time.Advance(TimeSpan.FromMilliseconds(500));
        (await pending.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(TrayPromotionResult.NotFound);
    }

    [Theory]
    [InlineData(@"{905e63b6-c1bf-494e-b29c-65b732d3d21a}\Mixdeck Tests\x.exe", false)]
    [InlineData(@"C:\Mixdeck Tests\current\..\current\Mixdeck.exe", true)]
    [InlineData(@"C:\Mixdeck Tests\current\Mixdeck.exe.old", false)]
    [InlineData("", false)]
    public void ComparesNormalizedPaths(string registered, bool same) =>
        TrayIconPromoter.IsSameExecutable(registered, Executable).Should().Be(same);

    [Fact]
    public void ResolvesKnownFolderPrefix()
    {
        // {905E63B6-…} = Program Files : l'Explorateur écrit ainsi les applis qui y sont installées.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        TrayIconPromoter.IsSameExecutable(@"{905E63B6-C1BF-494E-B29C-65B732D3D21A}\VB\app.exe", Path.Combine(programFiles, "VB", "app.exe"))
            .Should().BeTrue();
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\Mixdeck.Tests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            parent.Close();
            Registry.CurrentUser.DeleteSubKey(@"Software\Mixdeck.Tests", throwOnMissingSubKey: false);
        }
    }
}
