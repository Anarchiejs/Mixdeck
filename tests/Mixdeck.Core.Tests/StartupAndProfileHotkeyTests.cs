using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;

namespace Mixdeck.Core.Tests;

public sealed class StartupAndProfileHotkeyTests
{
    private static AutomationCoordinator Coordinator(TestRig rig, FakeProcesses? processes = null, FakeOsd? osd = null) =>
        new(processes ?? new(), new FakeForeground(), rig.Profiles, rig.Mixer, rig.Settings, osd ?? new(), NullLogger<AutomationCoordinator>.Instance);

    [Fact]
    public async Task ProfileHotkeyUsesManualBaselineAndShowsOsdOnlyOnPress()
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        var processes = new FakeProcesses();
        await rig.Profiles.SaveAsync(new() { Id = "music", Name = "Musique", Icon = "♫", Buses = [new(0, -8, false)] });
        await rig.Profiles.SaveAsync(new() { Id = "game", Name = "Jeu" });
        await rig.Settings.UpdateAsync(s => s with { Rules = [new() { Id = "game", Name = "Jeu", ProcessName = "game", ProfileId = "game" }] });
        using var coordinator = Coordinator(rig, processes, osd);
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd, NullLogger<ActionDispatcher>.Instance, coordinator);
        var binding = new HotkeyBinding(HotkeyAction.ApplyProfile, HotkeyModifiers.None, 0, ProfileId: "music");
        await dispatcher.HandleAsync(new(binding, false));
        Assert.Null(rig.Profiles.Active);
        await dispatcher.HandleAsync(new(binding, true));
        Assert.Equal("music", rig.Settings.Current.LastProfileId);
        Assert.Equal(-8, rig.Mixer.Snapshot.Buses[0].Gain);
        Assert.Equal(("Profil : Musique", "♫", false), osd.Messages.Last());
        processes.StartProcess("game");
        await TestRig.UntilAsync(() => rig.Profiles.Active?.Id == "game");
        processes.ExitProcess("game");
        await TestRig.UntilAsync(() => rig.Profiles.Active?.Id == "music");
        await rig.Settings.UpdateAsync(s => s with { ShowOsd = false });
        var count = osd.Messages.Count;
        await dispatcher.HandleAsync(new(binding, true));
        Assert.Equal(count, osd.Messages.Count);
    }

    private sealed class RecordingLogger : ILogger<ActionDispatcher>
    {
        public int Warnings { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Warning) Warnings++; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    public async Task MissingShortcutProfileOnlyLogs(string? id)
    {
        using var rig = new TestRig();
        var osd = new FakeOsd();
        var log = new RecordingLogger();
        using var coordinator = Coordinator(rig);
        var dispatcher = new ActionDispatcher(rig.Mixer, rig.Profiles, rig.Settings, osd, log, coordinator);
        await dispatcher.HandleAsync(new(new(HotkeyAction.ApplyProfile, HotkeyModifiers.None, 0, ProfileId: id), true));
        Assert.Equal(1, log.Warnings);
        Assert.Null(rig.Profiles.Active);
        Assert.Null(rig.Settings.Current.LastProfileId);
        Assert.Empty(rig.Mixer.Floats);
        Assert.Empty(osd.Messages);
    }

    [Fact]
    public async Task StartupWaitsForConnectionAndRunsOnlyOnceThenRulesTakeOver()
    {
        using var rig = new TestRig();
        var processes = new FakeProcesses { Running = ["game"] };
        var osd = new FakeOsd();
        await rig.Profiles.SaveAsync(new() { Id = "startup", Name = "Accueil", Buses = [new(0, -5, false)] });
        await rig.Profiles.SaveAsync(new() { Id = "game", Name = "Jeu", Buses = [new(0, -10, false)] });
        await rig.Settings.UpdateAsync(s => s with { StartupProfileId = "startup", Rules = [
            new() { Id = "game", Name = "Jeu", ProcessName = "game", ProfileId = "game" }] });
        rig.Mixer.SetState(MixerConnectionState.Disconnected);
        using var coordinator = Coordinator(rig, processes, osd);
        var first = coordinator.ApplyStartupProfileAsync();
        var second = coordinator.ApplyStartupProfileAsync();
        Assert.Same(first, second);
        Assert.False(first.IsCompleted);
        Assert.Empty(rig.Mixer.Scripts);
        rig.Mixer.SetState(MixerConnectionState.Connected);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Buses[0].Gain == -10);
        Assert.Equal(1, osd.Messages.Count(m => m.Text == "Profil : Accueil"));
        Assert.Equal("game", rig.Profiles.Active?.Id);
        var scripts = rig.Mixer.Scripts.Count;
        rig.Mixer.SetState(MixerConnectionState.Disconnected);
        rig.Mixer.SetState(MixerConnectionState.Connected);
        await coordinator.ApplyStartupProfileAsync();
        // Une application manuelle sert de barrière de file après la reconnexion.
        await coordinator.ApplyManualProfileAsync("game");
        Assert.Equal(scripts + 1, rig.Mixer.Scripts.Count);
        Assert.Equal(1, osd.Messages.Count(m => m.Text == "Profil : Accueil"));
    }

    [Fact]
    public async Task AlreadyConnectedStartupRunsOnceAndDoesNotOverwriteSavedManualBaseline()
    {
        using var rig = new TestRig();
        await rig.Profiles.SaveAsync(new() { Id = "startup", Name = "Accueil" });
        await rig.Settings.UpdateAsync(s => s with { StartupProfileId = "startup", LastProfileId = "manual" });
        using var coordinator = Coordinator(rig);
        await coordinator.ApplyStartupProfileAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await coordinator.ApplyStartupProfileAsync();
        Assert.Equal("startup", rig.Profiles.Active?.Id);
        Assert.Single(rig.Mixer.Scripts);
        Assert.Equal("manual", rig.Settings.Current.LastProfileId);
    }

    [Fact]
    public async Task MissingStartupProfileDoesNotBlockRules()
    {
        using var rig = new TestRig();
        await rig.Settings.UpdateAsync(s => s with { StartupProfileId = "missing", Rules = [
            new() { Id = "mic", Name = "Micro", ProcessName = "app", Action = RuleActionKind.SetMicMuted, MicMuted = true }] });
        using var coordinator = Coordinator(rig, new() { Running = ["app"] });
        await coordinator.ApplyStartupProfileAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await TestRig.UntilAsync(() => rig.Mixer.Snapshot.Strips[0].Mute);
        Assert.Null(rig.Profiles.Active);
    }

    [Fact]
    public async Task DisposalCancelsStartupWaitingForConnection()
    {
        using var rig = new TestRig();
        await rig.Settings.UpdateAsync(s => s with { StartupProfileId = "startup" });
        rig.Mixer.SetState(MixerConnectionState.Disconnected);
        using var coordinator = Coordinator(rig);
        var startup = coordinator.ApplyStartupProfileAsync();
        coordinator.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startup);
    }
}

