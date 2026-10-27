using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Automation;

/// <summary>Atténuation linéaire en dB, pilotée par les niveaux et une horloge monotone.</summary>
public sealed class DuckingController(IMixer mixer, ISettingsStore settings, TimeProvider time,
    ILogger<DuckingController> logger) : IDisposable
{
    private readonly object gate = new();
    private readonly List<float> pendingWrites = [];
    private IDisposable? levels;
    private DuckingSettings config = new();
    private MixerLayout? configuredLayout;
    private bool started, disposed, reconnect, ducked;
    private int suspensions;
    private int? target;
    private float baseline, observed, current, rampFrom, rampTo;
    private long rampStart;
    private double rampMilliseconds;
    private long? lastFrame, belowSince;
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(50);

    public void Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started) return;
            started = true;
            settings.Changed += SettingsChanged;
            mixer.SnapshotChanged += SnapshotChanged;
            mixer.StateChanged += StateChanged;
            mixer.LevelsUpdated += LevelsUpdated;
            Guard(Configure);
        }
    }

    private bool Enabled => config.Enabled && config.SourceStrip >= 0 && config.SourceStrip < mixer.Layout.StripCount &&
        config.TargetStrip >= 0 && config.TargetStrip < mixer.Layout.StripCount && config.SourceStrip != config.TargetStrip &&
        float.IsFinite(config.ThresholdDb) && float.IsFinite(config.AmountDb) && config.AmountDb <= 0;

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { logger.LogError(ex, "Échec de l'atténuation automatique"); }
    }

    private void Configure()
    {
        var layout = mixer.Layout;
        if (config == settings.Current.Ducking && configuredLayout == layout) return;
        if (configuredLayout is not null && configuredLayout != layout)
        {
            // Les index peuvent désigner d'autres entrées après un changement de version.
            // Ne jamais restaurer la base de l'ancien moteur sur le nouveau.
            target = null;
            pendingWrites.Clear();
            belowSince = null;
            reconnect = false;
        }
        else Restore();
        configuredLayout = layout;
        config = settings.Current.Ducking;
        if (Enabled) levels ??= mixer.RequestLevels();
        else { levels?.Dispose(); levels = null; }
        lastFrame = null;
    }

    private void SettingsChanged(object? sender, AppSettings value)
    { lock (gate) { if (!disposed) Guard(Configure); } }

    private void StateChanged(object? sender, MixerConnectionState state)
    {
        lock (gate)
        {
            if (disposed) return;
            if (state != MixerConnectionState.Connected) reconnect = true;
            else Guard(Configure);
            lastFrame = null;
        }
    }

    private void SnapshotChanged(object? sender, MixerSnapshot snapshot)
    {
        lock (gate)
        {
            if (disposed || mixer.State != MixerConnectionState.Connected) return;
            Guard(() =>
            {
                Configure();
                if (reconnect) { Restore(snapshot); reconnect = false; }
                else Observe(snapshot);
            });
        }
    }

    // Les accusés de réception des commandes mises en file ne constituent pas une nouvelle base.
    // Toute autre valeur (fader, profil, Voicemeeter) remplace immédiatement la base utilisateur.
    private void Observe(MixerSnapshot snapshot)
    {
        if (target is not { } index || snapshot.Strips.FirstOrDefault(s => s.Index == index) is not { } strip ||
            !float.IsFinite(strip.Gain) || Math.Abs(strip.Gain - observed) < 0.001f) return;
        observed = strip.Gain;
        var acknowledgement = pendingWrites.FindIndex(value => Math.Abs(value - observed) < 0.01f);
        if (acknowledgement >= 0) { pendingWrites.RemoveRange(0, acknowledgement + 1); return; }
        pendingWrites.Clear();
        baseline = current = mixer.Layout.ClampGain(observed);
        BeginRamp(ducked, time.GetTimestamp());
    }

    private void LevelsUpdated(object? sender, LevelFrame frame)
    {
        lock (gate)
        {
            if (disposed || mixer.State != MixerConnectionState.Connected) return;
            Guard(() => Tick(frame));
        }
    }

    private void Tick(LevelFrame frame)
    {
        Configure();
        if (frame.Layout != mixer.Layout) return;
        if (reconnect) { Restore(); reconnect = false; }
        if (!Enabled || suspensions > 0 || frame.Strips.Count <= config.SourceStrip) return;
        var now = time.GetTimestamp();
        if (lastFrame is { } previous && time.GetElapsedTime(previous, now) < FrameInterval) return;
        lastFrame = now;
        if (mixer.Snapshot.Strips.FirstOrDefault(s => s.Index == config.TargetStrip) is not { } strip || !float.IsFinite(strip.Gain)) return;
        if (target is null)
        {
            target = config.TargetStrip;
            baseline = observed = current = strip.Gain;
            ducked = false;
            belowSince = null;
            pendingWrites.Clear();
            BeginRamp(false, now);
        }
        Observe(mixer.Snapshot);
        var peak = frame.Strips[config.SourceStrip].Max;
        var db = float.IsFinite(peak) && peak > 0 ? 20 * MathF.Log10(peak) : float.NegativeInfinity;
        if (db > config.ThresholdDb)
        {
            belowSince = null;
            if (!ducked) BeginRamp(true, now);
        }
        else if (ducked)
        {
            belowSince ??= now;
            if (time.GetElapsedTime(belowSince.Value, now).TotalMilliseconds >= Math.Max(0, config.HoldMilliseconds))
                BeginRamp(false, now);
        }
        var fraction = rampMilliseconds <= 0 ? 1 : Math.Clamp(time.GetElapsedTime(rampStart, now).TotalMilliseconds / rampMilliseconds, 0, 1);
        Write(mixer.Layout.ClampGain(rampFrom + (rampTo - rampFrom) * (float)fraction));
    }

    private void BeginRamp(bool attenuate, long now)
    {
        ducked = attenuate;
        rampFrom = current;
        rampTo = mixer.Layout.ClampGain(baseline + (attenuate ? config.AmountDb : 0));
        rampStart = now;
        rampMilliseconds = Math.Max(0, attenuate ? config.AttackMilliseconds : config.ReleaseMilliseconds);
    }

    private void Write(float gain)
    {
        if (target is not { } index || index < 0 || index >= mixer.Layout.StripCount || Math.Abs(gain - current) < 0.1f) return;
        // Enregistrer avant l'appel : certains moteurs publient leur état de façon synchrone.
        current = gain;
        pendingWrites.Add(gain);
        if (pendingWrites.Count > 128) pendingWrites.RemoveAt(0);
        mixer.SetFloat(VmParam.StripGain(index), gain);
    }

    private void Restore(MixerSnapshot? snapshot = null)
    {
        if (target is null) return;
        if (mixer.State != MixerConnectionState.Connected) { reconnect = true; return; }
        Observe(snapshot ?? mixer.Snapshot);
        Write(baseline);
        target = null;
        pendingWrites.Clear();
        belowSince = null;
        lastFrame = null;
    }

    /// <summary>Le coordinateur garde ce jeton jusqu'à la fin réelle du fondu, même s'il est remplacé.</summary>
    public IDisposable SuspendForProfile()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (suspensions++ == 0) Guard(() => Restore());
            return new Suspension(this);
        }
    }

    private sealed class Suspension(DuckingController owner) : IDisposable
    {
        private DuckingController? controller = owner;
        public void Dispose()
        {
            var value = Interlocked.Exchange(ref controller, null);
            if (value is null) return;
            lock (value.gate) { value.suspensions--; value.lastFrame = null; }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            settings.Changed -= SettingsChanged;
            mixer.SnapshotChanged -= SnapshotChanged;
            mixer.StateChanged -= StateChanged;
            mixer.LevelsUpdated -= LevelsUpdated;
            Guard(() => Restore());
            levels?.Dispose();
            levels = null;
        }
    }
}
