using Microsoft.Win32;
using Mixdeck.Platform.Startup;

namespace Mixdeck.Platform.Tests;

public sealed class AutoStartTests : IDisposable
{
    private readonly string _keyPath = @"Software\Mixdeck.Tests\" + Guid.NewGuid().ToString("N");
    private const string Executable = @"C:\Mixdeck Tests\Mixdeck.exe";

    [Fact]
    public void WritesQuotedCommandAndRemovesOnlyItsValue()
    {
        using (Registry.CurrentUser.CreateSubKey(_keyPath)) { }
        var service = new AutoStart(_keyPath, Executable);
        service.IsEnabled.Should().BeFalse();
        service.SetEnabled(true);
        using (var key = Registry.CurrentUser.OpenSubKey(_keyPath, writable: true))
        {
            key!.GetValue("Mixdeck").Should().Be("\"C:\\Mixdeck Tests\\Mixdeck.exe\" --minimized");
            key.SetValue("Other", "conservé");
        }
        service.IsEnabled.Should().BeTrue();
        service.SetEnabled(false);
        service.IsEnabled.Should().BeFalse();
        using var read = Registry.CurrentUser.OpenSubKey(_keyPath);
        read!.GetValue("Other").Should().Be("conservé");
    }

    [Theory]
    [InlineData("\"C:\\Other\\Mixdeck.exe\" --minimized", false)]
    [InlineData("\"c:\\mixdeck tests\\MIXDECK.EXE\" --minimized", true)]
    [InlineData("\"C:\\Mixdeck Tests\\Mixdeck.exe.old\"", false)]
    [InlineData("\"C:\\Mixdeck Tests\\Mixdeck.exe", false)]
    [InlineData("Mixdeck.exe", false)]
    public void RecognizesOnlyCurrentExecutable(string command, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath);
        key.SetValue("Mixdeck", command);
        new AutoStart(_keyPath, Executable).IsEnabled.Should().Be(enabled);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

        // Retire aussi la clé parente si plus aucun test ne l'utilise : rien ne doit rester dans le registre.
        using var parent = Registry.CurrentUser.OpenSubKey(@"Software\Mixdeck.Tests");
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
        {
            parent.Close();
            Registry.CurrentUser.DeleteSubKey(@"Software\Mixdeck.Tests", throwOnMissingSubKey: false);
        }
    }
}
