using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Tests;
public sealed class SettingsRobustnessTests
{
    // Fichier écrit à la main, par une ancienne version ou tronqué : les champs absents ou nuls prennent leur valeur par défaut.
    [Theory]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"strips\":null,\"buses\":null,\"hotkeys\":null,\"rules\":null,\"ducking\":null}")]
    public async Task MissingOrNullCollectionsFallBackToDefaults(string json)
    {
        using var rig = new TestRig();
        rig.Paths.EnsureCreated();
        await File.WriteAllTextAsync(rig.Paths.SettingsFile, json);
        var settings = await new SettingsStore(rig.Paths, NullLogger<SettingsStore>.Instance).LoadAsync();
        Assert.NotNull(settings.Strips);
        Assert.NotNull(settings.Buses);
        Assert.NotNull(settings.Rules);
        Assert.NotNull(settings.Ducking);
        Assert.Equal(AppSettings.DefaultHotkeys, settings.Hotkeys);
        Assert.Equal(new ChannelCustomization(0), settings.StripCustomization(0));
    }
}
