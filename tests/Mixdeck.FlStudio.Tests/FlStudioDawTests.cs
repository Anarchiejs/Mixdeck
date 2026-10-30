using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.FlStudio.Tests;

public sealed class FlStudioDawTests
{
    [Fact]
    public async Task Connects_lists_and_follows_all_commands_and_external_changes()
    {
        var sim = new SimulatedFlStudio();
        var clock = new FakeTimeProvider();
        await using var daw = Create(sim, new Settings(), clock);
        var notifications = new ConcurrentQueue<(DawTrack Track, int Thread)>();
        var caller = Environment.CurrentManagedThreadId;
        daw.TrackChanged += (_, t) => notifications.Enqueue((t, Environment.CurrentManagedThreadId));
        await daw.StartAsync();
        await Until(() => daw.Tracks.Count == 4 && daw.SelectedTrack == 0);
        daw.State.Should().Be(DawConnectionState.Connected);
        daw.Tracks[1].Name.Should().Be("Voix");
        daw.Tracks[1].Slots.Should().Equal(new DawSlot(0, true, "Pro-Q"), new DawSlot(1, true, "Compresseur"), new DawSlot(2, false, "Pitcher"));
        daw.SetMute(1, true);
        await Until(() => daw.Tracks[1].Muted);
        daw.SetSlotsEnabled(1, false);
        await Until(() => !daw.Tracks[1].SlotsEnabled);
        daw.SetSlot(1, 2, true);
        await Until(() => daw.Tracks[1].Slots[2].Enabled);
        daw.SetVolume(2, 1);
        await Until(() => daw.Tracks[2].Volume == 1);
        daw.SelectTrack(3);
        await Until(() => daw.SelectedTrack == 3);
        var before = notifications.Count;
        daw.ChangePreset(1, 0, true);
        await Until(() => notifications.Count > before);
        sim.ChangeTrack(daw.Tracks[1] with { Muted = false, Name = "Voix FL" });
        await Until(() => daw.Tracks[1].Name == "Voix FL" && !daw.Tracks[1].Muted);
        sim.SelectTrack(2);
        await Until(() => daw.SelectedTrack == 2);
        notifications.Should().OnlyContain(n => n.Thread != caller);
    }

    [Fact]
    public async Task ListEnd_prunes_old_tracks_and_foreign_messages_are_ignored()
    {
        var sim = new SimulatedFlStudio();
        await using var daw = Create(sim, new Settings(), new FakeTimeProvider());
        await daw.StartAsync();
        await Until(() => daw.Tracks.Count == 4);
        var snapshot = daw.Tracks;
        sim.RemoveTrack(3);
        sim.EmitSysex([0xF0, 1, 2, 3, 0x48, 0xF7]);
        await daw.RefreshAsync();
        await Until(() => daw.Tracks.Count == 3);
        daw.Tracks.Should().NotContainKey(3);
        snapshot.Should().HaveCount(4);
        daw.State.Should().Be(DawConnectionState.Connected);
    }

    [Fact]
    public async Task Spontaneous_list_from_fl_replaces_tracks_without_refresh()
    {
        var sim = new SimulatedFlStudio();
        await using var daw = Create(sim, new Settings(), new FakeTimeProvider());
        await daw.StartAsync();
        await Until(() => daw.Tracks.Count == 4);
        // Nouveau projet dans FL : le script renvoie de lui-même la liste encadrée, sans « Relire les pistes ».
        sim.EmitSysex([0xF0, 0x7D, 0x4D, 0x58, 0x4A, 0xF7]);
        sim.EmitSysex([0xF0, 0x7D, 0x4D, 0x58, 0x48, 0, 1, 0x66, 0x66, 0, 1, 0, 0, 0, 0, 1, 65, 0xF7]);
        sim.EmitSysex([0xF0, 0x7D, 0x4D, 0x58, 0x4B, 0, 1, 0xF7]);
        await Until(() => daw.Tracks.Count == 1);
        daw.Tracks[1].Name.Should().Be("A");
    }

