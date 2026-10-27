using System.Reflection;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Sources;

namespace Mixdeck.App.Services;

public sealed record UpdateCheckResult(bool IsAvailable, string? Version, string? NotesMarkdown, string? Error = null);

/// <summary>Mises à jour automatiques depuis les releases GitHub (Velopack).</summary>
public interface IUpdateService
{
    /// <summary>Faux en développement (appli non installée par Velopack) : les mises à jour sont alors désactivées.</summary>
    bool IsInstalled { get; }

    string CurrentVersion { get; }

    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Télécharge la mise à jour trouvée par le dernier <see cref="CheckAsync"/>, l'installe et redémarre.</summary>
    Task DownloadAndRestartAsync(IProgress<int>? progress = null);
}

public sealed class UpdateService : IUpdateService
{
    public const string RepositoryUrl = "https://github.com/Anarchiejs/Mixdeck";

    private readonly ILogger<UpdateService> _logger;
    private readonly UpdateManager _manager;
    private UpdateInfo? _pending;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
    }

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return new UpdateCheckResult(false, null, null, "Mises à jour désactivées : Mixdeck n'a pas été installé avec son installateur.");
        }

        try
        {
            _pending = await _manager.CheckForUpdatesAsync();
            if (_pending is null)
            {
                return new UpdateCheckResult(false, null, null);
            }

            var release = _pending.TargetFullRelease;
            return new UpdateCheckResult(true, release.Version.ToString(), release.NotesMarkdown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Échec de la recherche de mise à jour");
            return new UpdateCheckResult(false, null, null, "Impossible de joindre GitHub pour l'instant.");
        }
    }

    public async Task DownloadAndRestartAsync(IProgress<int>? progress = null)
    {
        if (_pending is null)
        {
            return;
        }

        await _manager.DownloadUpdatesAsync(_pending, p => progress?.Report(p));
        _logger.LogInformation("Installation de la version {Version} et redémarrage", _pending.TargetFullRelease.Version);
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
