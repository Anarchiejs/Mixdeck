using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Audio;

internal sealed record SessionEntry(AppAudioSession Session, Guid Grouping);

internal static class AppAudioSessionPolicy
{
    internal static string DisplayName(string? sessionName, string? fileDescription, string processName, bool system) =>
        !string.IsNullOrWhiteSpace(sessionName) ? sessionName.Trim() :
        system ? AppAudioSessionNames.SystemSoundsFallback :
        !string.IsNullOrWhiteSpace(fileDescription) ? fileDescription.Trim() :
        !string.IsNullOrWhiteSpace(processName) ? processName : AppAudioSessionNames.UnknownApplicationFallback;

    // Comme Sndvol : GUID_NULL signifie une ligne indépendante, même pour un PID identique.
    internal static string GroupKey(SessionEntry entry) => entry.Grouping == Guid.Empty
        ? "session:" + entry.Session.Id : "group:" + entry.Grouping;

    internal static IReadOnlyList<AppAudioSession> Group(IEnumerable<SessionEntry> entries) =>
        Array.AsReadOnly(entries.GroupBy(GroupKey)
            .Select(group => group.OrderBy(entry => entry.Session.Id, StringComparer.Ordinal).First().Session)
            .OrderBy(session => session.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(session => session.Id, StringComparer.Ordinal).ToArray());
}

/// <summary>Regroupe les rafales sans repousser indéfiniment la dernière notification.</summary>
internal sealed class AppAudioEventLimiter(TimeProvider time)
{
    private readonly long _interval = Math.Max(1, time.TimestampFrequency / 10);
    private long? _last;
    internal bool IsPending { get; private set; }
    internal void MarkChanged() => IsPending = true;

    internal int DelayMilliseconds
    {
        get
        {
            if (!IsPending) return Timeout.Infinite;
            if (_last is null) return 0;
            var remaining = _interval - (time.GetTimestamp() - _last.Value);
            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining * 1000d / time.TimestampFrequency);
        }
    }

    internal bool Take()
    {
        if (!IsPending || DelayMilliseconds != 0) return false;
        IsPending = false;
        _last = time.GetTimestamp();
        return true;
    }
}
