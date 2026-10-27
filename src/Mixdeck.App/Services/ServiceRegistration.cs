using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Flyout;
using Mixdeck.App.Views.Osd;
using Mixdeck.App.Views.Shell;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Profiles;
using Mixdeck.Core.Settings;
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
        services.AddSingleton<IUiDispatcher>(new UiDispatcher(dispatcherQueue));

        // Moteur audio : le vrai Voicemeeter, ou une simulation en mode démo.
        services.AddSingleton<IMixer>(sp =>
        {
            var loggers = sp.GetRequiredService<ILoggerFactory>();
            return options.Demo ? VoicemeeterMixer.CreateSimulated(loggers) : VoicemeeterMixer.CreateDefault(loggers);
        });

        // Cœur
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<ActionDispatcher>();
        services.AddSingleton<AutomationCoordinator>();

        // Plateforme Win32
        services.AddSingleton<IHotkeyService, HotkeyService>();
        services.AddSingleton<IProcessWatcher, ProcessWatcher>();
        services.AddSingleton<IForegroundWatcher, ForegroundWatcher>();
        services.AddSingleton<IAutoStart, AutoStart>();
        services.AddSingleton<ITaskbarLocator, TaskbarLocator>();
        services.AddSingleton<SystemEventsWatcher>();

        // Interface
        services.AddSingleton<ThemeService>();
        services.AddSingleton<IOsdService, OsdService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<ShellService>();
        services.AddSingleton<IShellService>(sp => sp.GetRequiredService<ShellService>());
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<MixerViewModel>();
        services.AddSingleton<FlyoutWindow>();
        // La console est recréée à chaque ouverture et libérée à la fermeture (mémoire au repos).
        services.AddTransient<MainWindow>();
        services.AddSingleton<OsdWindow>();

        // Pages de la console : une instance par navigation.
        services.AddTransient<MixerPageViewModel>();
        services.AddTransient<RoutingPageViewModel>();
        services.AddTransient<DevicesPageViewModel>();
        services.AddTransient<ProfilesPageViewModel>();
        services.AddTransient<RulesPageViewModel>();
        services.AddTransient<SettingsPageViewModel>();
        services.AddTransient<AboutPageViewModel>();
        return services;
    }
}
