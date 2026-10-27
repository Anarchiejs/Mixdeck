using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Serialization;
namespace Mixdeck.Core.Settings;
public sealed class SettingsStore(MixdeckPaths paths, ILogger<SettingsStore> logger) : ISettingsStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public AppSettings Current { get; private set; } = new();
    public event EventHandler<AppSettings>? Changed;
    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(paths.SettingsFile))
            {
                try { return Current = await ReadAsync(paths.SettingsFile, cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Réglages illisibles, restauration de la sauvegarde");
                    try { File.Move(paths.SettingsFile, paths.SettingsFile + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff")); }
                    catch (Exception error) { logger.LogWarning(error, "Impossible de déplacer les réglages corrompus"); }
                }
            }
            if (File.Exists(paths.SettingsBackupFile))
            {
                try
                {
                    Current = await ReadAsync(paths.SettingsBackupFile, cancellationToken);
                    await AtomicJson.WriteAsync(paths.SettingsFile, Current, MixdeckJsonContext.Default.AppSettings, cancellationToken);
                    return Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Sauvegarde illisible"); }
            }
            return Current = new();
        }
        finally { gate.Release(); }
    }
    private static async Task<AppSettings> ReadAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Migrate(await JsonSerializer.DeserializeAsync(stream, MixdeckJsonContext.Default.AppSettings, ct) ?? throw new JsonException("Réglages vides"));
    }
    private static AppSettings Migrate(AppSettings settings) => settings.SchemaVersion switch
    {
        1 => settings,
        _ => throw new JsonException("Version de réglages non prise en charge")
    };
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => UpdateAsync(_ => settings, cancellationToken);
    public async Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default)
    {
        AppSettings? saved = null;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var next = change(Current);
            await AtomicJson.WriteAsync(paths.SettingsFile, next, MixdeckJsonContext.Default.AppSettings, cancellationToken);
            Current = saved = next;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Impossible d'enregistrer les réglages"); }
        finally { gate.Release(); }
        if (saved is not null) Changed?.Invoke(this, saved);
    }
}
