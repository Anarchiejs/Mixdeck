using Serilog;

namespace Mixdeck.App.Services;

/// <summary>Lance une action d'interface sans laisser une exception devenir non observée.</summary>
internal static class UiAsync
{
    public static async void Run(Func<Task> work)
    {
        try { await work(); }
        catch (Exception exception) { Log.Warning(exception, "Action d'interface interrompue"); }
    }
}
