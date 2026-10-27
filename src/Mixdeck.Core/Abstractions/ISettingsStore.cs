using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Abstractions;

/// <summary>
/// Réglages JSON. Écriture atomique (fichier temporaire puis remplacement) avec copie .bak ; au chargement, un
/// fichier corrompu est mis de côté et la copie .bak est restaurée, sinon les valeurs par défaut.
/// </summary>
public interface ISettingsStore
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? Changed;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Applique une modification et enregistre : <c>await store.UpdateAsync(s => s with { ShowOsd = false })</c>.</summary>
    Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default);
}
