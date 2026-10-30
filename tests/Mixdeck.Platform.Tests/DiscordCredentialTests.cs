using System.Text;
using System.Security.Cryptography;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Discord;

namespace Mixdeck.Platform.Tests;

public sealed class DiscordCredentialTests
{
    [Fact]
    public void DpapiRoundTripDoesNotPersistPlaintextAndForgetDeletesFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mixdeck-discord-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DiscordCredentialStore(new MixdeckPaths(directory));
            store.Load().Should().BeNull();
            store.Save(new DiscordCredentials { ClientId = "fake-id", ClientSecret = "fake-app-secret-123",
                AccessToken = "fake-access-token", RefreshToken = "fake-refresh-token", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            var file = Path.Combine(directory, "discord.bin");
            Encoding.UTF8.GetString(File.ReadAllBytes(file)).Should().NotContain("fake-");
            var loaded = store.Load()!;
            loaded.ClientId.Should().Be("fake-id");
            loaded.ClientSecret.Should().Be("fake-app-secret-123");
            loaded.AccessToken.Should().Be("fake-access-token");
            loaded.RefreshToken.Should().Be("fake-refresh-token");
            store.Forget();
            File.Exists(file).Should().BeFalse();
            store.Load().Should().BeNull();
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void CorruptCredentialsAreRejectedWithoutTheirContentsInException()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mixdeck-discord-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "discord.bin"), "fake-secret");
            var store = new DiscordCredentialStore(new MixdeckPaths(directory));
            var exception = Assert.Throws<CryptographicException>(() => store.Load());
            exception.ToString().Should().NotContain("fake-secret");
        }
        finally { Directory.Delete(directory, true); }
    }
}
