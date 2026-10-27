using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Rules;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Automation;
public sealed class AutomationCoordinator(IProcessWatcher processes, IForegroundWatcher foreground,
    IProfileService profiles, IMixer mixer, ISettingsStore settings, IOsdService osd,
    ILogger<AutomationCoordinator> logger) : IDisposable
{
    private readonly Channel<Func<Task>> queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly RuleEngine engine = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private Task? worker;
    private bool disposed;
    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (worker is not null) return;
            processes.ProcessStarted += Started;
            processes.ProcessExited += Exited;
            foreground.ForegroundProcessChanged += ForegroundChanged;
            settings.Changed += SettingsChanged;
            queue.Writer.TryWrite(async () =>
            {
                engine.SetRules(settings.Current.Rules);
                engine.SetBaselineProfile(settings.Current.LastProfileId);
                engine.SetBaselineMicMuted(mixer.Snapshot.Strips.FirstOrDefault(s => s.Index == settings.Current.MicStripIndex)?.Mute ?? false);
                engine.Initialize(processes.GetRunningProcessNames(), foreground.Current);
                await ApplyAsync(engine.Evaluate());
            });
            processes.Start();
            foreground.Start();
            worker = Task.Run(RunAsync);
        }
    }
    private async Task RunAsync()
    {
        try
        {
            await foreach (var action in queue.Reader.ReadAllAsync(lifetime.Token))
            {
                try { await action(); }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception ex) { logger.LogError(ex, "Échec d'une action automatique"); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private void Started(object? sender, ProcessEventArgs e) => queue.Writer.TryWrite(() => ApplyAsync(engine.OnProcessStarted(e.ProcessName)));
    private void Exited(object? sender, ProcessEventArgs e) => queue.Writer.TryWrite(() => ApplyAsync(engine.OnProcessExited(e.ProcessName)));
    private void ForegroundChanged(object? sender, string? exe) => queue.Writer.TryWrite(() => ApplyAsync(engine.OnForegroundChanged(exe)));
    private void SettingsChanged(object? sender, AppSettings value) => queue.Writer.TryWrite(() =>
    {
        engine.SetRules(settings.Current.Rules);
        return ApplyAsync(engine.Evaluate());
    });
    private async Task ApplyAsync(RuleDecision decision)
    {
        if (decision.ProfileId is { } id)
        {
            try
            {
                await profiles.ApplyAsync(id, cancellationToken: lifetime.Token);
                if (!lifetime.IsCancellationRequested && settings.Current.ShowOsd && profiles.Active is { } active)
                {
                    var cause = decision.ProfileCause;
                    osd.Show("Profil : " + active.Name + (cause is null ? "" : " (" + cause.ProcessName + ")"), active.Icon);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            { logger.LogError(ex, "Impossible d'appliquer le profil {ProfileId}", id); }
        }
        if (!lifetime.IsCancellationRequested && decision.MicMuted is { } muted)
        {
            var index = settings.Current.MicStripIndex;
            if (index is < 0 or >= BananaLayout.StripCount) return;
            mixer.SetFloat(VmParam.StripMute(index), VmParam.FromBool(muted));
            if (settings.Current.ShowOsd) osd.Show(muted ? "Micro coupé" : "Micro activé", muted ? "\uF781" : "\uE720", muted);
        }
    }
    public Task ApplyManualProfileAsync(string profileId)
    {
        Start();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.Writer.TryWrite(async () =>
        {
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                await profiles.ApplyAsync(profileId, cancellationToken: lifetime.Token);
                engine.SetBaselineProfile(profileId);
                await settings.UpdateAsync(s => s with { LastProfileId = profileId }, lifetime.Token);
                completion.TrySetResult();
            }
            catch (Exception ex) { completion.TrySetException(ex); }
        })) completion.TrySetException(new ObjectDisposedException(nameof(AutomationCoordinator)));
        return completion.Task.WaitAsync(lifetime.Token);
    }
    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed) return;
            disposed = true;
            processes.ProcessStarted -= Started;
            processes.ProcessExited -= Exited;
            foreground.ForegroundProcessChanged -= ForegroundChanged;
            settings.Changed -= SettingsChanged;
            queue.Writer.TryComplete();
            lifetime.Cancel();
        }
    }
}
