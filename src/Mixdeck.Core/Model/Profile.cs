namespace Mixdeck.Core.Model;

/// <summary>Un ensemble de réglages (gains, muets, routage) enregistré en JSON dans profiles/&lt;Id&gt;.json.</summary>
public sealed record Profile
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Glyphe Segoe Fluent Icons (ex. "" micro, "" jeu, "" musique).</summary>
    public string Icon { get; init; } = "";

    /// <summary>Couleur de la pastille, "#RRGGBB" ; null = couleur d'accent.</summary>
    public string? Color { get; init; }

    public IReadOnlyList<StripProfile> Strips { get; init; } = [];
    public IReadOnlyList<BusProfile> Buses { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public static string NewId() => Guid.NewGuid().ToString("N");
}

/// <param name="Routes">5 booléens A1, A2, A3, B1, B2.</param>
public sealed record StripProfile(int Index, float Gain, bool Mute, IReadOnlyList<bool> Routes, float PanX = 0f);

public sealed record BusProfile(int Index, float Gain, bool Mute);
