using System.Globalization;
using System.Text;
using System.Text.Json;
using Mixdeck.Core.Model;
using Mixdeck.Core.Serialization;
namespace Mixdeck.Core.Tests;
public sealed class ProfileTests
{
    [Fact]
    public async Task CaptureDuplicateExportImportDeleteAndReload()
    {
        using var rig = new TestRig();
        rig.Mixer.SetFloat("Strip[0].Gain", -6.5f);
        var captured = await rig.Profiles.CaptureCurrentAsync("Musique");
        Assert.Equal(5, captured.Strips.Count);
        Assert.Equal(5, captured.Buses.Count);
        Assert.Equal(-6.5f, captured.Strips[0].Gain);
        var copy = await rig.Profiles.DuplicateAsync(captured.Id, "Copie");
        Assert.NotEqual(captured.Id, copy.Id);
        copy.Strips.Should().BeEquivalentTo(captured.Strips);
        using var stream = new MemoryStream();
        await rig.Profiles.ExportAsync(captured.Id, stream);
        stream.Position = 0;
        var imported = await rig.Profiles.ImportAsync(stream);
        Assert.NotEqual(captured.Id, imported.Id);
        await rig.Profiles.DeleteAsync(copy.Id);
        Assert.False(File.Exists(Path.Combine(rig.Paths.ProfilesDirectory, copy.Id + ".json")));
        await File.WriteAllTextAsync(Path.Combine(rig.Paths.ProfilesDirectory, "bad.json"), "{");
        await rig.Profiles.LoadAsync();
        Assert.Equal(2, rig.Profiles.Profiles.Count);
        Assert.Equal(rig.Profiles.Profiles.OrderBy(p => p.CreatedAt).ThenBy(p => p.Name, StringComparer.Ordinal), rig.Profiles.Profiles);
    }
    [Fact]
    public async Task ImportClampsAndIgnoresUnknownIndices()
    {
        using var rig = new TestRig();
        const string json = """
        {"id":"import", "name":"Import", "unknown":true,
         "strips":[{"index":0,"gain":100,"mute":true,"routes":[true]},{"index":9,"gain":0,"routes":[]}],
         "buses":[{"index":0,"gain":-100},{"index":-1,"gain":0}]}
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var profile = await rig.Profiles.ImportAsync(stream);
        Assert.Equal(12, Assert.Single(profile.Strips).Gain);
        Assert.Equal(5, profile.Strips[0].Routes.Count);
        Assert.Equal(-60, Assert.Single(profile.Buses).Gain);
    }
    [Fact]
    public async Task FadeHasExactTargetTimingMuteOrderAndInvariantCulture()
    {
        using var rig = new TestRig();
        var oldCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var p = new Profile { Id = "fade", Name = "Fondu", Strips = [new(0, -6.5f, true, [true, false, true, false, false]), new(1, -3, false, [])], Buses = [new(0, -9, true)] };
            await rig.Profiles.SaveAsync(p);
            var activeEvents = 0;
            rig.Profiles.ActiveChanged += (_, _) => activeEvents++;
            var task = rig.Profiles.ApplyAsync(p.Id, TimeSpan.FromMilliseconds(48));
            Assert.Equal(p.Id, rig.Profiles.Active?.Id);
            Assert.Equal(1, activeEvents);
            var initial = Assert.Single(rig.Mixer.Scripts);
            Assert.Contains("Strip[1].Mute=0;", initial);
            Assert.Contains("Strip[0].A3=1;", initial);
            Assert.DoesNotContain("Strip[0].Mute=1;", initial);
            for (var i = 1; i <= 3; i++)
            {
                rig.Time.Advance(TimeSpan.FromMilliseconds(16));
                var count = i + 1;
                await TestRig.UntilAsync(() => rig.Mixer.Scripts.Count == count);
            }
            await task;
            var scripts = rig.Mixer.Scripts.ToArray();
            Assert.Equal(4, scripts.Length);
            Assert.All(scripts, s => Assert.DoesNotContain(",", s));
            Assert.Contains("Strip[0].Gain=-6.5;", scripts[^1]);
            Assert.Contains("Bus[0].Gain=-9;", scripts[^1]);
            Assert.EndsWith("Bus[0].Mute=1;", scripts[^1]);
            Assert.All(scripts[..^1], s => Assert.DoesNotContain("Bus[0].Mute=1;", s));
            Assert.Equal(-6.5f, rig.Mixer.Snapshot.Strips[0].Gain);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }
    [Fact]
    public async Task NewFadeCancelsOldAndStartsFromReachedState()
    {
        using var rig = new TestRig();
        await rig.Profiles.SaveAsync(new() { Id = "a", Name = "A", Buses = [new(0, -12, false)] });
        await rig.Profiles.SaveAsync(new() { Id = "b", Name = "B", Buses = [new(0, 6, false)] });
        var first = rig.Profiles.ApplyAsync("a", TimeSpan.FromMilliseconds(48));
        rig.Time.Advance(TimeSpan.FromMilliseconds(16));
        await TestRig.UntilAsync(() => rig.Mixer.Scripts.Count == 2);
        Assert.Equal(-4, rig.Mixer.Snapshot.Buses[0].Gain);
        var second = rig.Profiles.ApplyAsync("b", TimeSpan.FromMilliseconds(16));
        Assert.Contains("Bus[0].Gain=-4;", rig.Mixer.Scripts.Last());
        rig.Time.Advance(TimeSpan.FromMilliseconds(16));
        await Task.WhenAll(first, second);
        Assert.Equal(6, rig.Mixer.Snapshot.Buses[0].Gain);
        var count = rig.Mixer.Scripts.Count;
        rig.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(count, rig.Mixer.Scripts.Count);
        await rig.Profiles.ApplyAsync("a", TimeSpan.Zero);
        Assert.Equal(count + 1, rig.Mixer.Scripts.Count);
    }
    [Fact]
    public async Task ExternalCancellationAndNextPreviousWrapping()
    {
        using var rig = new TestRig();
        await rig.Profiles.SaveAsync(new() { Id = "a", Name = "A" });
        await rig.Profiles.SaveAsync(new() { Id = "b", Name = "B" });
        await rig.Profiles.ApplyNextAsync(-1);
        Assert.Equal("a", rig.Profiles.Active?.Id);
        await rig.Profiles.ApplyNextAsync(-1);
        Assert.Equal("b", rig.Profiles.Active?.Id);
        await rig.Profiles.ApplyNextAsync(1);
        Assert.Equal("a", rig.Profiles.Active?.Id);
        using var ct = new CancellationTokenSource();
        var task = rig.Profiles.ApplyAsync("b", TimeSpan.FromSeconds(1), ct.Token);
        ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}
