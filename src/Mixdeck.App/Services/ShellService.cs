using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Mixdeck.App.Views.Flyout;
using Mixdeck.App.Views.Shell;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.Services;

/// <summary>Crée les fenêtres à la demande et les garde ouvertes (cachées) ensuite.</summary>
public sealed class ShellService(IServiceProvider services, MixdeckPaths paths, ILogger<ShellService> logger) : IShellService
{
    private MainWindow? _main;
    private FlyoutWindow? _flyout;

    /// <summary>Levé quand l'utilisateur choisit « Quitter » : l'appli libère tout puis s'arrête.</summary>
    public event EventHandler? ExitRequested;

    public void ShowMainWindow(ShellPage? page = null)
    {
        HideFlyout();
        _main ??= services.GetRequiredService<MainWindow>();
        _main.ShowPage(page);
    }

    public nint MainWindowHandle => _main is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(_main);

    public void ToggleFlyout()
    {
        _flyout ??= services.GetRequiredService<FlyoutWindow>();
        _flyout.Toggle();
    }

    public void HideFlyout() => _flyout?.Hide();

    public void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(paths.LogsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{paths.LogsDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Impossible d'ouvrir le dossier des journaux");
        }
    }

    public void Exit()
    {
        logger.LogInformation("Arrêt demandé par l'utilisateur");
        ExitRequested?.Invoke(this, EventArgs.Empty);
        Application.Current.Exit();
    }
}
