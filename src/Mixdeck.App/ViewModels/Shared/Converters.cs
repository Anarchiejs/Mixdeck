using Mixdeck.App.Localization;
using System.Globalization;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>Fonctions pour x:Bind (pas de IValueConverter : x:Bind appelle directement des méthodes statiques).</summary>
public static class Converters
{
    private static CultureInfo DisplayCulture => Strings.Culture;

    public static bool Not(bool value) => !value;

    public static string MicLabel(bool muted) => muted ? Strings.Get("Core_MicMuted") : Strings.Get("CS_021");

    public static string MicGlyph(bool muted) => muted ? "" : "";

    public static string MuteLabel(bool muted) => muted ? Strings.Get("CS_068") : Strings.Get("CS_003");

    /// <summary>« −6,0 dB », « +2,5 dB », « 0,0 dB » (moins typographique, virgule française).</summary>
    public static string Db(double db)
    {
        var rounded = Math.Round(db, 1);
        var text = Math.Abs(rounded).ToString("0.0", DisplayCulture);
        return rounded switch
        {
            < 0 => $"−{text} dB",
            > 0 => $"+{text} dB",
            _ => $"{text} dB",
        };
    }
}
