using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.Views.Pages.Settings;

/// <summary>Le magasin journalise les échecs : une notification confirme la réussite de cette écriture.</summary>
internal static class SettingsWriter
{
    internal static async Task<bool> UpdateAsync(ISettingsStore store, Func<AppSettings, AppSettings> change)
    {
        AppSettings? expected = null;
        var confirmed = false;
        void Saved(object? sender, AppSettings value)
        {
            if (ReferenceEquals(value, expected)) confirmed = true;
        }
        store.Changed += Saved;
        try
        {
            await store.UpdateAsync(current => expected = change(current));
            return confirmed;
        }
        catch (Exception) { return false; }
        finally { store.Changed -= Saved; }
    }
}
