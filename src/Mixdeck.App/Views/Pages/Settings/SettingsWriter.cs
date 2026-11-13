using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.Views.Pages.Settings;

/// <summary>Relais vers le magasin : false quand l'écriture a échoué.</summary>
internal static class SettingsWriter
{
    internal static Task<bool> UpdateAsync(ISettingsStore store, Func<AppSettings, AppSettings> change) =>
        store.UpdateAsync(change);
}
