namespace Mixdeck.Voicemeeter;

public sealed class VoicemeeterMixerOptions
{
    public TimeSpan DirtyPollInterval { get; init; } = TimeSpan.FromMilliseconds(33);
    public TimeSpan LevelInterval { get; init; } = TimeSpan.FromMilliseconds(16);
    public TimeSpan ReconnectInterval { get; init; } = TimeSpan.FromSeconds(2);
    public int LevelType { get; init; } = 1;

    internal void Validate()
    {
        if (DirtyPollInterval <= TimeSpan.Zero || LevelInterval <= TimeSpan.Zero || ReconnectInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(DirtyPollInterval), "Les intervalles doivent être positifs.");
        if (LevelType is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(LevelType));
    }
}
