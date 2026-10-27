using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.App.Localization;

/// <summary>Textes résolus dans le PRI de l’application, avec la même langue que le XAML.</summary>
public static class Strings
{
    private static readonly Lazy<ResourceLoader> Loader = new(() => new ResourceLoader());
    private static readonly Lazy<string> SystemUiLanguage = new(() => CultureInfo.CurrentUICulture.Name);
    public static string Language { get; private set; } = "fr-FR";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language);

    public static void ApplyLanguage(string? requested)
    {
        // Langue d'affichage de Windows (et non le premier élément de la liste des langues préférées, qui peut être
        // l'anglais sur un Windows affiché en français).
        var windows = SystemUiLanguage.Value;
        Language = requested is "fr-FR" or "en-US" ? requested
            : windows.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en-US" : "fr-FR";
        Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = Language;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }

    public static string Get(string key) => Loader.Value.GetString(key.Replace('.', '/'));
    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);
}

internal sealed class AppTextProvider : ITextProvider
{
    public string Get(string key) => Strings.Get(key);
    public CultureInfo Culture => Strings.Culture;
}
