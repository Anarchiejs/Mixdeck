using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Mixdeck.Desktop;

public sealed record UpdateCheck(bool Available, string? Version, string? Notes, string? Error);

public sealed class DesktopUpdates
{
    public const string RepositoryUrl = "https://github.com/Anarchiejs/Mixdeck";
    private readonly UpdateManager? _manager;
    private UpdateInfo? _pending;

    public DesktopUpdates()
    {
        try { _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false)); }
        catch (Exception) { _manager = null; }
    }

    public bool IsInstalled => _manager?.IsInstalled == true;

    public string CurrentVersion =>
        _manager?.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.1.0";

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null || !IsInstalled)
            return new UpdateCheck(false, null, null, DesktopText.Get("Updates_Dev"));
        try
        {
            _pending = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
            if (_pending is null) return new UpdateCheck(false, null, null, null);
            var release = _pending.TargetFullRelease;
            return new UpdateCheck(true, release.Version.ToString(), release.NotesMarkdown, null);
        }
        catch (Exception)
        {
            return new UpdateCheck(false, null, null, DesktopText.Get("Updates_Failed"));
        }
    }

    public async Task ApplyAsync(IProgress<int>? progress = null)
    {
        if (_manager is null || _pending is null) return;
        await _manager.DownloadUpdatesAsync(_pending, percent => progress?.Report(percent));
        _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
    }
}
