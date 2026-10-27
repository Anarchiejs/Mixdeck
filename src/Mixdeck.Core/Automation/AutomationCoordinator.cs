using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Rules;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Automation;
public sealed class AutomationCoordinator(IProcessWatcher processes, IForegroundWatcher foreground,
    IProfileService profiles, IMixer mixer, ISettingsStore settings, IOsdService osd,
    ILogger<AutomationCoordinator> logger, DuckingController? ducking = null) : IDisposable
{
    private readonly Channel<Func<Task>> queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly RuleEngine engine = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private Task? worker;
    private bool disposed;
    private bool startupAttempted;
    private readonly TaskCompletionSource startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    // Voicemeeter ignore les écritures hors connexion : les décisions prises pendant ce temps sont rejouées à la reconnexion.
    // Ces champs ne sont lus et écrits que depuis la file.
    private string? pendingProfile;
    private Rule? pendingProfileCause;
    private bool? pendingMic;
    private int micGeneration;
    private bool micMuted;
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
            mixer.StateChanged += MixerStateChanged;
            queue.Writer.TryWrite(async () =>
            {
                engine.SetRules(settings.Current.Rules);
                engine.SetBaselineProfile(settings.Current.LastProfileId);
                engine.SetBaselineMicMuted(mixer.Snapshot.Strips.FirstOrDefault(s => s.Index == settings.Current.MicStripIndex)?.Mute ?? false);
                engine.Initialize(processes.GetRunningProcessNames(), foreground.Current);
                await ApplyStartupWhenConnectedAsync();
                Apply(engine.Evaluate());
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
    private void Enqueue(Action action) => queue.Writer.TryWrite(() => { action(); return Task.CompletedTask; });
    private void Started(object? sender, ProcessEventArgs e) => Enqueue(() => Apply(engine.OnProcessStarted(e.ProcessName)));
    private void Exited(object? sender, ProcessEventArgs e) => Enqueue(() => Apply(engine.OnProcessExited(e.ProcessName)));
    private void ForegroundChanged(object? sender, string? exe) => Enqueue(() => Apply(engine.OnForegroundChanged(exe)));
    private void SettingsChanged(object? sender, AppSettings value) => Enqueue(() =>
    {
        engine.SetRules(settings.Current.Rules);
        Apply(engine.Evaluate());
    });
    private void MixerStateChanged(object? sender, MixerConnectionState state)
    {
        if (state == MixerConnectionState.Connected) queue.Writer.TryWrite(async () =>
        {
            await ApplyStartupWhenConnectedAsync();
            Reconcile();
        });
    }
    /// <summary>Attend la première connexion et l'application unique du profil de démarrage, sans bloquer l'interface.</summary>
    public Task ApplyStartupProfileAsync()
    {
        Start();
        return startup.Task;
    }
    private async Task ApplyStartupWhenConnectedAsync()
    {
        if (startupAttempted) return;
        var id = settings.Current.StartupProfileId;
        if (id is not null && mixer.State != MixerConnectionState.Connected) return;
        startupAttempted = true;
        try
        {
            if (id is not null)
            {
                await ApplyProfileWithDuckingAsync(id);
                if (settings.Current.ShowOsd && profiles.Active is { } active)
                    osd.Show("Profil : " + active.Name, active.Icon);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Impossible d'appliquer le profil de démarrage {ProfileId}", id); }
        finally { startup.TrySetResult(); }
    }
    private async Task ApplyProfileWithDuckingAsync(string id)
    {
        // Le jeton couvre le fondu entier ; les fondus concurrents gardent chacun leur jeton.
        using var suspension = ducking?.SuspendForProfile();
        await profiles.ApplyAsync(id, cancellationToken: lifetime.Token);
    }
    private void Reconcile()
    {
        if (mixer.State != MixerConnectionState.Connected || (pendingProfile is null && pendingMic is null)) return;
        var decision = new RuleDecision(pendingProfile, pendingMic, pendingProfileCause, null);
        (pendingProfile, pendingProfileCause, pendingMic) = (null, null, null);
        Apply(decision);
    }
    // N'attend pas la fin des fondus : la file reste libre pour les décisions suivantes, et un nouveau profil
    // interrompt le fondu en cours (ProfileService annule l'application précédente).
    private void Apply(RuleDecision decision)
    {
        if (lifetime.IsCancellationRequested) return;
        if (mixer.State != MixerConnectionState.Connected)
        {
            if (decision.ProfileId is not null) (pendingProfile, pendingProfileCause) = (decision.ProfileId, decision.ProfileCause);
            if (decision.MicMuted is not null) pendingMic = decision.MicMuted;
            return;
        }
        Task? fade = null;
        if (decision.ProfileId is { } id)
        {
            (pendingProfile, pendingProfileCause) = (null, null);
            fade = ApplyProfileWithDuckingAsync(id);
        }
        if (decision.MicMuted is { } muted)
        {
            pendingMic = null;
            SetMic(muted);
        }
        if (fade is not null) _ = CompleteProfileAsync(fade, decision.ProfileId!, decision.ProfileCause, decision.MicMuted is null ? null : micGeneration);
    }
    private void SetMic(bool muted)
    {
        var index = settings.Current.MicStripIndex;
        if (index < 0 || index >= mixer.Layout.StripCount) return;
        micGeneration++;
        micMuted = muted;
        mixer.SetFloat(VmParam.StripMute(index), VmParam.FromBool(muted));
        if (settings.Current.ShowOsd) osd.Show(muted ? "Micro coupé" : "Micro activé", muted ? "" : "", muted);
    }
    private async Task CompleteProfileAsync(Task fade, string id, Rule? cause, int? micAfter)
    {
        try { await fade; }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { logger.LogError(ex, "Impossible d'appliquer le profil {ProfileId}", id); return; }
        // Un autre profil a pu remplacer celui-ci pendant le fondu : seul le dernier est annoncé.
        if (lifetime.IsCancellationRequested || profiles.Active?.Id != id) return;
        // La fin du fondu remet les coupures du profil ; la décision micro prise en même temps reste prioritaire.
        if (micAfter is { } generation) Enqueue(() =>
        {
            var index = settings.Current.MicStripIndex;
            if (generation == micGeneration && mixer.State == MixerConnectionState.Connected && index >= 0 && index < mixer.Layout.StripCount)
                mixer.SetFloat(VmParam.StripMute(index), VmParam.FromBool(micMuted));
        });
        if (settings.Current.ShowOsd && profiles.Active is { } active)
            osd.Show("Profil : " + active.Name + (cause is null ? "" : " (" + cause.ProcessName + ")"), active.Icon);
    }
    public Task ApplyManualProfileAsync(string profileId)
    {
        Start();
        var started = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.Writer.TryWrite(async () =>
        {
            try
            {
                lifetime.Token.ThrowIfCancellationRequested();
                var fade = ApplyProfileWithDuckingAsync(profileId);
                if (fade.IsFaulted) await fade;
                (pendingProfile, pendingProfileCause) = mixer.State == MixerConnectionState.Connected ? (null, null) : (profileId, (Rule?)null);
                engine.SetBaselineProfile(profileId);
                await settings.UpdateAsync(s => s with { LastProfileId = profileId }, lifetime.Token);
                started.TrySetResult(fade);
            }
            catch (Exception ex) { started.TrySetException(ex); }
        })) started.TrySetException(new ObjectDisposedException(nameof(AutomationCoordinator)));
        return WaitManualAsync(started.Task);
    }
    private async Task WaitManualAsync(Task<Task> started) => await (await started.WaitAsync(lifetime.Token)).WaitAsync(lifetime.Token);
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
            mixer.StateChanged -= MixerStateChanged;
            queue.Writer.TryComplete();
            lifetime.Cancel();
            startup.TrySetCanceled(lifetime.Token);
        }
    }
}
