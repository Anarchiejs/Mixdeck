namespace Mixdeck.App.Services;

/// <summary>Pages de la console (fenêtre principale).</summary>
public enum ShellPage
{
    Mixer,
    Routing,
    Devices,
    FlStudio,
    Profiles,
    Rules,
    Settings,
    About,
}

/// <summary>Ouverture des fenêtres de l'appli, utilisable depuis n'importe quel ViewModel.</summary>
public interface IShellService
{
    void ShowMainWindow(ShellPage? page = null);

    /// <summary>
    /// Handle de la console ouverte (sélecteurs de fichiers), 0 si elle est fermée. La console est réellement
    /// fermée quand on la quitte : ne gardez jamais de référence à la fenêtre elle-même.
    /// </summary>
    nint MainWindowHandle { get; }

    void ToggleFlyout();

    void HideFlyout();

    void OpenLogsFolder();

    void Exit();
}