    [Fact]
    public async Task Two_unanswered_keepalives_clear_state_then_reconnect()
    {
        var sim = new SimulatedFlStudio();
        var clock = new FakeTimeProvider();
        await using var daw = Create(sim, new Settings(), clock);
        await daw.StartAsync();
        await Until(() => daw.Tracks.Count == 4 && daw.SelectedTrack == 0);
        sim.Respond = false;
        var pingCount = Pings(sim);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Until(() => Pings(sim) == pingCount + 1);
        clock.Advance(TimeSpan.FromSeconds(1));
        // Un tour témoin permet d'attendre le traitement de la première échéance.
        daw.SetMute(1, true);
        await Until(() => sim.SentMessages.Any(m => m[4] == 3));
        daw.State.Should().Be(DawConnectionState.Connected);
        clock.Advance(TimeSpan.FromSeconds(4));
        await Until(() => Pings(sim) == pingCount + 2);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => daw.State == DawConnectionState.WaitingForDaw);
        daw.Tracks.Should().BeEmpty();
        daw.SelectedTrack.Should().Be(-1);
        var count = sim.SentMessages.Count;
        daw.SetMute(1, true);
        daw.SetVolume(1, .5f);
        await daw.RefreshAsync();
        sim.SentMessages.Should().HaveCount(count);
        sim.Respond = true;
        clock.Advance(TimeSpan.FromSeconds(2));
        await Until(() => daw.State == DawConnectionState.Connected && daw.Tracks.Count == 4);
        daw.Tracks[1].Muted.Should().BeFalse();
    }

    [Fact]
    public async Task Initial_handshake_retries_after_one_second_without_pong()
    {
        var sim = new SimulatedFlStudio { Respond = false };
        var clock = new FakeTimeProvider();
        await using var daw = Create(sim, new Settings(), clock);
        await daw.StartAsync();
        await Until(() => Pings(sim) == 1);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Until(() => daw.State == DawConnectionState.WaitingForDaw);
        // L'entrée est fermée à l'échéance ; attendre la fermeture avant d'avancer le temps.
        await Until(() => CanOpenInput(sim));
        sim.Respond = true;
        clock.Advance(TimeSpan.FromSeconds(2));
        await Until(() => daw.Tracks.Count == 4);
    }

    [Fact]
    public async Task Missing_ports_settings_changes_and_disable_are_observed()
    {
        var sim = new SimulatedFlStudio { PortsAvailable = false };
        var settings = new Settings();
        var clock = new FakeTimeProvider();
        await using var daw = Create(sim, settings, clock);
        await daw.StartAsync();
        await Until(() => daw.State == DawConnectionState.PortsMissing);
        sim.PortsAvailable = true;
        clock.Advance(TimeSpan.FromSeconds(2));
        await Until(() => daw.Tracks.Count == 4);
        await settings.UpdateAsync(s => s with { FlStudio = s.FlStudio with { InputPort = "absent" } });
        await Until(() => daw.State == DawConnectionState.PortsMissing);
        daw.Tracks.Should().BeEmpty();
        await settings.UpdateAsync(s => s with { FlStudio = s.FlStudio with { InputPort = "FL to Mixdeck" } });
        await Until(() => daw.Tracks.Count == 4);
        await settings.UpdateAsync(s => s with { FlStudio = s.FlStudio with { Enabled = false } });
        await Until(() => daw.State == DawConnectionState.Disabled);
        daw.Tracks.Should().BeEmpty();
        using var input = sim.OpenInput("FL to Mixdeck", _ => { });
    }

    [Fact]
    public async Task Volume_bursts_keep_last_value_per_track_and_limit_total_rate()
    {
        var sim = new SimulatedFlStudio();
        var clock = new FakeTimeProvider();
        await using var daw = Create(sim, new Settings(), clock);
        await daw.StartAsync();
        await Until(() => daw.Tracks.Count == 4);
        daw.SetVolume(0, .1f);
        await Until(() => Volumes(sim).Length == 1);
        for (var i = 0; i < 100; i++) daw.SetVolume(1, i / 100f);
        daw.SetVolume(2, .25f);
        // Au même instant, aucun autre volume ne peut être envoyé.
        daw.SetMute(0, true);
        await Until(() => daw.Tracks[0].Muted);
        Volumes(sim).Should().HaveCount(1);
        clock.Advance(TimeSpan.FromMilliseconds(34));
        await Until(() => Volumes(sim).Length == 2);
        Volumes(sim)[1].Should().Equal(FlProtocol.Volume(1, .99f));
        clock.Advance(TimeSpan.FromMilliseconds(34));
        await Until(() => Volumes(sim).Length == 3);
        Volumes(sim)[2].Should().Equal(FlProtocol.Volume(2, .25f));
    }

    [Fact]
    public async Task Error_is_only_logged_and_throwing_subscriber_does_not_stop_client()
    {
        var sim = new SimulatedFlStudio();
        var log = new TestLogger();
        await using var daw = new FlStudioDaw(sim, new Settings(), log, new FakeTimeProvider());
        daw.TrackChanged += (_, _) => throw new InvalidOperationException("Abonné test");
        await daw.StartAsync();
        await Until(() => daw.Tracks.Count == 4);
        var before = log.Warnings;
        daw.SetSlot(2, 0, true);
        await Until(() => log.Warnings > before);
        daw.State.Should().Be(DawConnectionState.Connected);
        daw.SetMute(1, true);
        await Until(() => daw.Tracks[1].Muted);
    }

    [Fact]
    public async Task Start_is_idempotent_dispose_closes_ports_and_disabled_start_sends_nothing()
    {
        var sim = new SimulatedFlStudio();
        var settings = new Settings { Current = new() { FlStudio = new() { Enabled = false } } };
        var daw = Create(sim, settings, new FakeTimeProvider());
        await daw.StartAsync();
        await daw.StartAsync();
        daw.State.Should().Be(DawConnectionState.Disabled);
        sim.SentMessages.Should().BeEmpty();
        await settings.UpdateAsync(s => s with { FlStudio = s.FlStudio with { Enabled = true } });
        await Until(() => daw.Tracks.Count == 4);
        await daw.DisposeAsync();
        await daw.DisposeAsync();
        using var input = sim.OpenInput("FL to Mixdeck", _ => { });
        Func<Task> restart = () => daw.StartAsync();
        await restart.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static FlStudioDaw Create(SimulatedFlStudio sim, Settings settings, FakeTimeProvider clock) =>
        new(sim, settings, NullLogger<FlStudioDaw>.Instance, clock);
    private static int Pings(SimulatedFlStudio sim) => sim.SentMessages.Count(m => m[4] == 1);
    private static byte[][] Volumes(SimulatedFlStudio sim) => sim.SentMessages.Where(m => m[4] == 4).ToArray();
    private static bool CanOpenInput(SimulatedFlStudio sim)
    {
        try { using var input = sim.OpenInput("FL to Mixdeck", _ => { }); return true; }
        catch (MidiPortException) { return false; }
    }
    private static async Task Until(Func<bool> predicate)
    {
        var until = Environment.TickCount64 + 3000;
        while (!predicate() && Environment.TickCount64 < until) await Task.Delay(5);
        predicate().Should().BeTrue("le thread de fond doit avoir traité le message");
    }
    private sealed class Settings : ISettingsStore
    {
        private AppSettings _current = new();
        public AppSettings Current { get => Volatile.Read(ref _current); set => Volatile.Write(ref _current, value); }
        public event EventHandler<AppSettings>? Changed;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings; Changed?.Invoke(this, settings); return Task.CompletedTask;
        }
        public Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default) => SaveAsync(change(Current), cancellationToken);
    }
    private sealed class TestLogger : ILogger<FlStudioDaw>
    {
        private int _warnings;
        public int Warnings => Volatile.Read(ref _warnings);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (logLevel == LogLevel.Warning) Interlocked.Increment(ref _warnings); }
    }
}
