using Mixdeck.Core.Model;

namespace Mixdeck.Core.Abstractions;

/// <summary>Profils : enregistrement JSON, application avec fondu, import / export.</summary>
public interface IProfileService
{
    IReadOnlyList<Profile> Profiles { get; }

    /// <summary>Dernier profil appliqué (null au démarrage tant qu'aucun n'a été appliqué).</summary>
    Profile? Active { get; }

    event EventHandler? ProfilesChanged;

    event EventHandler<Profile?>? ActiveChanged;

    /// <summary>Charge profiles/*.json ; un fichier illisible est ignoré et journalisé, jamais fatal.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Crée et enregistre un profil à partir de l'état actuel du mixeur.</summary>
    Task<Profile> CaptureCurrentAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Crée ou remplace (même Id).</summary>
    Task SaveAsync(Profile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);

    Task<Profile> DuplicateAsync(string profileId, string newName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applique un profil. Les gains glissent linéairement en dB pendant <paramref name="fade"/> (défaut : réglage
    /// FadeMilliseconds) ; routage appliqué au début ; un muet qui s'active est appliqué à la fin du fondu, un muet
    /// qui se désactive au début. Un nouvel appel annule le fondu en cours et repart de l'état atteint.
    /// </summary>
    Task ApplyAsync(string profileId, TimeSpan? fade = null, CancellationToken cancellationToken = default);

    /// <summary>Profil suivant / précédent dans l'ordre de la liste (boucle).</summary>
    Task ApplyNextAsync(int direction, CancellationToken cancellationToken = default);

    Task ExportAsync(string profileId, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>Importe un profil ; reçoit un nouvel Id s'il entre en collision.</summary>
    Task<Profile> ImportAsync(Stream source, CancellationToken cancellationToken = default);
}
