using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;

namespace Mixdeck.Core.Tests;

public sealed class DiscordMuteSyncTests
{
    private sealed class FakeDiscord : IDiscordVoice
    {
        public DiscordConnectionState State { get; private set; } = DiscordConnectionState.Disconnected;
        public string? ErrorMessage => null;
        public bool HasCredentials => true;
        public List<bool> Writes { get; } = [];
        public event EventHandler<DiscordConnectionState>? StateChanged;
        public event EventHandler<bool>? MuteChanged;
        public void StateTo(DiscordConnectionState state) { State = state; StateChanged?.Invoke(this, state); }
        public void MuteTo(bool value) => MuteChanged?.Invoke(this, value);
        public Task SetMuteAsync(bool muted, CancellationToken cancellationToken = default)
        {
            Writes.Add(muted);
            MuteTo(muted); // Discord can acknowledge a local write via VOICE_SETTINGS_UPDATE.
            return Task.CompletedTask;
        }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AuthorizeAsync(string clientSecret, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ForgetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Rig : IAsyncDisposable
    {
        public FakeMixer Mixer { get; } = new();
        public FakeSettings Settings { get; } = new();
        public FakeDiscord Discord { get; } = new();
        public DiscordMuteSync Sync { get; }
        public Rig() => Sync = new(Mixer, Discord, Settings, NullLogger<DiscordMuteSync>.Instance);
        public async Task StartAsync()
        {
            await Settings.UpdateAsync(s => s with { Discord = new() { Enabled = true, ClientId = "fake-id" } });
            Sync.Start();
            await Sync.DrainAsync();
            Discord.StateTo(DiscordConnectionState.Connected);
            await DrainAsync();
            Discord.Writes.Clear();
        }
        public async Task DrainAsync() { await Sync.DrainAsync(); await Sync.DrainAsync(); }
        public void Mic(bool muted, int index = 0) => Mixer.PublishSnapshot(Mixer.Snapshot with
        { Strips = Mixer.Snapshot.Strips.Select(s => s.Index == index ? s with { Mute = muted } : s).ToArray() });
        public ValueTask DisposeAsync() => Sync.DisposeAsync();
    }

    [Fact]
    public async Task MixdeckMuteAndUnmuteReachDiscordWithoutEchoWrites()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Mic(true);
        await r.DrainAsync();
        r.Mic(false);
        await r.DrainAsync();
        Assert.Equal([true, false], r.Discord.Writes);
        Assert.Empty(r.Mixer.Floats);
    }

    [Fact]
    public async Task RapidMixdeckTogglesDoNotReplayDelayedDiscordAcknowledgements()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Mic(true);
        r.Mic(false);
        await r.DrainAsync();
        Assert.Equal([true, false], r.Discord.Writes);
        Assert.Empty(r.Mixer.Floats);
        Assert.False(r.Mixer.Snapshot.Strips[0].Mute);
    }

    [Fact]
    public async Task DiscordMuteAndUnmuteUpdateMicWithoutEchoIncludingDeferredMixerSnapshots()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Mixer.DeferWrites = true;
        r.Discord.MuteTo(true);
        await r.DrainAsync();
        Assert.Equal(("Strip[0].Mute", 1f), Assert.Single(r.Mixer.Floats));
        r.Mixer.PublishSnapshot(r.Mixer.Snapshot); // Still old: write has not reached the audio thread yet.
        await r.DrainAsync();
        r.Mixer.FlushWrites();
        await r.DrainAsync();
        Assert.True(r.Mixer.Snapshot.Strips[0].Mute);
        r.Discord.MuteTo(false);
        await r.DrainAsync();
        r.Mixer.FlushWrites();
        await r.DrainAsync();
        Assert.False(r.Mixer.Snapshot.Strips[0].Mute);
        Assert.Empty(r.Discord.Writes);
        Assert.Equal(2, r.Mixer.Floats.Count);
    }

    [Fact]
    public async Task RapidDiscordReversalSupersedesUnobservedMixerWrite()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Mixer.DeferWrites = true;
        r.Discord.MuteTo(true);
        r.Discord.MuteTo(false);
        await r.DrainAsync();
        Assert.Equal(new[] { ("Strip[0].Mute", 1f), ("Strip[0].Mute", 0f) }, r.Mixer.Floats);
        // The intermediate mixer observation must not reverse the newer Discord intent.
        r.Mic(true);
        await r.DrainAsync();
        Assert.Empty(r.Discord.Writes);
        r.Mixer.FlushWrites();
        await r.DrainAsync();
        Assert.False(r.Mixer.Snapshot.Strips[0].Mute);
        Assert.Empty(r.Discord.Writes);
    }

    [Fact]
    public async Task DiscordReconnectionPushesMixdeckAndIgnoresInitialDiscordState()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Discord.StateTo(DiscordConnectionState.Disconnected);
        r.Mic(true);
        r.Discord.MuteTo(false);
        r.Discord.StateTo(DiscordConnectionState.Connected);
        await r.DrainAsync();
        Assert.Equal([true], r.Discord.Writes);
        Assert.Empty(r.Mixer.Floats);
    }

    [Fact]
    public async Task ChangingMicrophoneStripPushesNewStripAndReceivesOnlyThere()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Mic(true, 1);
        await r.Settings.UpdateAsync(s => s with { MicStripIndex = 1 });
        await r.DrainAsync();
        Assert.Equal([true], r.Discord.Writes);
        r.Discord.MuteTo(false);
        await r.DrainAsync();
        Assert.Equal(("Strip[1].Mute", 0f), Assert.Single(r.Mixer.Floats));
    }

    [Fact]
    public async Task DisabledAndDisposedSynchronizationDoesNothing()
    {
        await using var r = new Rig();
        await r.StartAsync();
        await r.Settings.UpdateAsync(s => s with { Discord = s.Discord with { Enabled = false } });
        await r.DrainAsync();
        r.Mic(true);
        r.Discord.MuteTo(true);
        await r.DrainAsync();
        Assert.Empty(r.Mixer.Floats);
        Assert.Empty(r.Discord.Writes);
        await r.Sync.DisposeAsync();
        r.Discord.MuteTo(false);
        r.Mic(false);
        Assert.Empty(r.Mixer.Floats);
        Assert.Empty(r.Discord.Writes);
    }

    [Fact]
    public async Task UnavailableMixerIsNeverOverwrittenAndReconnectionPushesItsState()
    {
        await using var r = new Rig();
        await r.StartAsync();
        r.Mixer.SetState(MixerConnectionState.Disconnected);
        r.Discord.MuteTo(true);
        await r.DrainAsync();
        Assert.Empty(r.Mixer.Floats);
        r.Mixer.SetState(MixerConnectionState.Connected);
        await r.DrainAsync();
        Assert.Equal([false], r.Discord.Writes);
    }
}
