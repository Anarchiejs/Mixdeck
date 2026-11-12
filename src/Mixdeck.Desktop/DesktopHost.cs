using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;
using Mixdeck.Core.Profiles;
using Mixdeck.Core.Settings;
using Mixdeck.FlStudio;
using Mixdeck.Platform.Desktop;
using Mixdeck.Platform.Desktop.Audio;
using Mixdeck.Voicemeeter;

namespace Mixdeck.Desktop;

public sealed class DesktopHost : IAsyncDisposable
{
    private readonly ILoggerFactory _loggers;
    private readonly AutomationCoordinator _automation;
    private readonly DuckingController _ducking;
    private int _disposed;

    private DesktopHost(
        DesktopOptions options,
        MixdeckPaths paths,
        ILoggerFactory loggers,
        ISettingsStore settings,
        DesktopSurfaces surfaces,
        IMixer mixer,
        IMidiPorts midi,
        IDaw daw,
        IProfileService profiles,
        ActionDispatcher actions,
        AutomationCoordinator automation,
        DuckingController ducking,
        DesktopUpdates updates)
    {
        Options = options;
        Paths = paths;
        _loggers = loggers;
        Log = loggers.CreateLogger<DesktopHost>();
        Settings = settings;
        Surfaces = surfaces;
        Mixer = mixer;
        Midi = midi;
        Daw = daw;
        Profiles = profiles;
        Actions = actions;
        _automation = automation;
        _ducking = ducking;
        Updates = updates;
        Actions.FlyoutRequested += (_, _) => ShowRequested?.Invoke();
        Surfaces.Hotkeys.Triggered += (_, args) => _ = OnHotkeyAsync(args);
    }

    public DesktopOptions Options { get; }
    public MixdeckPaths Paths { get; }
    public ILogger Log { get; }
    public ISettingsStore Settings { get; }
    public DesktopSurfaces Surfaces { get; }
    public IMixer Mixer { get; }
    public IMidiPorts Midi { get; }
    public IDaw Daw { get; }
    public IProfileService Profiles { get; }
    public ActionDispatcher Actions { get; }
    public DesktopUpdates Updates { get; }
    public event Action<string>? Status;
    public event Action? ShowRequested;
    public event Action? LanguageChanged;

