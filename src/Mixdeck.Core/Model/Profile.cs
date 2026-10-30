namespace Mixdeck.Core.Model;

/// <summary>Un ensemble de réglages (gains, muets, routage) enregistré en JSON dans profiles/&lt;Id&gt;.json.</summary>
public sealed record Profile
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Disposition à la capture ; les anciens profils sont des profils Banana.</summary>
    public int VoicemeeterType { get; init; } = 2;

    /// <summary>Glyphe Segoe Fluent Icons (ex. "" micro, "" jeu, "" musique).</summary>
    public string Icon { get; init; } = "";

    /// <summary>Couleur de la pastille, "#RRGGBB" ; null = couleur d'accent.</summary>
    public string? Color { get; init; }

    public IReadOnlyList<StripProfile> Strips { get; init; } = [];
    public IReadOnlyList<BusProfile> Buses { get; init; } = [];

    /// <summary>État de pistes de FL Studio à appliquer avec le profil ; vide = ne pas toucher à FL.</summary>
    public IReadOnlyList<DawTrackProfile> Daw { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <param name="Routes">Routages dans l'ordre des bus du type de Voicemeeter enregistré.</param>
public sealed record StripProfile(int Index, float Gain, bool Mute, IReadOnlyList<bool> Routes, float PanX = 0f);

public sealed record BusProfile(int Index, float Gain, bool Mute);
