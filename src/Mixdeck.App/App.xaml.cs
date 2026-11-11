using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mixdeck.App.Services;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Power;
using Mixdeck.Platform.SingleInstance;
using Mixdeck.Platform.Taskbar;
using Serilog;

namespace Mixdeck.App;

public partial class App : Application
{
    private readonly AppOptions _options;
    private readonly SingleInstanceGuard _instance;
    private IHost? _host;
    private Microsoft.Extensions.Logging.ILogger? _logger;

    public App(AppOptions options, SingleInstanceGuard instance)
    {
        _options = options;
        _instance = instance;
        InitializeComponent();

        // Mixdeck vit dans la zone de notification : fermer la console (souvent la seule fenêtre) ne doit pas
        // arrêter l'appli. L'arrêt passe uniquement par « Quitter » (Application.Exit).
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += OnUnhandledException;
    }

    public static IServiceProvider Services =>
        ((App)Current)._host?.Services ?? throw new InvalidOperationException("Services pas encore prêts");

    public static T GetService<T>()
        where T : notnull => Services.GetRequiredService<T>();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var paths = MixdeckPaths.Default;

        try
        {
            paths.EnsureCreated();
            ConfigureLogging(paths);
            var isFirstRun = !File.Exists(paths.SettingsFile);
            using var bootstrapLoggers = LoggerFactory.Create(b => b.AddSerilog());
            var settingsStore = new SettingsStore(paths, bootstrapLoggers.CreateLogger<SettingsStore>());
            var settings = await settingsStore.LoadAsync();
            Localization.Strings.ApplyLanguage(settings.Language);
            if (isFirstRun)
            {
                // Les lancements suivants ne sont plus des « premiers lancements » (la console ne s'ouvre plus d'elle-même).
                await settingsStore.SaveAsync(settings);
            }

            // La couleur d'accent doit être posée avant la création de la première fenêtre.
            ThemeService.ApplyAccent(this, settings.AccentColor);

            var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
            builder.Services.AddSerilog(dispose: true);
            builder.Services.AddMixdeck(_options, paths, settingsStore, DispatcherQueue.GetForCurrentThread());
            _host = builder.Build();
            _logger = GetService<ILogger<App>>();
            _logger.LogInformation("Démarrage de Mixdeck {Version} (démo : {Demo})", GetService<IUpdateService>().CurrentVersion, _options.Demo);
            if (_options.AfterCrash)
            {
                _logger.LogWarning("Redémarré après un plantage (voir logs\\crash-*.txt)");
            }

            await StartServicesAsync(settings);

            if (_options.Gallery)
            {
                new Controls.Gallery.ControlsGalleryWindow().Activate();
            }
            else if (_options.Page is { } page && Enum.TryParse<ShellPage>(page, ignoreCase: true, out var shellPage))
            {
                GetService<IShellService>().ShowMainWindow(shellPage);
            }
            else if (_options.Flyout)
            {
                GetService<IShellService>().ToggleFlyout();
            }
            else if (isFirstRun || !_options.Minimized)
            {
                // Premier lancement : on montre la console pour qu'on découvre l'appli ; ensuite, elle vit dans la barre des tâches.
                GetService<IShellService>().ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Échec du démarrage");
            await Log.CloseAndFlushAsync();
            Exit();
        }
    }

    private async Task StartServicesAsync(AppSettings settings)
    {
        var theme = GetService<ThemeService>();
        theme.SetMode(settings.Theme);

        var mixer = GetService<IMixer>();
        await mixer.StartAsync();
        if (settings.AutoLaunchVoicemeeter && !_options.Demo
            && await mixer.LaunchVoicemeeterAsync() && mixer is Mixdeck.Voicemeeter.VoicemeeterMixer remote)
        {
            // Sans attendre : la connexion peut prendre quelques secondes, l'icône doit apparaître tout de suite.
            _ = remote.HideWindowWhenConnectedAsync(settings.HideVoicemeeterWindow);
        }

        // FL Studio : recherche des ports loopMIDI et reconnexion en fond, sans retarder le démarrage.
        await GetService<IDaw>().StartAsync();
        // L5 : la synchronisation écoute avant que Discord puisse se connecter.
        GetService<DiscordMuteSync>().Start();
        await GetService<IDiscordVoice>().StartAsync();

        await GetService<IProfileService>().LoadAsync();

        GetService<TrayIconService>().Create();
        _ = KeepTrayIconVisibleAsync(settings);
        GetService<DuckingController>().Start();
        GetService<AutomationCoordinator>().Start();
        _ = GetService<AutomationCoordinator>().ApplyStartupProfileAsync();

        var actions = GetService<ActionDispatcher>();
        var shell = GetService<IShellService>();
        var ui = GetService<IUiDispatcher>();
        actions.FlyoutRequested += (_, _) => ui.Post(shell.ToggleFlyout);

        var hotkeys = GetService<IHotkeyService>();
        hotkeys.Triggered += async (_, e) =>
        {
            try
            {
                await actions.HandleAsync(e);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Échec du raccourci {Action}", e.Binding.Action);
            }
        };
        ApplyHotkeys(hotkeys, settings);

        var autoStart = GetService<IAutoStart>();
        SyncAutoStart(autoStart, settings);

        var store = GetService<ISettingsStore>();
        var showTrayIcon = settings.AlwaysShowTrayIcon;
        store.Changed += (_, updated) => ui.Post(() =>
        {
            if (updated.AlwaysShowTrayIcon != showTrayIcon)
            {
                // Choix explicite dans Réglages : on l'applique dans les deux sens.
                showTrayIcon = updated.AlwaysShowTrayIcon;
                GetService<TrayIconPromoter>().SetVisible(showTrayIcon);
            }

            theme.SetMode(updated.Theme);
            ApplyHotkeys(hotkeys, updated);
            SyncAutoStart(autoStart, updated);
        });

        var system = GetService<SystemEventsWatcher>();
        system.DisplayChanged += (_, _) => ui.Post(shell.HideFlyout);
        system.Resumed += (_, _) => _logger?.LogInformation("Reprise après mise en veille");

        _instance.ActivationRequested += (_, _) => ui.Post(() => shell.ShowMainWindow());

        GetService<ShellService>().ExitRequested += (_, _) => Shutdown();
        QueueUpdateCheck(settings);
    }

