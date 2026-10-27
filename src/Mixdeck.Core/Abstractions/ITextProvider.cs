namespace Mixdeck.Core.Abstractions;

/// <summary>Fournit les textes de l'interface sans dépendance à Windows.</summary>
public interface ITextProvider
{
    string Get(string key);
}

/// <summary>Valeurs françaises de repli pour les consommateurs sans interface.</summary>
public sealed class FrenchTextProvider : ITextProvider
{
    public static FrenchTextProvider Instance { get; } = new();
    public string Get(string key) => key switch
    {
        "Core_MicMuted" => "Micro coupé",
        "Core_MicOn" => "Micro activé",
        "Core_Profile" => "Profil : {0}",
        "Core_Gain" => "{0} : {1} dB",
        "Core_InputName" => "Entrée {0}",
        "Core_PresetOffName" => "Aucun",
        "Core_PresetOffDescription" => "Désactive la porte de bruit, la compression et la couleur de la voix.",
        "Core_PresetLightName" => "Léger",
        "Core_PresetLightDescription" => "Réduit doucement les bruits faibles et lisse les écarts de voix.",
        "Core_PresetVoiceName" => "Voix",
        "Core_PresetVoiceDescription" => "Atténue les bruits entre les phrases et rend la voix plus claire et régulière.",
        "Core_PresetStrongName" => "Fort",
        "Core_PresetStrongDescription" => "Pour une pièce bruyante : porte de bruit marquée et voix plus présente.",
        _ => key,
    };
}
