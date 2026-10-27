using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mixdeck.Platform.SingleInstance;
using Velopack;

namespace Mixdeck.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Doit passer en premier : Velopack y traite les étapes d'installation et de mise à jour.
        VelopackApp.Build().Run();

        var options = AppOptions.Parse(args);

        // Une seule instance : une deuxième ouverture réveille la première (console au premier plan) et s'arrête.
        using var instance = new SingleInstanceGuard();
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