    public static DesktopHost Create(DesktopOptions options, MixdeckPaths paths)
    {
        paths.EnsureCreated();
        var loggers = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new Logging.RollingFileLoggerProvider(paths.LogsDirectory));
        });
        var settings = new SettingsStore(paths, loggers.CreateLogger<SettingsStore>());
        var surfaces = DesktopSurfaces.Create(loggers.CreateLogger<DesktopSurfaces>());
        var mixer = CreateMixer(options, loggers);
        var midi = options.Demo ? new SimulatedFlStudio() : MidiPorts.CreatePlatform();
        var daw = new FlStudioDaw(midi, settings, loggers.CreateLogger<FlStudioDaw>(), TimeProvider.System);
        var profiles = new ProfileService(mixer, settings, paths, loggers.CreateLogger<ProfileService>(), TimeProvider.System, daw);
        var osd = new StatusOsd(settings);
        var ducking = new DuckingController(mixer, settings, TimeProvider.System, loggers.CreateLogger<DuckingController>());
        var automation = new AutomationCoordinator(surfaces.Processes, surfaces.Foreground, profiles, mixer, settings, osd,
            loggers.CreateLogger<AutomationCoordinator>(), ducking);
        var actions = new ActionDispatcher(mixer, profiles, settings, osd, loggers.CreateLogger<ActionDispatcher>(),
            automation, new DesktopTextProvider(), daw);
        var host = new DesktopHost(options, paths, loggers, settings, surfaces, mixer, midi, daw, profiles, actions, automation, ducking, new DesktopUpdates());
        osd.Bind(host.RaiseStatus);
        return host;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await Settings.LoadAsync(cancellationToken);
        DesktopText.Apply(Settings.Current.Language);
        Surfaces.Audio.Start();
        try { await Surfaces.Audio.RefreshAsync(cancellationToken); }
        catch (Exception exception) { Log.LogWarning(exception, "Lecture audio initiale impossible."); }
        await Mixer.StartAsync(cancellationToken);
        await Daw.StartAsync(cancellationToken);
        await Profiles.LoadAsync(cancellationToken);
        Surfaces.Hotkeys.Apply(Settings.Current.Hotkeys);
        _automation.Start();
        _ducking.Start();
        Surfaces.Processes.Start();
        Surfaces.Foreground.Start();
        SyncAutoStart();
        Log.LogInformation("Mixdeck desktop sur {Os}. Audio : {Audio}. Raccourcis : {Hotkeys}.",
            Surfaces.Capabilities.OperatingSystemName, Surfaces.Audio.IsAvailable, Surfaces.Capabilities.GlobalHotkeys);
    }

    public void RaiseStatus(string text) => Status?.Invoke(text);

    public void RequestShow() => ShowRequested?.Invoke();

    public async Task SetLanguageAsync(string? language)
    {
        await Settings.UpdateAsync(settings => settings with { Language = language });
        DesktopText.Apply(language);
        LanguageChanged?.Invoke();
        RaiseStatus(DesktopText.Get("Status_Saved"));
    }

    public async Task ToggleInputMuteAsync()
    {
        var snapshot = Surfaces.Audio.Snapshot;
        if (snapshot.DefaultInputId is not { } id)
        {
            RaiseStatus(Surfaces.Audio.UnavailableReason ?? DesktopText.Get("Status_AudioUnavailable"));
            return;
        }
        var device = snapshot.Inputs.FirstOrDefault(item => item.Id == id);
        var mute = device is null || !device.Mute;
        if (!await Surfaces.Audio.SetDeviceMuteAsync(SystemAudioFlow.Input, id, mute))
        {
            RaiseStatus(Surfaces.Audio.UnavailableReason ?? DesktopText.Get("Status_AudioUnavailable"));
            return;
        }
        RaiseStatus(DesktopText.Get(mute ? "Core_MicMuted" : "Core_MicOn"));
    }

    public void SyncAutoStart()
    {
        if (!Updates.IsInstalled) return;
        var autoStart = Surfaces.AutoStart;
        if (autoStart.IsEnabled != Settings.Current.StartWithWindows)
            autoStart.SetEnabled(Settings.Current.StartWithWindows);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try
        {
            _automation.Dispose();
            _ducking.Dispose();
            Surfaces.Dispose();
            await Mixer.DisposeAsync();
            await Daw.DisposeAsync();
        }
        catch (Exception exception) { Log.LogWarning(exception, "Arrêt incomplet."); }
        finally { _loggers.Dispose(); }
    }

    private async Task OnHotkeyAsync(HotkeyEventArgs args)
    {
        try
        {
            if (!args.IsPressed && args.Binding.Action != HotkeyAction.PushToTalk) return;
            if (Mixer.State == MixerConnectionState.Connected)
            {
                await Actions.HandleAsync(args);
                return;
            }
            switch (args.Binding.Action)
            {
                case HotkeyAction.OpenFlyout:
                    ShowRequested?.Invoke();
                    break;
                case HotkeyAction.ToggleMic:
                    await ToggleInputMuteAsync();
                    break;
                case HotkeyAction.PushToTalk:
                    await SetInputMuteAsync(!args.IsPressed);
                    break;
                case HotkeyAction.VolumeUp:
                case HotkeyAction.VolumeDown:
                    await NudgeOutputAsync(args.Binding);
                    break;
                case HotkeyAction.ToggleDawSlot:
                case HotkeyAction.ToggleDawTrackMute:
                    await Actions.HandleAsync(args);
                    break;
                default:
                    RaiseStatus(DesktopText.Get("Status_ProfileWindowsOnly"));
                    break;
            }
        }
        catch (Exception exception) { Log.LogError(exception, "Raccourci impossible."); }
    }

    private async Task SetInputMuteAsync(bool mute)
    {
        var id = Surfaces.Audio.Snapshot.DefaultInputId;
        if (id is null || !await Surfaces.Audio.SetDeviceMuteAsync(SystemAudioFlow.Input, id, mute))
        {
            RaiseStatus(Surfaces.Audio.UnavailableReason ?? DesktopText.Get("Status_AudioUnavailable"));
            return;
        }
        RaiseStatus(DesktopText.Get(mute ? "Core_MicMuted" : "Core_MicOn"));
    }

    private async Task NudgeOutputAsync(HotkeyBinding binding)
    {
        var snapshot = Surfaces.Audio.Snapshot;
        var id = snapshot.DefaultOutputId;
        var device = snapshot.Outputs.FirstOrDefault(item => item.Id == id);
        if (id is null || device is null)
        {
            RaiseStatus(Surfaces.Audio.UnavailableReason ?? DesktopText.Get("Status_AudioUnavailable"));
            return;
        }
        var step = float.IsFinite(binding.StepDb) ? Math.Abs(binding.StepDb) : 2f;
        if (step == 0) step = 2f;
        var factor = MathF.Pow(10f, step / 20f);
        var next = binding.Action == HotkeyAction.VolumeUp ? device.Volume * factor : device.Volume / factor;
        if (device.Volume <= 0.001f && binding.Action == HotkeyAction.VolumeUp) next = 0.05f;
        next = Math.Clamp(next, 0, 1);
        if (!await Surfaces.Audio.SetDeviceVolumeAsync(SystemAudioFlow.Output, id, next))
            RaiseStatus(Surfaces.Audio.UnavailableReason ?? DesktopText.Get("Status_AudioUnavailable"));
    }

    private static IMixer CreateMixer(DesktopOptions options, ILoggerFactory loggers)
    {
        if (options.Demo) return VoicemeeterMixer.CreateSimulated(loggers, options.DemoLayout);
        if (OperatingSystem.IsWindows()) return VoicemeeterMixer.CreateDefault(loggers);
        return new UnavailableMixer();
    }

    private sealed class StatusOsd(ISettingsStore settings) : IOsdService
    {
        private Action<string>? _show;
        public void Bind(Action<string> show) => _show = show;
        public void Show(string text, string glyph, bool isWarning = false)
        {
            if (settings.Current.ShowOsd) _show?.Invoke(text);
        }
    }
}
