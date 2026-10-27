using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Serialization;
using Mixdeck.Core.Settings;
namespace Mixdeck.Core.Profiles;
public sealed class ProfileService(IMixer mixer, ISettingsStore settings, MixdeckPaths paths,
    ILogger<ProfileService> logger, TimeProvider timeProvider) : IProfileService
{
    private readonly SemaphoreSlim files = new(1, 1);
    private readonly object fadeLock = new();
    private CancellationTokenSource? currentFade;
    public IReadOnlyList<Profile> Profiles { get; private set; } = [];
    public Profile? Active { get; private set; }
    public event EventHandler? ProfilesChanged;
    public event EventHandler<Profile?>? ActiveChanged;
    private string FilePath(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id is "." or "..")
            throw new ArgumentException("Identifiant de profil invalide", nameof(id));
        return Path.Combine(paths.ProfilesDirectory, id + ".json");
    }
    private void Publish(IEnumerable<Profile> profiles) => Profiles = profiles.OrderBy(p => p.CreatedAt).ThenBy(p => p.Name, StringComparer.Ordinal).ToArray();
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await files.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(paths.ProfilesDirectory);
            var loaded = new List<Profile>();
            foreach (var file in Directory.EnumerateFiles(paths.ProfilesDirectory, "*.json"))
            {
                try
                {
                    await using var stream = File.OpenRead(file);
                    var profile = await JsonSerializer.DeserializeAsync(stream, MixdeckJsonContext.Default.Profile, cancellationToken)
                        ?? throw new JsonException("Profil vide");
                    if (!string.Equals(Path.GetFullPath(FilePath(profile.Id)), Path.GetFullPath(file), StringComparison.OrdinalIgnoreCase))
                        throw new JsonException("Identifiant différent du nom de fichier");
                    loaded.Add(Sanitize(profile));
                }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Profil illisible : {File}", file); }
            }
            Publish(loaded);
        }
        finally { files.Release(); }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task<Profile> CaptureCurrentAsync(string name, CancellationToken cancellationToken = default)
    {
        var snapshot = mixer.Snapshot;
        var profile = new Profile { Id = Profile.NewId(), Name = name, CreatedAt = timeProvider.GetUtcNow(),
            Strips = snapshot.Strips.Select(s => new StripProfile(s.Index, s.Gain, s.Mute, s.Routes.ToArray(), s.PanX)).ToArray(),
            Buses = snapshot.Buses.Select(b => new BusProfile(b.Index, b.Gain, b.Mute)).ToArray() };
        await SaveAsync(profile, cancellationToken);
        return profile;
    }
    public async Task SaveAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        profile = Sanitize(profile);
        await files.WaitAsync(cancellationToken);
        try
        {
            await AtomicJson.WriteAsync(FilePath(profile.Id), profile, MixdeckJsonContext.Default.Profile, cancellationToken);
            Publish(Profiles.Where(p => p.Id != profile.Id).Append(profile));
        }
        finally { files.Release(); }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await files.WaitAsync(cancellationToken);
        try
        {
            var path = FilePath(profileId);
            File.Delete(path);
            File.Delete(path + ".bak");
            Publish(Profiles.Where(p => p.Id != profileId));
        }
        finally { files.Release(); }
        if (Active?.Id == profileId) { Active = null; ActiveChanged?.Invoke(this, null); }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
    private Profile Find(string id) => Profiles.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException("Profil introuvable : " + id);
    public async Task<Profile> DuplicateAsync(string profileId, string newName, CancellationToken cancellationToken = default)
    {
        var copy = Find(profileId) with { Id = Profile.NewId(), Name = newName, CreatedAt = timeProvider.GetUtcNow() };
        await SaveAsync(copy, cancellationToken);
        return copy;
    }
    public async Task ApplyAsync(string profileId, TimeSpan? fade = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var profile = Find(profileId);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        MixerSnapshot snapshot;
        lock (fadeLock)
        {
            currentFade?.Cancel();
            currentFade = source;
            snapshot = mixer.Snapshot;
            Active = profile;
        }
        try
        {
            ActiveChanged?.Invoke(this, profile);
            var duration = fade ?? TimeSpan.FromMilliseconds(Math.Max(0, settings.Current.FadeMilliseconds));
            var steps = Math.Max(1, (int)Math.Ceiling(duration.TotalMilliseconds / 16));
            for (var step = duration <= TimeSpan.Zero ? steps : 0; step <= steps; step++)
            {
                if (duration > TimeSpan.Zero && step > 0)
                {
                    var previous = Math.Min((step - 1) * 16d, duration.TotalMilliseconds);
                    var next = Math.Min(step * 16d, duration.TotalMilliseconds);
                    await Task.Delay(TimeSpan.FromMilliseconds(next - previous), timeProvider, source.Token);
                }
                lock (fadeLock)
                {
                    source.Token.ThrowIfCancellationRequested();
                    var script = new MixerScript();
                    var fraction = duration <= TimeSpan.Zero ? 1f : (float)(Math.Min(step * 16d, duration.TotalMilliseconds) / duration.TotalMilliseconds);
                    if (step == 0 || duration <= TimeSpan.Zero)
                    {
                        foreach (var s in profile.Strips)
                        {
                            for (var bus = 0; bus < s.Routes.Count; bus++) script.Set(VmParam.StripRoute(s.Index, bus), s.Routes[bus]);
                            script.Set(VmParam.StripPanX(s.Index), s.PanX);
                            if (!s.Mute) script.Set(VmParam.StripMute(s.Index), false);
                        }
                        foreach (var b in profile.Buses) if (!b.Mute) script.Set(VmParam.BusMute(b.Index), false);
                    }
                    foreach (var s in profile.Strips)
                    {
                        var start = snapshot.Strips.FirstOrDefault(x => x.Index == s.Index)?.Gain ?? 0;
                        script.Set(VmParam.StripGain(s.Index), step == steps ? s.Gain : start + (s.Gain - start) * fraction);
                    }
                    foreach (var b in profile.Buses)
                    {
                        var start = snapshot.Buses.FirstOrDefault(x => x.Index == b.Index)?.Gain ?? 0;
                        script.Set(VmParam.BusGain(b.Index), step == steps ? b.Gain : start + (b.Gain - start) * fraction);
                    }
                    if (step == steps)
                    {
                        foreach (var s in profile.Strips) if (s.Mute) script.Set(VmParam.StripMute(s.Index), true);
                        foreach (var b in profile.Buses) if (b.Mute) script.Set(VmParam.BusMute(b.Index), true);
                    }
                    mixer.ApplyScript(script.ToString());
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        finally { lock (fadeLock) { if (ReferenceEquals(currentFade, source)) currentFade = null; } }
    }
    public Task ApplyNextAsync(int direction, CancellationToken cancellationToken = default)
    {
        var list = Profiles;
        if (list.Count == 0) return Task.CompletedTask;
        var index = list.ToList().FindIndex(p => p.Id == Active?.Id);
        var next = index < 0 ? 0 : (index + (direction < 0 ? -1 : 1) + list.Count) % list.Count;
        return ApplyAsync(list[next].Id, cancellationToken: cancellationToken);
    }
    public Task ExportAsync(string profileId, Stream destination, CancellationToken cancellationToken = default) =>
        JsonSerializer.SerializeAsync(destination, Find(profileId), MixdeckJsonContext.Default.Profile, cancellationToken);
    public async Task<Profile> ImportAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var profile = Sanitize(await JsonSerializer.DeserializeAsync(source, MixdeckJsonContext.Default.Profile, cancellationToken) ?? throw new JsonException("Profil vide"));
        if (Profiles.Any(p => p.Id == profile.Id)) profile = profile with { Id = Profile.NewId() };
        await SaveAsync(profile, cancellationToken);
        return profile;
    }
    private static float Gain(float gain) => float.IsFinite(gain) ? BananaLayout.ClampGain(gain) : BananaLayout.MinGainDb;
    private static Profile Sanitize(Profile p) => p with
    {
        Strips = (p.Strips ?? []).Where(s => s.Index is >= 0 and < BananaLayout.StripCount).DistinctBy(s => s.Index)
            .Select(s => s with { Gain = Gain(s.Gain), Routes = Enumerable.Range(0, BananaLayout.BusCount).Select(i => i < (s.Routes?.Count ?? 0) && s.Routes![i]).ToArray(), PanX = float.IsFinite(s.PanX) ? Math.Clamp(s.PanX, -1, 1) : 0 }).ToArray(),
        Buses = (p.Buses ?? []).Where(b => b.Index is >= 0 and < BananaLayout.BusCount).DistinctBy(b => b.Index).Select(b => b with { Gain = Gain(b.Gain) }).ToArray()
    };
}
