namespace Mixdeck.App.Services;

/// <summary>Pages de la console (fenêtre principale).</summary>
public enum ShellPage
{
    Mixer,
    Routing,
    Profiles,
    Rules,
    Settings,
    About,
}

/// <summary>Ouverture des fenêtres de l'appli, utilisable depuis n'importe quel ViewModel.</summary>
public interface IShellService
{
    void ShowMainWindow(ShellPage? page = null);

    void ToggleFlyout();

    void HideFlyout();

    void OpenLogsFolder();

    void Exit();
}
