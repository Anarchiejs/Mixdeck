using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;
using Mixdeck.Core.Profiles;

namespace Mixdeck.Core.Tests;

public sealed class DawProfileAndHotkeyTests
{
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Levels.Add(logLevel);
    }
    private static FakeDaw Daw() => new() { Tracks = new Dictionary<int, DawTrack>
    {
        [2] = new(2, "Piano", .8f, false, true, [new(0, true), new(3, false)]),
        [0] = new(0, "Master", .7f, true, false, [])
    } };
    private static ProfileService Service(TestRig rig, FakeDaw daw) =>
        new(rig.Mixer, rig.Settings, rig.Paths, NullLogger<ProfileService>.Instance, rig.Time, daw);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CaptureAndJsonRoundTrip(bool connected, bool capture)
    {
        using var rig = new TestRig();
        var daw = Daw();
        if (!connected) daw.State = DawConnectionState.WaitingForDaw;
        await rig.Settings.UpdateAsync(s => s with { FlStudio = s.FlStudio with { CaptureInProfiles = capture } });
        var service = Service(rig, daw);
        var profile = await service.CaptureCurrentAsync("FL");
        Assert.Equal(connected && capture ? 2 : 0, profile.Daw.Count);
        if (connected && capture)
        {
            var piano = Assert.Single(profile.Daw, t => t.Track == 2);
            Assert.Equal(false, piano.Muted);
            Assert.Equal(.8f, piano.Volume);
            Assert.Equal(true, piano.SlotsEnabled);
            Assert.Equal(daw.Tracks[2].Slots, piano.Slots);
        }
        using var stream = new MemoryStream();
        await service.ExportAsync(profile.Id, stream);
        stream.Position = 0;
        var imported = await service.ImportAsync(stream);
        Assert.Equal(profile.Daw.Count, imported.Daw.Count);
        await service.LoadAsync();
        Assert.Equal(profile.Daw.Count, service.Profiles.First(p => p.Id == profile.Id).Daw.Count);
        if (connected && capture)
        {
            var restored = service.Profiles.First(p => p.Id == profile.Id).Daw.First(t => t.Track == 2);
            Assert.Equal(profile.Daw.First(t => t.Track == 2).Slots, restored.Slots);
            Assert.Equal(.8f, restored.Volume);
            Assert.Equal(false, restored.Muted);
            Assert.Equal(true, restored.SlotsEnabled);
        }
        Assert.Empty(daw.Calls);
    }

    [Fact]
    public async Task OldJsonDoesNotTouchDaw()
    {
        using var rig = new TestRig();
        var daw = Daw();
        var service = Service(rig, daw);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"id":"old","name":"Old"}"""));
        var profile = await service.ImportAsync(stream);
        Assert.Empty(profile.Daw);
        await service.ApplyAsync(profile.Id, TimeSpan.Zero);
        Assert.Empty(daw.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ApplyOnlyDifferencesAndIgnoreNulls(bool connected)
    {
        using var rig = new TestRig();
        var daw = Daw();
        if (!connected) daw.State = DawConnectionState.WaitingForDaw;
        var service = Service(rig, daw);
        await service.SaveAsync(new() { Id = "fl", Name = "FL", Daw = [
            new(2, true, .4f, false, [new(0, false), new(3, false)]),
            new(0, true, .7f, false), new(8)] });
        var apply = service.ApplyAsync("fl", TimeSpan.FromMilliseconds(32));
        Assert.Equal(connected ? 4 : 0, daw.Calls.Count);
        if (connected)
        {
            Assert.Contains(("Mute", 2, -1, (object)true), daw.Calls);
            Assert.Contains(("Volume", 2, -1, (object).4f), daw.Calls);
            Assert.Contains(("Effects", 2, -1, (object)false), daw.Calls);
            Assert.Contains(("Slot", 2, 0, (object)false), daw.Calls);
        }
        // Le volume FL est déjà posé avant la fin du fondu Voicemeeter.
        for (var i = 0; i < 4 && !apply.IsCompleted; i++)
        {
            rig.Time.Advance(TimeSpan.FromMilliseconds(16));
            await Task.Delay(10);
        }
        await apply.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(connected ? 4 : 0, daw.Calls.Count);
        // FL ouvert après le profil (règle au lancement de FL) : l'état FL part dès la connexion.
        if (!connected) { daw.Connect(); Assert.Equal(4, daw.Calls.Count); }
        daw.Calls.Clear();
        await service.SaveAsync(new() { Id = "null", Name = "Null", Daw = [new(2)] });
        await service.ApplyAsync("null", TimeSpan.Zero);
        Assert.Empty(daw.Calls);
    }

    [Theory]
    [InlineData(HotkeyAction.ToggleDawSlot, true)]
    [InlineData(HotkeyAction.ToggleDawSlot, false)]
    [InlineData(HotkeyAction.ToggleDawTrackMute, true)]
    [InlineData(HotkeyAction.ToggleDawTrackMute, false)]
    public async Task HotkeysToggleKnownStateAndRespectOsd(HotkeyAction action, bool enabled)
    {
        using var rig = new TestRig();
        var daw = Daw();
        daw.Tracks = new Dictionary<int, DawTrack> { [2] = daw.Tracks[2] with { Muted = enabled, Slots = [new(0, enabled)] } };
        var osd = new FakeOsd();
        var log = new RecordingLogger<ActionDispatcher>();
        using var coordinator = new AutomationCoordinator(new FakeProcesses(), new FakeForeground(), rig.Profiles,
            rig.Mixer, rig.Settings, osd, NullLogger<AutomationCoordinator>.Instance);
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd,
            log, coordinator, daw: daw);
        var binding = new HotkeyBinding(action, HotkeyModifiers.None, 0, DawTrack: 2);
        await dispatcher.HandleAsync(new(binding, false));
        Assert.Empty(daw.Calls);
        await dispatcher.HandleAsync(new(binding, true));
        var call = Assert.Single(daw.Calls);
        Assert.Equal(!enabled, call.Value);
        Assert.Equal(action == HotkeyAction.ToggleDawSlot ? "Slot" : "Mute", call.Kind);
        Assert.Equal(action == HotkeyAction.ToggleDawSlot
            ? enabled ? "Piano : Slot 1 coupé" : "Piano : Slot 1 activé"
            : enabled ? "Piano rétablie" : "Piano coupée", Assert.Single(osd.Messages).Text);
        await rig.Settings.UpdateAsync(s => s with { ShowOsd = false });
        await dispatcher.HandleAsync(new(binding, true));
        Assert.Single(osd.Messages);
        await dispatcher.HandleAsync(new(binding with { DawTrack = 99 }, true));
        await dispatcher.HandleAsync(new(binding with { Action = HotkeyAction.ToggleDawSlot, DawSlot = 9 }, true));
        daw.State = DawConnectionState.WaitingForDaw;
        await dispatcher.HandleAsync(new(binding, true));
        Assert.Equal(2, daw.Calls.Count);
        Assert.Single(osd.Messages);
        Assert.Equal(3, log.Levels.Count(level => level == LogLevel.Warning));
        Assert.DoesNotContain(LogLevel.Error, log.Levels);
    }
}
