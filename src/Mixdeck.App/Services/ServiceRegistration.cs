using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Mixdeck.App.ViewModels.AppVolume;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Flyout;
using Mixdeck.App.Views.Osd;
using Mixdeck.App.Views.Shell;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Profiles;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Audio;
using Mixdeck.Platform.Foreground;
using Mixdeck.Platform.Hotkeys;
using Mixdeck.Platform.Power;
using Mixdeck.Platform.Processes;
using Mixdeck.Platform.Startup;
using Mixdeck.Platform.Taskbar;
using Mixdeck.Voicemeeter;

namespace Mixdeck.App.Services;

/// <summary>Toutes les dépendances de l'appli : un seul endroit pour voir qui fournit quoi.</summary>
internal static class ServiceRegistration
{
    public static IServiceCollection AddMixdeck(
        this IServiceCollection services,
        AppOptions options,
        MixdeckPaths paths,
        ISettingsStore settings,
        DispatcherQueue dispatcherQueue)
    {
        services.AddSingleton(options);
        services.AddSingleton(paths);
        services.AddSingleton(settings);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITextProvider, Mixdeck.App.Localization.AppTextProvider>();
        services.AddSingleton<IUiDispatcher>(new UiDispatcher(dispatcherQueue));

        // Moteur audio : le vrai Voicemeeter, ou une simulation en mode démo.
        services.AddSingleton<IMixer>(sp =>
        {
            var loggers = sp.GetRequiredService<ILoggerFactory>();
            if (!options.Demo) return VoicemeeterMixer.CreateDefault(loggers);
            var layout = options.DemoLayout;
            var api = new SimulatedVoicemeeterApi(layout: layout);
            string[] labels = [Localization.Strings.Get("CS_104"), Localization.Strings.Get("CS_108"), Localization.Strings.Get("CS_107"),
                Localization.Strings.Get("CS_010"), Localization.Strings.Get("CS_109")];
            for (var index = 0; index < layout.StripCount; index++)
                api.SimulateExternalChange(Core.Model.VmParam.StripLabel(index), index < labels.Length ? labels[index]
                    : layout.IsVirtualStrip(index) ? layout.DefaultStripLabels[index] : Localization.Strings.Format("Core_InputName", index + 1));
            api.SimulateExternalChange(Core.Model.VmParam.BusDeviceName(0), Localization.Strings.Get("CS_105") + " (Realtek Audio)");
            return new VoicemeeterMixer(api, loggers.CreateLogger<VoicemeeterMixer>(), TimeProvider.System);
        });

        // FL Studio par loopMIDI et le script fl/Mixdeck/device_Mixdeck.py, ou un FL simulé en mode démo.
        services.AddSingleton<Mixdeck.FlStudio.IMidiPorts>(_ => options.Demo
            ? new Mixdeck.FlStudio.SimulatedFlStudio()
            : new Mixdeck.FlStudio.WinMidiPorts());
        services.AddSingleton<IDaw, Mixdeck.FlStudio.FlStudioDaw>();

        // Cœur
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<ActionDispatcher>();
        services.AddSingleton<DuckingController>();
        services.AddSingleton<AutomationCoordinator>();

        // Plateforme Win32
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IProcessWatcher, ProcessWatcher>();
        services.AddSingleton<IForegroundWatcher, ForegroundWatcher>();
        services.AddSingleton<IAutoStart, AutoStart>();
        services.AddSingleton<ITaskbarLocator, TaskbarLocator>();
        services.AddSingleton<TrayIconPromoter>();

        // Volume par application : une instance par ouverture du panneau, libérée à sa fermeture.
        services.AddSingleton<Func<IAppAudioSessions>>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<AppAudioSessions>>();
            return () => new AppAudioSessions(logger);
        });
        services.AddSingleton<IAppAudioRouter, Mixdeck.Platform.Audio.AudioPolicy.AppAudioRouter>();
        services.AddSingleton<SystemEventsWatcher>();

        // Interface
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IOsdService, OsdService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ShellService>();
        services.AddSingleton<IShellService>(sp => sp.GetRequiredService<ShellService>());
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<MixerViewModel>();
        services.AddSingleton<FlQuickPickViewModel>();
        // L6 Périphérique Windows par défaut : enregistrements ici.
        services.AddSingleton<IWindowsDefaultDevices>(_ => options.Demo
            ? new SimulatedWindowsDefaultDevices()
            : new WindowsDefaultDevices());
        services.AddSingleton<WindowsDefaultDevicesViewModel>();
        services.AddTransient<AppVolumeListViewModel>();
        services.AddSingleton<FlyoutWindow>();
        // La console est recréée à chaque ouverture et libérée à la fermeture (mémoire au repos).
        services.AddTransient<MainWindow>();
        services.AddSingleton<OsdWindow>();

        // Pages de la console : une instance par navigation.
        services.AddTransient<MixerPageViewModel>();
        services.AddTransient<RoutingPageViewModel>();
        services.AddTransient<DevicesPageViewModel>();
        services.AddTransient<FlStudioPageViewModel>();
        services.AddTransient<ProfilesPageViewModel>();
        services.AddTransient<RulesPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        return services;
    }
}
