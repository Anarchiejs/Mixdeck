// API privée, IID/provenance/versions dans AudioPolicyIds.cs et AudioPolicySession.cs.
// La disponibilité de l'ABI ne prouve pas la migration d'un flux audio existant : essai utilisateur requis.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Audio.AudioPolicy;

/// <summary>
/// Préférences de sortie par processus. Contrat synchrone : à appeler depuis un thread de travail
/// (Task.Run côté interface), car Windows peut effectuer des appels RPC. Aucun objet COM n'est partagé entre threads.
/// </summary>
public sealed class AppAudioRouter : IAppAudioRouter
{
    private readonly object _gate = new();
    private readonly Func<IAudioPolicySession> _open;
    private readonly Func<IReadOnlyList<AudioEndpoint>> _endpoints;
    private readonly ILogger<AppAudioRouter> _logger;
    private bool _unavailable;

    public AppAudioRouter(ILogger<AppAudioRouter>? logger = null, Action<string>? trace = null)
    {
        _logger = logger ?? NullLogger<AppAudioRouter>.Instance;
        void Report(string message)
        {
            _logger.LogDebug("{DiagnosticAudio}", message);
            trace?.Invoke(message);
        }
        _open = () => new AudioPolicySession(Report);
        _endpoints = () => RenderEndpoints.Read(Report);
    }

    internal AppAudioRouter(Func<IAudioPolicySession> open, Func<IReadOnlyList<AudioEndpoint>>? endpoints = null)
    {
        _open = open;
        _endpoints = endpoints ?? (() => []);
        _logger = NullLogger<AppAudioRouter>.Instance;
    }

    public bool IsSupported => Execute(() => { using var session = _open(); return true; }, false);

    public IReadOnlyList<AudioEndpoint> GetRenderEndpoints()
    {
        try { return _endpoints(); }
        catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
        {
            _logger.LogWarning(ex, "Impossible de lire les sorties audio Windows (HRESULT 0x{HResult:X8})", ex.HResult);
            return [];
        }
    }

    public string? GetEndpoint(int processId)
    {
        if (processId <= 0) return null;
        return Execute(() =>
        {
            using var session = _open();
            return Unpack(session.Get(processId, AudioPolicyRole.Multimedia));
        }, (string?)null);
    }

    /// <summary>Lecture explicite pour les diagnostics : distingue un défaut Windows d'un échec et lit les deux rôles.</summary>
    public bool TryGetEndpoints(int processId, out string? multimedia, out string? console)
    {
        multimedia = console = null;
        if (processId <= 0) return false;
        var result = Execute(() =>
        {
            using var session = _open();
            return (Success: true, Multimedia: Unpack(session.Get(processId, AudioPolicyRole.Multimedia)),
                Console: Unpack(session.Get(processId, AudioPolicyRole.Console)));
        }, (Success: false, Multimedia: (string?)null, Console: (string?)null));
        multimedia = result.Multimedia;
        console = result.Console;
        return result.Success;
    }

    public bool SetEndpoint(int processId, string? endpointId)
    {
        if (processId <= 0 || (endpointId is not null && !AudioPolicyIds.IsEndpointId(endpointId))) return false;
        return Execute(() =>
        {
            using var session = _open();
            string? requested = AudioPolicyIds.Pack(endpointId);
            string? multimedia = session.Get(processId, AudioPolicyRole.Multimedia);
            string? console = session.Get(processId, AudioPolicyRole.Console);
            Unpack(multimedia);
            Unpack(console); // Refuser un format inconnu AVANT la moindre écriture.
            try
            {
                session.Set(processId, AudioPolicyRole.Multimedia, requested);
                session.Set(processId, AudioPolicyRole.Console, requested);
                Verify(session, processId, AudioPolicyRole.Multimedia, requested);
                Verify(session, processId, AudioPolicyRole.Console, requested);
                return true;
            }
            catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
            {
                // Restaurer chaque rôle même si le premier retour échoue. Ne jamais appeler ClearAll.
                Restore(session, processId, AudioPolicyRole.Multimedia, multimedia);
                Restore(session, processId, AudioPolicyRole.Console, console);
                throw;
            }
        }, false);
    }

    private string? Unpack(string? value)
    {
        if (AudioPolicyIds.TryUnpack(value, out string? id)) return id;
        _unavailable = true;
        throw new NotSupportedException("Windows a renvoyé un format de sortie inconnu ; routage désactivé.");
    }

    private static void Verify(IAudioPolicySession session, int processId, AudioPolicyRole role, string? expected)
    {
        string? actual = session.Get(processId, role);
        if (!string.Equals(actual ?? "", expected ?? "", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La relecture du rôle {role} ne correspond pas à la sortie demandée.");
    }

    private void Restore(IAudioPolicySession session, int processId, AudioPolicyRole role, string? value)
    {
        try
        {
            session.Set(processId, role, value);
            Verify(session, processId, role, value);
        }
        catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
        {
            _logger.LogError(ex, "Restauration audio refusée pour le processus {Pid}, rôle {Role}, valeur {Value}", processId, role, value ?? "<default>");
        }
    }

    private T Execute<T>(Func<T> action, T fallback)
    {
        lock (_gate)
        {
            if (_unavailable) return fallback;
            try { return action(); }
            catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
            {
                if (PrivateApiNative.IsUnavailable(ex)) _unavailable = true;
                _logger.LogWarning(ex, "Routage audio Windows indisponible ou refusé (HRESULT 0x{HResult:X8})", ex.HResult);
                return fallback;
            }
        }
    }
}
