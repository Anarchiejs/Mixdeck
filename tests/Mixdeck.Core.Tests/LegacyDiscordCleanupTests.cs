using System.Text.Json;
using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Tests;

public sealed class LegacyDiscordCleanupTests
{
    [Fact]
    public void Startup_wipes_discord_credentials_and_settings_once()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-discord-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var credential = Path.Combine(root, "discord.bin");
            var secret = new byte[100_000];
            Random.Shared.NextBytes(secret);
            File.WriteAllBytes(credential, secret);
            File.SetAttributes(credential, File.GetAttributes(credential) | FileAttributes.ReadOnly);
            var temporary = Path.Combine(root, "discord.bin." + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllBytes(temporary, secret);
            var kept = Path.Combine(root, "profiles-note.txt");
            File.WriteAllText(kept, "keep discord mentioned here");
            File.WriteAllText(Path.Combine(root, "settings.json"), """
                {
                  "schemaVersion": 1,
                  "theme": "dark",
                  "discord": { "enabled": true, "clientId": "1234567890" }
                }
                """);
            File.WriteAllText(Path.Combine(root, "settings.json.bak"), """
                { "schemaVersion": 1, "Discord": { "enabled": false, "clientId": "42" }, "language": "en-US" }
                """);

            using (var held = new FileStream(credential, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                LegacyDiscordCleanup.Remove(root);
                held.Position = 0;
                var wiped = new byte[secret.Length];
                var read = held.Read(wiped, 0, wiped.Length);
                read.Should().Be(secret.Length);
                wiped.Should().OnlyContain(value => value == 0);
            }

            File.Exists(credential).Should().BeFalse();
            File.Exists(temporary).Should().BeFalse();
            File.ReadAllText(kept).Should().Be("keep discord mentioned here");

            using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "settings.json")));
            settings.RootElement.TryGetProperty("discord", out _).Should().BeFalse();
            settings.RootElement.TryGetProperty("Discord", out _).Should().BeFalse();
            settings.RootElement.GetProperty("theme").GetString().Should().Be("dark");
            settings.RootElement.GetProperty("schemaVersion").GetInt32().Should().Be(1);

            using var backup = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "settings.json.bak")));
            backup.RootElement.TryGetProperty("Discord", out _).Should().BeFalse();
            backup.RootElement.GetProperty("language").GetString().Should().Be("en-US");

            var rewritten = File.ReadAllText(Path.Combine(root, "settings.json"));
            LegacyDiscordCleanup.Remove(root);
            File.ReadAllText(Path.Combine(root, "settings.json")).Should().Be(rewritten);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Loading_settings_removes_a_leftover_discord_section()
    {
        var root = Path.Combine(Path.GetTempPath(), "mixdeck-discord-load-" + Guid.NewGuid().ToString("N"));
        var paths = new MixdeckPaths(root);
        paths.EnsureCreated();
        try
        {
            await File.WriteAllTextAsync(paths.SettingsFile, """
                { "schemaVersion": 1, "language": "fr-FR", "discord": { "enabled": true, "clientId": "99" } }
                """);
            var settings = await new SettingsStore(paths, Microsoft.Extensions.Logging.Abstractions.NullLogger<SettingsStore>.Instance).LoadAsync();
            settings.Language.Should().Be("fr-FR");
            var text = await File.ReadAllTextAsync(paths.SettingsFile);
            text.IndexOf("discord", StringComparison.OrdinalIgnoreCase).Should().BeLessThan(0);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }
}
