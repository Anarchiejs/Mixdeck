using System.Runtime.InteropServices;
using Mixdeck.Platform.Taskbar;
using static Mixdeck.Platform.Taskbar.TrayNotifyInterop;

namespace Mixdeck.Platform.Tests;

public sealed class TrayNotifyTests
{
    private const string Executable = @"C:\Apps\Mixdeck.exe";

    [Fact]
    public void X64NotifyItemLayoutMatchesTheNativeAbi()
    {
        Marshal.SizeOf<NotifyItem>().Should().Be(56);
        Marshal.OffsetOf<NotifyItem>(nameof(NotifyItem.Preference)).ToInt32().Should().Be(32);
        Marshal.OffsetOf<NotifyItem>(nameof(NotifyItem.Guid)).ToInt32().Should().Be(40);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mixdeck.exe")]
    [InlineData("C:\\Apps\\bad\0.exe")]
    public void RejectsAmbiguousPathsBeforeCallingCom(string path)
        => TrayNotifyInterop.TryPromoteNow(path, true).Should().BeFalse();

    [Fact]
    public void MissingIconNeverWrites()
    {
        using var tray = new FakeTray([]);
        Change(tray, Executable, true, null).Should().BeFalse();
        tray.Writes.Should().BeEmpty();
    }

    [Fact]
    public void UnknownPreferenceNeverWrites()
    {
        using var tray = new FakeTray([Item(1, 99)]);
        Change(tray, Executable, true, null).Should().BeFalse();
        tray.Writes.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, 2u)]
    [InlineData(false, 1u)]
    public void AppliesAndReadsBackEveryMatchingIcon(bool visible, uint expected)
    {
        using var tray = new FakeTray([Item(1, 0), Item(2, 1)]);
        Change(tray, Executable, visible, null).Should().BeTrue();
        tray.Items.Should().OnlyContain(item => item.Preference == expected);
        tray.ReadCount.Should().Be(2);
    }

    [Fact]
    public void AlreadyPromotedDoesNotWrite()
    {
        using var tray = new FakeTray([Item(1, 2)]);
        Change(tray, Executable, true, null).Should().BeTrue();
        tray.Writes.Should().BeEmpty();
    }

    [Fact]
    public void PartialFailureRestoresEachExactPreference()
    {
        using var tray = new FakeTray([Item(1, 0), Item(2, 1)]) { FailWrite = 2 };
        Change(tray, Executable, true, null).Should().BeFalse();
        tray.Items.Select(item => item.Preference).Should().Equal(0u, 1u);
        tray.Writes.Should().HaveCount(4);
    }

    [Fact]
    public void MissingPersistenceIsNotReportedAsSuccess()
    {
        using var tray = new FakeTray([Item(1, 0)]) { IgnoreWrites = true };
        Change(tray, Executable, true, null).Should().BeFalse();
        tray.Writes.Select(item => item.Preference).Should().Equal(2u, 0u);
    }

    [Fact]
    public void AReadbackForAnotherIconCannotConfirmTheChange()
    {
        var icon = Item(1, 0);
        var other = icon;
        other.Id = 2;
        SameIcon(icon, other).Should().BeFalse();
        other = icon;
        other.Window = 13;
        SameIcon(icon, other).Should().BeFalse();
        other = icon;
        other.Guid = Guid.NewGuid();
        SameIcon(icon, other).Should().BeFalse();
        other = icon;
        other.ExecutablePath = @"C:\Other\Mixdeck.exe";
        SameIcon(icon, other).Should().BeFalse();
    }

    private static NotifyItem Item(uint id, uint preference) => new()
    {
        ExecutablePath = Executable, Window = 12, Id = id, Preference = preference,
    };

    private sealed class FakeTray(List<NotifyItem> items) : ITraySession
    {
        internal List<NotifyItem> Items { get; } = items;
        internal List<NotifyItem> Writes { get; } = [];
        internal int ReadCount;
        internal int FailWrite;
        internal bool IgnoreWrites;

        public IReadOnlyList<NotifyItem> Read(string path)
        {
            ReadCount++;
            return Items.ToArray();
        }

        public void Set(NotifyItem item)
        {
            Writes.Add(item);
            if (Writes.Count == FailWrite) throw new COMException("Refus", unchecked((int)0x80070005));
            if (!IgnoreWrites) Items[Items.FindIndex(previous => SameIcon(previous, item))] = item;
        }

        public void Dispose() { }
    }
}
