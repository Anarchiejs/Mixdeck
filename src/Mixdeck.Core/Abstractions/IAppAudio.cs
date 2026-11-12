namespace Mixdeck.Core.Abstractions;

/// <summary>Exact fallback display names produced when Windows gives a session no usable name. The UI localizes these values.</summary>
public static class AppAudioSessionNames
{
    public const string SystemSoundsFallback = "System sounds";
    public const string UnknownApplicationFallback = "Application";
}

/// <summary>Une application qui joue du son (session audio Windows), comme dans le mélangeur de volume de Windows.</summary>
/// <param name="Id">Identifiant stable de la session (IAudioSessionControl2.GetSessionInstanceIdentifier).</param>
/// <param name="ProcessName">Nom de l'exécutable avec extension, ex. "chrome.exe" ; vide pour les sons système.</param>
/// <param name="DisplayName">Nom à afficher (nom de la session, sinon description du fichier, sinon nom du processus).</param>
/// <param name="IconPath">Exécutable ou fichier d'icône dont tirer l'icône ; null si inconnu.</param>
/// <param name="Volume">Volume de la session, 0 à 1 (ISimpleAudioVolume).</param>
public sealed record AppAudioSession(
    string Id,
    int ProcessId,
    string ProcessName,
    string DisplayName,
    string? IconPath,
    float Volume,
    bool Mute,
    bool IsSystemSounds);

/// <summary>Volume par application sur la sortie par défaut de Windows (IAudioSessionManager2, ISimpleAudioVolume).</summary>
public interface IAppAudioSessions : IDisposable
{
    /// <summary>Sessions actives (les sessions expirées sont retirées).</summary>
    IReadOnlyList<AppAudioSession> Sessions { get; }

    /// <summary>Levé sur un thread quelconque quand une session apparaît, disparaît ou change de volume.</summary>
    event EventHandler? SessionsChanged;

    /// <summary>Commence à suivre les sessions et la sortie par défaut. Sans effet si déjà démarré.</summary>
    void Start();

    void SetVolume(string sessionId, float volume);

    void SetMute(string sessionId, bool mute);
}

/// <summary>Sortie audio de Windows (point de terminaison de rendu actif).</summary>
public sealed record AudioEndpoint(string Id, string Name);

/// <summary>
/// Sortie choisie par application (« un jeu → entrée Voicemeeter AUX »), comme Paramètres &gt; Son &gt; Mélangeur de volume.
/// Repose sur une API Windows non documentée : <see cref="IsSupported"/> vaut false si elle est absente ou a changé.
/// </summary>
public interface IAppAudioRouter
{
    bool IsSupported { get; }

    IReadOnlyList<AudioEndpoint> GetRenderEndpoints();

    /// <summary>Sortie imposée à ce processus, null = sortie par défaut de Windows.</summary>
    string? GetEndpoint(int processId);

    /// <summary>Impose une sortie (null = revenir à la sortie par défaut). false si Windows a refusé.</summary>
    bool SetEndpoint(int processId, string? endpointId);
}
