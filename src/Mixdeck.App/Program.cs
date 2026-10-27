using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mixdeck.App.Services;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.SingleInstance;
using Mixdeck.Platform.Startup;
using Velopack;

namespace Mixdeck.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Doit passer en premier : Velopack y traite les étapes d'installation et de mise à jour.
        VelopackApp.Build()
            // Désinstallation : retirer l'entrée de démarrage avec Windows (elle pointerait vers un exe disparu).
            .OnBeforeUninstallFastCallback(_ => new AutoStart().SetEnabled(false))
            .Run();

        CrashReporter.Install(MixdeckPaths.Default);
#if DEBUG
        if (Environment.GetEnvironmentVariable("MIXDECK_TEST_CRASH") == "1")
        {
            // Essai du journal des plantages : exception hors du thread de l'interface, donc fatale.
            new Thread(() =>
            {
                Thread.Sleep(3000);
                throw new InvalidOperationException("Plantage de test (MIXDECK_TEST_CRASH)");
            }) { IsBackground = true }.Start();
        }
#endif

        var options = AppOptions.Parse(args);

        // Une seule instance : une deuxième ouverture réveille la première (console au premier plan) et s'arrête.
        var instance = new SingleInstanceGuard();
        // Relance après plantage : l'instance qui meurt tient encore le verrou quelques instants.
        for (var attempt = 0; options.AfterCrash && !instance.IsFirstInstance && attempt < 20; attempt++)
        {
            instance.Dispose();
            Thread.Sleep(500);
            instance = new SingleInstanceGuard();
        }

        using var guard = instance;
        if (!instance.IsFirstInstance)
        {
            instance.SignalFirstInstance();
            return 0;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(options, instance);
        });
        return 0;
    }
}
