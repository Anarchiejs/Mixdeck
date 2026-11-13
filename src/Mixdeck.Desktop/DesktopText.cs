using System.Globalization;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Desktop;

public static class DesktopText
{
    private static readonly (string Key, string English, string French)[] Entries =
    [
        ("Nav_Audio", "Audio", "Audio"),
        ("Nav_Apps", "Applications", "Applications"),
        ("Nav_Studio", "Studio", "Studio"),
        ("Nav_Settings", "Settings", "Réglages"),
        ("Audio_Outputs", "Playback", "Lecture"),
        ("Audio_Inputs", "Recording", "Enregistrement"),
        ("Audio_Default", "Default", "Par défaut"),
        ("Audio_Mute", "Mute", "Sourdine"),
        ("Audio_Empty", "No devices reported yet.", "Aucun périphérique pour l'instant."),
        ("Audio_Refresh", "Refresh", "Actualiser"),
        ("Apps_Empty", "Nothing is playing through the default output.", "Aucune application ne joue sur la sortie par défaut."),
        ("Apps_Unavailable", "Per-application volume is not available on this system.", "Le volume par application n'est pas disponible sur ce système."),
        ("Apps_Route", "Output", "Sortie"),
        ("Apps_DefaultRoute", "Default output", "Sortie par défaut"),
        ("Studio_Voicemeeter", "Voicemeeter", "Voicemeeter"),
        ("Studio_VoicemeeterBody", "Voicemeeter runs on Windows only. Mixdeck does not imitate it here. System playback and recording are on the Audio page.", "Voicemeeter n'existe que sur Windows. Mixdeck ne le simule pas ici. La lecture et l'enregistrement du système sont sur la page Audio."),
        ("Studio_Demo", "Demo mixer (not Voicemeeter)", "Mixeur de démonstration (pas Voicemeeter)"),
        ("Studio_Mute", "Mute", "Muet"),
        ("Fl_Title", "FL Studio", "FL Studio"),
        ("Fl_Unavailable", "FL Studio control is available on Windows and macOS. No MIDI port is opened here.", "Le pilotage de FL Studio est disponible sous Windows et macOS. Aucun port MIDI n'est ouvert ici."),
        ("Fl_Mac", "Mixdeck talks to FL Studio through Core MIDI. Use an IAC port named in Settings, or let Mixdeck create virtual ports called “Mixdeck to FL” and “FL to Mixdeck”.", "Mixdeck parle à FL Studio par Core MIDI. Utilisez un port IAC du nom indiqué dans les réglages, ou laissez Mixdeck créer les ports virtuels « Mixdeck to FL » et « FL to Mixdeck »."),
        ("Fl_Connected", "Connected", "Connecté"),
        ("Fl_Waiting", "Waiting for FL Studio", "En attente de FL Studio"),
        ("Fl_Ports", "MIDI ports are missing", "Ports MIDI absents"),
        ("Fl_Disabled", "Turned off in settings", "Désactivé dans les réglages"),
        ("Settings_Language", "Language", "Langue"),
        ("Settings_System", "System language", "Langue du système"),
        ("Settings_English", "English", "Anglais"),
        ("Settings_French", "French", "Français"),
        ("Settings_Autostart", "Start when I sign in", "Démarrer à l'ouverture de session"),
        ("Settings_AutostartHint", "A copy launched from bin/ does not register itself. An installed package does.", "Une copie lancée depuis bin/ ne s'inscrit pas. Un paquet installé le fait."),
        ("Settings_Osd", "Show a status line when a hotkey changes the volume or the microphone", "Afficher une ligne d'état quand un raccourci change le volume ou le micro"),
        ("Settings_Updates", "Check for updates", "Chercher les mises à jour"),
        ("Settings_Hotkeys", "Hotkeys", "Raccourcis"),
        ("Settings_HotkeyFailed", "not registered", "non enregistré"),
        ("Settings_Features", "What works here", "Ce qui fonctionne ici"),
        ("Feature_Yes", "Available", "Disponible"),
        ("Feature_No", "Unavailable", "Indisponible"),
        ("Feature_Voicemeeter", "Voicemeeter mixer, profiles, and rules", "Mixeur Voicemeeter, profils et règles"),
        ("Feature_Devices", "System devices, default device, volume, and mute", "Périphériques, périphérique par défaut, volume et sourdine"),
        ("Feature_Apps", "Per-application volume", "Volume par application"),
        ("Feature_Route", "Send an application to another output", "Envoyer une application vers une autre sortie"),
        ("Feature_Hotkeys", "Global hotkeys", "Raccourcis globaux"),
        ("Feature_Foreground", "Foreground application", "Application au premier plan"),
        ("Feature_Fl", "FL Studio", "FL Studio"),
        ("Menu_Open", "Open Mixdeck", "Ouvrir Mixdeck"),
        ("Menu_Mute", "Mute microphone", "Couper le micro"),
        ("Menu_Quit", "Quit", "Quitter"),
        ("Status_ProfileWindowsOnly", "Profiles drive Voicemeeter, which is Windows-only.", "Les profils pilotent Voicemeeter, qui n'existe que sur Windows."),
        ("Status_AudioUnavailable", "System audio is not available.", "L'audio du système n'est pas disponible."),
        ("Status_Saved", "Saved", "Enregistré"),
        ("About_Body", "Mixdeck on macOS controls the system audio devices. The Windows app remains the Voicemeeter console.", "Sous macOS, Mixdeck pilote les périphériques audio du système. L'application Windows reste la console Voicemeeter."),
        ("About_CopyCrash", "Copy redacted crash report", "Copier le rapport expurgé"),
        ("Updates_None", "You are up to date.", "Vous êtes à jour."),
        ("Updates_Ready", "Update {0} is ready.", "La mise à jour {0} est prête."),
        ("Updates_Dev", "This build is not installed, so it does not update itself.", "Cette version n'est pas installée : elle ne se met pas à jour."),
        ("Updates_Failed", "The update check failed.", "La recherche de mise à jour a échoué."),
        ("Core_DawSlotOn", "{0}: {1} on", "{0} : {1} activé"),
        ("Core_DawSlotOff", "{0}: {1} off", "{0} : {1} coupé"),
        ("Core_DawTrackMuted", "{0} muted", "{0} coupée"),
        ("Core_DawTrackOn", "{0} on", "{0} rétablie"),
        ("Core_DawSlotName", "Slot {0}", "Slot {0}"),
        ("Core_MicMuted", "Microphone muted", "Micro coupé"),
        ("Core_MicOn", "Microphone on", "Micro activé"),
        ("Core_Profile", "Profile: {0}", "Profil : {0}"),
        ("Core_Gain", "{0}: {1} dB", "{0} : {1} dB"),
        ("Core_InputName", "Input {0}", "Entrée {0}"),
        ("Core_ProfileWithCause", "Profile: {0} ({1})", "Profil : {0} ({1})"),
        ("Core_BusGain", "{0}: {1} dB", "{0} : {1} dB"),
        ("Core_StripMuted", "Input {0} muted", "Entrée {0} coupée"),
        ("Core_StripOn", "Input {0} on", "Entrée {0} rétablie"),
        ("Core_Routed", "{0} sent to {1}", "{0} envoyé vers {1}"),
        ("Core_RouteDefault", "{0} sent back to the default output", "{0} renvoyé vers la sortie par défaut"),
        ("Core_RouteUnavailable", "Sending to another output is unavailable", "Envoi vers une autre sortie indisponible"),
        ("Status_SaveFailed", "Could not save the setting.", "Le réglage n'a pas pu être enregistré."),
        ("Nav_Profiles", "Profiles", "Profils"),
        ("Nav_Rules", "Rules", "Règles"),
        ("Profiles_Empty", "No profiles yet.", "Aucun profil pour l'instant."),
        ("Profiles_Apply", "Apply", "Appliquer"),
        ("Rules_Empty", "No rules yet.", "Aucune règle pour l'instant."),
        ("Rules_Add", "Add rule", "Ajouter une règle"),
        ("Rules_Process", "Process name", "Nom du processus"),
        ("Rules_TriggerRunning", "When the process is running", "Quand le processus tourne"),
        ("Rules_TriggerForeground", "When the process is in front", "Quand le processus est devant"),
        ("Rules_ActionProfile", "Switch profile", "Changer de profil"),
        ("Rules_ActionMicOff", "Mute microphone", "Couper le micro"),
        ("Rules_ActionMicOn", "Unmute microphone", "Rétablir le micro"),
        ("Rules_ActionBus", "Set bus gain", "Régler le gain d'un bus"),
        ("Rules_ActionStrip", "Mute an input", "Couper une entrée"),
        ("Rules_ActionRoute", "Send the app to an output", "Envoyer l'application vers une sortie"),
        ("Rules_Revert", "Restore the previous value when the rule stops", "Rétablir la valeur précédente quand la règle s'arrête"),
        ("Rules_Save", "Save", "Enregistrer"),
        ("Rules_Delete", "Delete", "Supprimer"),
        ("Rules_Hint", "Type the application name without .exe.", "Indiquez le nom de l'application sans .exe."),
        ("Rules_Bus", "Bus", "Bus"),
        ("Rules_Gain", "Gain (dB)", "Gain (dB)"),
        ("Rules_Strip", "Input", "Entrée"),
        ("Rules_Endpoint", "Output id (empty = default)", "Identifiant de sortie (vide = défaut)"),
        ("Hotkey_Record", "Record", "Capturer"),
        ("Hotkey_Conflict", "This shortcut is already used.", "Ce raccourci est déjà utilisé."),
        ("Setup_Title", "First launch", "Premier lancement"),
        ("Setup_Body", "Mixdeck can copy the FL Studio script into Documents. Voicemeeter stays on Windows.", "Mixdeck peut copier le script FL Studio dans Documents. Voicemeeter reste sous Windows."),
        ("Setup_Copy", "Copy the FL Studio script", "Copier le script FL Studio"),
        ("Setup_Done", "Finish", "Terminer"),
        ("Setup_Copied", "Script copied.", "Script copié."),
        ("Setup_Missing", "The bundled script was not found.", "Le script livré est introuvable."),
        ("Apps_Search", "Search applications", "Chercher une application"),
    ];

    private static readonly Dictionary<string, int> Index = Entries
        .Select((entry, index) => (entry.Key, index))
        .ToDictionary(pair => pair.Key, pair => pair.index, StringComparer.Ordinal);

    public static bool UseFrench { get; private set; }

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    public static void Apply(string? language)
    {
        UseFrench = language switch
        {
            "fr-FR" => true,
            "en-US" => false,
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase),
        };
        Culture = CultureInfo.GetCultureInfo(UseFrench ? "fr-FR" : "en-US");
    }

    public static string Get(string key) =>
        Index.TryGetValue(key, out var index)
            ? UseFrench ? Entries[index].French : Entries[index].English
            : key;

    public static IReadOnlyList<string> Keys => Entries.Select(entry => entry.Key).ToArray();

    public static bool HasText(string key) =>
        Index.TryGetValue(key, out var index)
        && !string.IsNullOrWhiteSpace(Entries[index].English)
        && !string.IsNullOrWhiteSpace(Entries[index].French);
}

public sealed class DesktopTextProvider : ITextProvider
{
    public CultureInfo Culture => DesktopText.Culture;
    public string Get(string key) => DesktopText.Get(key);
}