    /// <summary>
    /// Installed builds contact GitHub Releases only when the user left "Check for updates" on.
    /// The About page can still check by hand when the switch is off. Development builds are not installed.
    /// </summary>
    private void QueueUpdateCheck(AppSettings settings)
    {
        if (!settings.CheckForUpdates) return;
        var updates = GetService<IUpdateService>();
        if (!updates.IsInstalled) return;
        _ = CheckForUpdatesQuietlyAsync(updates);
    }

    private async Task CheckForUpdatesQuietlyAsync(IUpdateService updates)
    {
        try
        {
            var result = await updates.CheckAsync().ConfigureAwait(false);
            if (result.IsAvailable)
                _logger?.LogInformation("Mise à jour disponible : {Version}", result.Version);
            else if (result.Error is not null)
                _logger?.LogInformation("Recherche automatique de mise à jour non aboutie");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Recherche automatique de mise à jour non aboutie");
        }
    }

    /// <summary>
    /// Sort l'icône de la zone cachée (flèche ^) une seule fois : si l'utilisateur l'y range ensuite, on respecte son choix.
    /// Windows ne crée la fiche de l'icône qu'après sa première apparition, d'où l'attente.
    /// </summary>
    private async Task KeepTrayIconVisibleAsync(AppSettings settings)
    {
        if (!settings.AlwaysShowTrayIcon || settings.TrayIconPromoted)
        {
            return;
        }

        try
        {
            var result = await GetService<TrayIconPromoter>().SetVisibleWhenReadyAsync(true, TimeSpan.FromSeconds(20));
            if (result is TrayPromotionResult.Changed or TrayPromotionResult.Unchanged)
            {
                await GetService<ISettingsStore>().UpdateAsync(s => s with { TrayIconPromoted = true });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Impossible de rendre l'icône visible");
        }
    }

    private void ApplyHotkeys(IHotkeyService hotkeys, AppSettings settings)
    {
        hotkeys.Apply(settings.Hotkeys);
        foreach (var failed in hotkeys.Failed)
        {
            _logger?.LogWarning("Raccourci déjà utilisé par une autre application : {Action}", failed.Action);
        }
    }

    private void SyncAutoStart(IAutoStart autoStart, AppSettings settings)
    {
        // En développement (exe dans bin\), on n'inscrit rien au démarrage de Windows.
        var installed = GetService<IUpdateService>().IsInstalled;
        if (installed && autoStart.IsEnabled != settings.StartWithWindows)
        {
            autoStart.SetEnabled(settings.StartWithWindows);
        }
    }

    private void Shutdown()
    {
        try
        {
            GetService<TrayIconService>().Dispose();
            GetService<AutomationCoordinator>().Dispose();
            GetService<DuckingController>().Dispose();
            GetService<DiscordMuteSync>().DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            GetService<IDiscordVoice>().DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            GetService<IHotkeyService>().Dispose();
            GetService<IMixer>().DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            GetService<IDaw>().DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(1));
            // Les services Discord sont asynchrones : Dispose() de DI les refuserait.
            if (_host is IAsyncDisposable asyncHost)
                asyncHost.DisposeAsync().AsTask().GetAwaiter().GetResult();
            else
                _host?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Erreur à l'arrêt");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogging(MixdeckPaths paths)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
#if DEBUG
            .MinimumLevel.Debug()
#endif
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(paths.LogsDirectory, "mixdeck-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Exception non gérée dans l'interface");
        // On garde l'appli en vie : une erreur d'affichage ne doit pas couper le contrôle du son.
        e.Handled = true;
    }
}
