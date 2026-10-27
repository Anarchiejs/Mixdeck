using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Serialization;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Tests;
public sealed class SettingsTests
{
    [Fact]
    public async Task AtomicSaveBackupRecoveryAndChanged()
    {
        using var rig = new TestRig();
        var store = new SettingsStore(rig.Paths, NullLogger<SettingsStore>.Instance);
        var changes = 0;
        store.Changed += (_, s) => { Assert.Same(s, store.Current); changes++; };
        Assert.Equal(new AppSettings(), await store.LoadAsync());
        await store.SaveAsync(new() { LastProfileId = "first" });
        await store.UpdateAsync(s => s with { LastProfileId = "second" });
        Assert.Equal(2, changes);
        Assert.False(File.Exists(rig.Paths.SettingsFile + ".tmp"));
        Assert.Contains("first", await File.ReadAllTextAsync(rig.Paths.SettingsBackupFile));
        await File.WriteAllTextAsync(rig.Paths.SettingsFile, "invalid");
        Assert.Equal("first", (await store.LoadAsync()).LastProfileId);
        Assert.Single(Directory.GetFiles(rig.Paths.Root, "settings.json.corrupt-*"));
        Assert.Contains("first", await File.ReadAllTextAsync(rig.Paths.SettingsFile));
        await store.SaveAsync(new() { LastProfileId = "third" });
        await File.WriteAllTextAsync(rig.Paths.SettingsFile, "null");
        await File.WriteAllTextAsync(rig.Paths.SettingsBackupFile, "{");
        Assert.Null((await store.LoadAsync()).LastProfileId);
    }
    [Fact]
    public async Task ConcurrentUpdatesAreSerialized()
    {
        using var rig = new TestRig();
        var store = new SettingsStore(rig.Paths, NullLogger<SettingsStore>.Instance);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => store.UpdateAsync(s => s with { FadeMilliseconds = s.FadeMilliseconds + 1 })));
        Assert.Equal(320, store.Current.FadeMilliseconds);
        Assert.Equal(320, (await store.LoadAsync()).FadeMilliseconds);
    }
    [Fact]
    public void GeneratedJsonUsesCamelCaseStringEnumsAndIgnoresUnknownProperties()
    {
        var value = new AppSettings { Theme = ThemeMode.Dark, Rules = [new() { Id = "r", Name = "Règle", ProcessName = "app", Trigger = RuleTrigger.ProcessForeground }] };
        var json = JsonSerializer.Serialize(value, MixdeckJsonContext.Default.AppSettings);
        Assert.Contains("\"theme\": \"Dark\"", json);
        Assert.Contains("\"ProcessForeground\"", json);
        var read = JsonSerializer.Deserialize(json[..^1] + ",\"futureProperty\":42}", MixdeckJsonContext.Default.AppSettings);
        Assert.Equal(ThemeMode.Dark, read!.Theme);
        Assert.Equal(RuleTrigger.ProcessForeground, Assert.Single(read.Rules).Trigger);
    }
    [Fact]
    public async Task InaccessibleDestinationDoesNotEscapeOrChangeCurrent()
    {
        using var rig = new TestRig();
        Directory.CreateDirectory(rig.Paths.SettingsFile);
        var store = new SettingsStore(rig.Paths, NullLogger<SettingsStore>.Instance);
        await store.SaveAsync(new() { ShowOsd = false });
        Assert.True(store.Current.ShowOsd);
    }
}
