using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;
using Mixdeck.Core.Serialization;
using Mixdeck.Core.Settings;
using Mixdeck.Core.Automation;
namespace Mixdeck.Core.Profiles;
public sealed class ProfileService(IMixer mixer, ISettingsStore settings, MixdeckPaths paths,
    ILogger<ProfileService> logger, TimeProvider timeProvider, IDaw? daw = null) : IProfileService
{
    private readonly IDaw daw = daw ?? NullDaw.Instance;
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
        var seeded = false;
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
            seeded = loaded.Count == 0;
            if (seeded)
            {
                foreach (var preset in ProfilePresets.Create(timeProvider.GetUtcNow()))
                    loaded.Add(preset);
                Publish(loaded);
            }
        }
        finally { files.Release(); }
        if (seeded)
        {
            foreach (var preset in Profiles.ToArray())
            {
                try { await SaveAsync(preset, cancellationToken); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Les profils restent utilisables en mémoire si le disque refuse l'écriture.
                }
            }
        }
        ProfilesChanged?.Invoke(this, EventArgs.Empty);
    }
    public async Task<Profile> CaptureCurrentAsync(string name, CancellationToken cancellationToken = default)
    {
        var snapshot = mixer.Snapshot;
        var profile = new Profile { Id = Profile.NewId(), Name = name, CreatedAt = timeProvider.GetUtcNow(),
            VoicemeeterType = snapshot.Layout.VoicemeeterType,
            Daw = settings.Current.FlStudio.CaptureInProfiles && daw.State == DawConnectionState.Connected
                ? daw.Tracks.Values.Select(t => new DawTrackProfile(t.Index, t.Muted, t.Volume, t.SlotsEnabled, t.Slots.ToArray())).ToArray()
                : [],
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
        var sourceLayout = MixerLayout.ForType(profile.VoicemeeterType);
        var layout = mixer.Layout;
        var strips = profile.Strips.Select(s => s with { Index = FindStrip(layout, sourceLayout, s.Index) })
            .Where(s => s.Index >= 0).ToArray();
        var buses = profile.Buses.Select(b => (Bus: b, Index: FindBus(layout, sourceLayout.BusNames[b.Index])))
            .Where(b => b.Index >= 0).ToArray();
        if (sourceLayout != layout)
            logger.LogInformation("Profil {Profile} : adaptation du type {Source} au type {Target}, canaux et routes absents ignorés",
                profile.Name, sourceLayout.VoicemeeterType, layout.VoicemeeterType);
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
            ApplyDaw(profile);
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
                    // Une reconnexion pendant le fondu invalide les index déjà préparés.
                    if (mixer.Layout != layout) return;
                    var script = new MixerScript();
                    var fraction = duration <= TimeSpan.Zero ? 1f : (float)(Math.Min(step * 16d, duration.TotalMilliseconds) / duration.TotalMilliseconds);
                    if (step == 0 || duration <= TimeSpan.Zero)
                    {
                        foreach (var s in strips)
                        {
                            for (var bus = 0; bus < s.Routes.Count; bus++)
                            {
                                var targetBus = FindBus(layout, sourceLayout.BusNames[bus]);
                                if (targetBus >= 0) script.Set(VmParam.StripRoute(s.Index, targetBus, layout), s.Routes[bus]);
                            }
                            script.Set(VmParam.StripPanX(s.Index), s.PanX);
                            if (!s.Mute) script.Set(VmParam.StripMute(s.Index), false);
                        }
                        foreach (var b in buses) if (!b.Bus.Mute) script.Set(VmParam.BusMute(b.Index), false);
                    }
                    foreach (var s in strips)
                    {
                        var start = snapshot.Strips.FirstOrDefault(x => x.Index == s.Index)?.Gain ?? 0;
                        script.Set(VmParam.StripGain(s.Index), step == steps ? s.Gain : start + (s.Gain - start) * fraction);
                    }
                    foreach (var b in buses)
                    {
                        var start = snapshot.Buses.FirstOrDefault(x => x.Index == b.Index)?.Gain ?? 0;
                        script.Set(VmParam.BusGain(b.Index), step == steps ? b.Bus.Gain : start + (b.Bus.Gain - start) * fraction);
                    }
                    if (step == steps)
                    {
                        foreach (var s in strips) if (s.Mute) script.Set(VmParam.StripMute(s.Index), true);
                        foreach (var b in buses) if (b.Bus.Mute) script.Set(VmParam.BusMute(b.Index), true);
                    }
                    var commands = script.ToString();
                    if (commands.Length > 0) mixer.ApplyScript(commands);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        finally { lock (fadeLock) { if (ReferenceEquals(currentFade, source)) currentFade = null; } }
    }
    private int dawSubscribed;
    private void DawStateChanged(object? sender, DawConnectionState state)
    {
        if (state == DawConnectionState.Connected && Active is { } profile) ApplyDaw(profile);
    }
    private void ApplyDaw(Profile profile)
    {
        if (profile.Daw.Count == 0) return;
        if (daw.State != DawConnectionState.Connected)
        {
            // Cas typique : une règle applique le profil au lancement de FL, avant que le script réponde.
            // L'état FL sera appliqué dès la connexion, si ce profil est toujours actif.
            logger.LogInformation("Profil {Profile} : FL Studio pas encore connecté, pistes appliquées à la connexion", profile.Name);
            if (Interlocked.Exchange(ref dawSubscribed, 1) == 0) daw.StateChanged += DawStateChanged;
            return;
        }
        var tracks = daw.Tracks;
        foreach (var target in profile.Daw)
        {
            tracks.TryGetValue(target.Track, out var current);
            if (target.Muted is { } muted && current?.Muted != muted) daw.SetMute(target.Track, muted);
            // Le volume voyage sur 14 bits : un écart d'un pas n'est pas une différence.
            if (target.Volume is { } volume && (current is null || Math.Abs(current.Volume - volume) > 1f / 16383)) daw.SetVolume(target.Track, volume);
            if (target.SlotsEnabled is { } enabled && current?.SlotsEnabled != enabled) daw.SetSlotsEnabled(target.Track, enabled);
            foreach (var slot in target.Slots ?? [])
                if (current?.Slots.FirstOrDefault(s => s.Index == slot.Index)?.Enabled != slot.Enabled)
                    daw.SetSlot(target.Track, slot.Index, slot.Enabled);
        }
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
    private static int FindBus(MixerLayout layout, string name)
    {
        for (var i = 0; i < layout.BusCount; i++) if (layout.BusNames[i] == name) return i;
        return -1;
    }
    private static int FindStrip(MixerLayout target, MixerLayout source, int index)
    {
        // Les entrées virtuelles suivent les physiques : leur index change entre éditions.
        if (!source.IsVirtualStrip(index)) return index < target.PhysicalStripCount ? index : -1;
        var mapped = target.PhysicalStripCount + index - source.PhysicalStripCount;
        return mapped < target.StripCount ? mapped : -1;
    }
    private static float Gain(float gain) => float.IsFinite(gain) ? MixerLayout.Banana.ClampGain(gain) : MixerLayout.MinGainDb;
    private static Profile Sanitize(Profile p)
    {
        var layout = p.VoicemeeterType is >= 1 and <= 3 ? MixerLayout.ForType(p.VoicemeeterType) : MixerLayout.Banana;
        return p with
        {
            Daw = p.Daw ?? [],
            VoicemeeterType = layout.VoicemeeterType,
            Strips = (p.Strips ?? []).Where(s => s.Index >= 0 && s.Index < layout.StripCount).DistinctBy(s => s.Index)
                .Select(s => s with { Gain = Gain(s.Gain), Routes = Enumerable.Range(0, layout.BusCount).Select(i => i < (s.Routes?.Count ?? 0) && s.Routes![i]).ToArray(), PanX = float.IsFinite(s.PanX) ? Math.Clamp(s.PanX, -1, 1) : 0 }).ToArray(),
            Buses = (p.Buses ?? []).Where(b => b.Index >= 0 && b.Index < layout.BusCount).DistinctBy(b => b.Index).Select(b => b with { Gain = Gain(b.Gain) }).ToArray()
        };
    }
}
