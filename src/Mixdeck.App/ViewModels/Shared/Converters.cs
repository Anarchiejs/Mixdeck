using System.Globalization;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>Fonctions pour x:Bind (pas de IValueConverter : x:Bind appelle directement des méthodes statiques).</summary>
public static class Converters
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static bool Not(bool value) => !value;

    public static string MicLabel(bool muted) => muted ? "Micro coupé" : "Micro actif";

    public static string MicGlyph(bool muted) => muted ? "" : "";

    public static string MuteLabel(bool muted) => muted ? "Rétablir le son" : "Couper le son";

    /// <summary>« −6,0 dB », « +2,5 dB », « 0,0 dB » (moins typographique, virgule française).</summary>
    public static string Db(double db)
    {
        var rounded = Math.Round(db, 1);
        var text = Math.Abs(rounded).ToString("0.0", French);
        return rounded switch
        {
            < 0 => $"−{text} dB",
            > 0 => $"+{text} dB",
            _ => $"{text} dB",
        };
    }
}
