using Mixdeck.Core.Model;

namespace Mixdeck.Core.Profiles;

/// <summary>Quatre profils Banana prêts à l'emploi quand le dossier de profils est vide.</summary>
public static class ProfilePresets
{
    public const string CallId = "preset-call";
    public const string GameId = "preset-game";
    public const string MusicId = "preset-music";
    public const string StreamId = "preset-stream";

    public static IReadOnlyList<Profile> Create(DateTimeOffset created)
    {
        var layout = MixerLayout.Banana;
        return
        [
            Make(CallId, "Call", "\uE717", "#3FB950", created, layout, micMute: false, musicGain: -12f, musicBuses: [0], micBuses: [0]),
            Make(GameId, "Game", "\uE7FC", "#C586C0", created.AddSeconds(1), layout, micMute: true, musicGain: 0f, musicBuses: [0, 1], micBuses: []),
            Make(MusicId, "Music", "\uE8D6", "#5A61EB", created.AddSeconds(2), layout, micMute: true, musicGain: 0f, musicBuses: [0], micBuses: []),
            Make(StreamId, "Stream", "\uE714", "#F0555A", created.AddSeconds(3), layout, micMute: false, musicGain: 0f, musicBuses: [0, 3], micBuses: [3]),
        ];
    }

    private static Profile Make(string id, string name, string icon, string color, DateTimeOffset created, MixerLayout layout,
        bool micMute, float musicGain, int[] musicBuses, int[] micBuses)
    {
        var strips = new List<StripProfile>();
        for (var index = 0; index < layout.StripCount; index++)
        {
            var music = index == layout.PhysicalStripCount;
            var mic = index == 0;
            var gain = music ? musicGain : 0f;
            var mute = mic ? micMute : !music;
            var routes = Routes(layout.BusCount, mic ? micBuses : music ? musicBuses : []);
            strips.Add(new StripProfile(index, gain, mute, routes));
        }
        var buses = Enumerable.Range(0, layout.BusCount).Select(index => new BusProfile(index, 0f, false)).ToArray();
        return new Profile
        {
            Id = id,
            Name = name,
            Icon = icon,
            Color = color,
            CreatedAt = created,
            VoicemeeterType = layout.VoicemeeterType,
            Strips = strips,
            Buses = buses,
        };
    }

    private static bool[] Routes(int count, IReadOnlyList<int> enabled)
    {
        var routes = new bool[count];
        foreach (var index in enabled)
        {
            if (index >= 0 && index < count) routes[index] = true;
        }
        return routes;
    }
}
