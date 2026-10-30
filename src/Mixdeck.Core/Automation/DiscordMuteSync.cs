using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Automation;

/// <summary>Serializes bidirectional mute updates. Mixdeck wins whenever either connection returns.</summary>
public sealed class DiscordMuteSync(IMixer mixer, IDiscordVoice discord, ISettingsStore settings,
    ILogger<DiscordMuteSync> logger) : IAsyncDisposable
{
    private readonly Channel<Func<Task>> queue = Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private Task? worker;
    private bool disposed;
    private bool connected;
    private bool? discordMute;
    private bool? pendingMixerMute;
    private readonly List<(bool Muted, long Sent)> expectedEchoes = [];
    private int micIndex = -1;
    private bool enabled;

    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null) return;
            mixer.SnapshotChanged += SnapshotChanged;
            mixer.StateChanged += MixerStateChanged;
            discord.MuteChanged += DiscordMuteChanged;
            discord.StateChanged += DiscordStateChanged;
            settings.Changed += SettingsChanged;
            connected = discord.State == DiscordConnectionState.Connected;
            Enqueue(ReconfigureAsync);
            worker = Task.Run(RunAsync);
        }
    }

    private void Enqueue(Func<Task> action) => queue.Writer.TryWrite(action);
    private async Task RunAsync()
    {
        try
        {
            await foreach (var action in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                try { await action(); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                // RPC failures must not break audio control or leak OAuth response bodies to logs.
                catch (Exception) { logger.LogWarning("Discord mute synchronization could not complete."); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }

    private bool CanSync => enabled && connected && mixer.State == MixerConnectionState.Connected;
    private bool? ReadMute(MixerSnapshot snapshot) => snapshot.Strips.FirstOrDefault(s => s.Index == micIndex)?.Mute;
    private Task PushAsync(MixerSnapshot snapshot, bool force = false)
    {
        if (!CanSync || ReadMute(snapshot) is not { } muted) return Task.CompletedTask;
        // An unrelated mixer snapshot can still contain the old value while a queued write is pending.
        if (pendingMixerMute is { } expected)
        {
            if (muted != expected) return Task.CompletedTask;
            pendingMixerMute = null;
        }
        if (!force && discordMute == muted) return Task.CompletedTask;
        return SendAsync(muted);
    }
    private async Task SendAsync(bool muted)
    {
        var echo = (Muted: muted, Sent: Environment.TickCount64);
        expectedEchoes.Add(echo);
        try
        {
            await discord.SetMuteAsync(muted, lifetime.Token);
            discordMute = muted;
        }
        catch
        {
            expectedEchoes.Remove(echo);
            throw;
        }
    }
    private Task ReconfigureAsync()
    {
        var current = settings.Current;
        if (micIndex == current.MicStripIndex && enabled == current.Discord.Enabled) return Task.CompletedTask;
        micIndex = current.MicStripIndex;
        enabled = current.Discord.Enabled;
        pendingMixerMute = null;
        discordMute = null;
        expectedEchoes.Clear();
        return PushAsync(mixer.Snapshot, true);
    }
    private void SettingsChanged(object? sender, AppSettings value) => Enqueue(ReconfigureAsync);
    private void SnapshotChanged(object? sender, MixerSnapshot snapshot) => Enqueue(() => PushAsync(snapshot));
    private void MixerStateChanged(object? sender, MixerConnectionState state) => Enqueue(() =>
    {
        pendingMixerMute = null;
        discordMute = null;
        expectedEchoes.Clear();
        return state == MixerConnectionState.Connected ? PushAsync(mixer.Snapshot, true) : Task.CompletedTask;
    });
    private void DiscordStateChanged(object? sender, DiscordConnectionState state) => Enqueue(() =>
    {
        connected = state == DiscordConnectionState.Connected;
        discordMute = null;
        pendingMixerMute = null;
        expectedEchoes.Clear();
        return connected ? PushAsync(mixer.Snapshot, true) : Task.CompletedTask;
    });
    private void DiscordMuteChanged(object? sender, bool muted) => Enqueue(() =>
    {
        // Discord may publish a SET acknowledgement after the next local toggle has already been sent.
        // Consume those in wire order instead of applying an obsolete acknowledgement to the microphone.
        expectedEchoes.RemoveAll(echo => Environment.TickCount64 - echo.Sent > 5000);
        if (expectedEchoes.Count > 0 && expectedEchoes[0].Muted == muted)
        {
            expectedEchoes.RemoveAt(0);
            return Task.CompletedTask;
        }
        if (!CanSync || discordMute == muted || ReadMute(mixer.Snapshot) is not { } current) return Task.CompletedTask;
        discordMute = muted;
        // A preceding Discord write may still be waiting in the audio thread's queue. Even if the
        // snapshot already equals this new value, enqueue it after that write to preserve the latest intent.
        if (current == muted && pendingMixerMute is null) return Task.CompletedTask;
        pendingMixerMute = muted;
        mixer.SetFloat(VmParam.StripMute(micIndex), VmParam.FromBool(muted));
        return Task.CompletedTask;
    });

    internal Task DrainAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.Writer.TryWrite(() => { completion.TrySetResult(); return Task.CompletedTask; }))
            completion.TrySetException(new ObjectDisposedException(nameof(DiscordMuteSync)));
        return completion.Task.WaitAsync(lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            mixer.SnapshotChanged -= SnapshotChanged;
            mixer.StateChanged -= MixerStateChanged;
            discord.MuteChanged -= DiscordMuteChanged;
            discord.StateChanged -= DiscordStateChanged;
            settings.Changed -= SettingsChanged;
            queue.Writer.TryComplete();
            lifetime.Cancel();
            running = worker;
        }
        if (running is not null) await running.ConfigureAwait(false);
        lifetime.Dispose();
    }
}
