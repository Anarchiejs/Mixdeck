using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Audio;

/// <summary>Envoie toutes les instances d'un processus vers une sortie via le routeur de sessions.</summary>
public sealed class ProcessAppRouter(IAppAudioRouter router) : IAppOutputRouter
{
    public bool CanRoute => router.IsSupported;

    public bool RouteProcess(string processName, string? endpointId)
    {
        if (!router.IsSupported || string.IsNullOrWhiteSpace(processName)) return false;
        var file = processName.Replace('\\', '/').Split('/')[^1];
        var bare = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        var found = false;
        var ok = true;
        foreach (var process in System.Diagnostics.Process.GetProcessesByName(bare))
        {
            using (process)
            {
                found = true;
                try { ok &= router.SetEndpoint(process.Id, endpointId); }
                catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    ok = false;
                }
            }
        }
        return found && ok;
    }
}
