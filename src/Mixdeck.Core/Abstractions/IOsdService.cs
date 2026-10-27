namespace Mixdeck.Core.Abstractions;

/// <summary>Petite bulle discrète affichée par-dessus les jeux quand un raccourci ou une règle change quelque chose.</summary>
public interface IOsdService
{
    /// <param name="glyph">Glyphe Segoe Fluent Icons.</param>
    /// <param name="isWarning">Couleur d'alerte (ex. micro coupé).</param>
    void Show(string text, string glyph, bool isWarning = false);
}
