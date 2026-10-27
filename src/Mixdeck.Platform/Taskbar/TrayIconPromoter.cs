using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Mixdeck.Platform.Taskbar;

public enum TrayPromotionResult
{
    /// <summary>La visibilité de l'icône a été modifiée.</summary>
    Changed,
    /// <summary>L'état était déjà celui demandé.</summary>
    Unchanged,
    /// <summary>Windows n'a pas (encore) de fiche pour notre icône.</summary>
    NotFound,
    Failed,
}

/// <summary>
/// Windows 11 range les nouvelles icônes derrière la flèche ^. Chaque icône a une fiche dans
/// HKCU\Control Panel\NotifyIconSettings\&lt;n&gt; (ExecutablePath, IsPromoted) que l'Explorateur relit en direct :
/// IsPromoted = 1 la montre près de l'horloge, 0 la cache. La fiche n'apparaît qu'après la première apparition de l'icône.
/// </summary>
public sealed partial class TrayIconPromoter
{
    private const string SettingsKey = @"Control Panel\NotifyIconSettings";
    private const string PromotedValue = "IsPromoted";
    private readonly string _keyPath;
    private readonly string _executablePath;
    private readonly ILogger<TrayIconPromoter> _logger;

    public TrayIconPromoter(ILogger<TrayIconPromoter>? logger = null)
        : this(SettingsKey, Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable courant introuvable."), logger) { }

    internal TrayIconPromoter(string keyPath, string executablePath, ILogger<TrayIconPromoter>? logger = null)
    {
        _keyPath = keyPath;
        _executablePath = executablePath;
        _logger = logger ?? NullLogger<TrayIconPromoter>.Instance;
    }

    /// <summary>Montre (true) ou cache (false) l'icône près de l'horloge, sur toutes les fiches de notre exécutable.</summary>
    public TrayPromotionResult SetVisible(bool visible)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(_keyPath);
            if (root is null) return TrayPromotionResult.NotFound;
            var found = false;
            var changed = false;
            foreach (var name in root.GetSubKeyNames())
            {
                using var entry = root.OpenSubKey(name, writable: true);
                if (entry?.GetValue("ExecutablePath") is not string path || !IsSameExecutable(path, _executablePath)) continue;
                found = true;
                var wanted = visible ? 1 : 0;
                if (entry.GetValue(PromotedValue) is int current && current == wanted) continue;
                entry.SetValue(PromotedValue, wanted, RegistryValueKind.DWord);
                changed = true;
            }
            if (!found) return TrayPromotionResult.NotFound;
            if (changed) _logger.LogInformation("Icône {Etat} près de l'horloge", visible ? "affichée" : "rangée");
            return changed ? TrayPromotionResult.Changed : TrayPromotionResult.Unchanged;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _logger.LogWarning(ex, "Impossible de régler la visibilité de l'icône");
            return TrayPromotionResult.Failed;
        }
    }

    /// <summary>
    /// Comme <see cref="SetVisible"/>, en réessayant tant que Windows n'a pas créé la fiche de l'icône
    /// (quelques secondes après sa première apparition).
    /// </summary>
    public async Task<TrayPromotionResult> SetVisibleWhenReadyAsync(bool visible, TimeSpan timeout, TimeProvider? time = null,
        CancellationToken cancellationToken = default)
    {
        time ??= TimeProvider.System;
        var deadline = time.GetUtcNow() + timeout;
        while (true)
        {
            var result = SetVisible(visible);
            if (result != TrayPromotionResult.NotFound || time.GetUtcNow() >= deadline) return result;
            await Task.Delay(TimeSpan.FromMilliseconds(500), time, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// L'Explorateur écrit le chemin en clair ou préfixé d'un dossier connu ("{GUID}\VB\app.exe" pour Program Files).
    /// </summary>
    internal static bool IsSameExecutable(string registered, string executable)
    {
        var resolved = ResolveKnownFolder(Environment.ExpandEnvironmentVariables(registered.Trim()));
        try
        {
            return string.Equals(Path.GetFullPath(resolved), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string ResolveKnownFolder(string path)
    {
        if (path.Length < 39 || path[0] != '{' || path[37] != '}' || !Guid.TryParse(path.AsSpan(1, 36), out var folder)) return path;
        if (SHGetKnownFolderPath(folder, 0, 0, out var buffer) != 0) return path;
        try
        {
            return Marshal.PtrToStringUni(buffer) + path[38..];
        }
        finally
        {
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [LibraryImport("shell32.dll")]
    private static partial int SHGetKnownFolderPath(in Guid folder, uint flags, nint token, out nint path);
}
