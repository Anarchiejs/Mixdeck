using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Mixdeck.Core.Tests;

public sealed class LocalizationResourceTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly HashSet<string> TextProperties = new(StringComparer.Ordinal)
    {
        "Text", "Content", "Header", "Title", "Subtitle", "Description", "PlaceholderText",
        "OnContent", "OffContent", "ToolTipService.ToolTip", "AutomationProperties.Name",
        "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText", "ChannelName", "ProfileName"
    };

    // Pas d'exceptions de traduction : même les marques et noms matériels invariants ont une ressource.
    // Les liaisons, références de ressources, nombres et glyphes sans lettre ne sont pas des textes à traduire.

    [Fact]
    public void FrenchAndEnglishHaveExactlyTheSameUniqueNonEmptyKeys()
    {
        var french = ReadResources("fr-FR");
        var english = ReadResources("en-US");

        french.Should().NotBeEmpty();
        english.Keys.Should().BeEquivalentTo(french.Keys);
        foreach (var (key, value) in french)
        {
            value.Should().NotBeNullOrWhiteSpace($"la ressource française {key} doit être renseignée");
            english[key].Should().NotBeNullOrWhiteSpace($"la ressource anglaise {key} doit être renseignée");
            FormatArguments(english[key]).Should().Equal(FormatArguments(value),
                $"les paramètres de format de {key} doivent être conservés dans la traduction");
        }
    }

    [Fact]
    public void EveryXamlTextIsLocalizedAndEveryUidHasResourcesInBothLanguages()
    {
        var french = ReadResources("fr-FR");
        var english = ReadResources("en-US");
        var violations = new List<string>();
        var files = Directory.GetFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .ToArray();
        files.Should().NotBeEmpty();

        foreach (var file in files)
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                var uid = (string?)element.Attribute(Xaml + "Uid");
                if (uid is not null)
                {
                    var prefix = uid + ".";
                    if (!french.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)) ||
                        !english.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal)))
                        violations.Add($"{Location(file, element)} : x:Uid={uid} sans ressource bilingue");
                }

                foreach (var attribute in element.Attributes().Where(a => a.Name.NamespaceName.Length == 0))
                {
                    if (!TextProperties.Contains(attribute.Name.LocalName) ||
                        attribute.Value.StartsWith('{') || !attribute.Value.Any(char.IsLetter))
                        continue;
                    if (uid is null)
                        violations.Add($"{Location(file, element)} : {attribute.Name}={attribute.Value} sans x:Uid");
                    else
                    {
                        var key = uid + "." + ResourceProperty(attribute.Name.LocalName);
                        if (!french.ContainsKey(key) || !english.ContainsKey(key))
                            violations.Add($"{Location(file, element)} : ressource bilingue manquante {key}");
                    }
                }

                // Un texte XML direct est également visible (TextBlock, Run, Button…).
                // Exceptions techniques : Color contient une valeur ARGB ; FontFamily un nom de police.
                // Ces valeurs servent au rendu et ne sont pas des chaînes affichées.
                if (element.Name.LocalName is not ("Color" or "FontFamily"))
                    foreach (var text in element.Nodes().OfType<XText>().Where(t => t.Value.Any(char.IsLetter)))
                        violations.Add($"{Location(file, element)} : texte XML direct non traduit « {text.Value.Trim()} »");
            }
        }

        violations.Should().BeEmpty("tous les textes XAML doivent provenir des ressources bilingues");
    }

    [Fact]
    public void XamlDoesNotForceFrenchRegardlessOfTheSelectedLanguage()
    {
        var forcedLanguages = Directory.GetFiles(AppDirectory(), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(file => XDocument.Load(file).Descendants()
                .Where(element => element.Attributes().Any(a =>
                    a.Name.LocalName == "Language" && a.Value is "fr-FR" or "en-US"))
                .Select(_ => Path.GetRelativePath(AppDirectory(), file)));

        forcedLanguages.Should().BeEmpty("la culture de l'interface doit suivre la langue choisie au démarrage");
    }

    [Fact]
    public void EveryLiteralCSharpResourceLookupExistsInBothLanguages()
    {
        var french = ReadResources("fr-FR");
        var english = ReadResources("en-US");
        var lookups = Directory.GetFiles(AppDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file),
                "\\bStrings\\.(?:Get|Format)\\(\\s*\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal).ToArray();

        lookups.Should().NotBeEmpty("les textes C# doivent utiliser le fournisseur de ressources");
        foreach (var key in lookups)
        {
            french.Should().ContainKey(key, $"la clé C# {key} doit être disponible en français");
            english.Should().ContainKey(key, $"la clé C# {key} doit être disponible en anglais");
        }
    }

    private static Dictionary<string, string> ReadResources(string language)
    {
        var path = Path.Combine(AppDirectory(), "Strings", language, "Resources.resw");
        File.Exists(path).Should().BeTrue($"le fichier {language}/Resources.resw doit exister");
        var entries = XDocument.Load(path).Root!.Elements("data").ToArray();
        entries.Should().OnlyContain(entry => entry.Attribute("name") != null);
        entries.Select(entry => (string)entry.Attribute("name")!).Should().OnlyHaveUniqueItems();
        return entries.ToDictionary(entry => (string)entry.Attribute("name")!,
            entry => (string?)entry.Element("value") ?? "", StringComparer.Ordinal);
    }

    private static string[] FormatArguments(string value) => Regex.Matches(value,
        @"(?<!\{)\{(\d+)(?:,[^}:]+)?(?::[^}]+)?\}(?!\})")
        .Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal).ToArray();

    private static string ResourceProperty(string property) => property switch
    {
        "AutomationProperties.Name" => "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        "ToolTipService.ToolTip" => "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        _ => property
    };

    private static string Location(string file, XElement element) =>
        $"{Path.GetRelativePath(AppDirectory(), file)}:{((IXmlLineInfo)element).LineNumber}";

    private static string AppDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Mixdeck.slnx")))
                return Path.Combine(directory.FullName, "src", "Mixdeck.App");
        throw new DirectoryNotFoundException("Le dépôt Mixdeck doit être disponible pour vérifier les ressources source.");
    }
}
