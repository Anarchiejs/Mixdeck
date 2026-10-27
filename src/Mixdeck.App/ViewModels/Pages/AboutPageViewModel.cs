using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.Services;

namespace Mixdeck.App.ViewModels.Pages;

public sealed partial class AboutPageViewModel(IUpdateService updates) : ObservableObject
{
    public string Version => $"Version {updates.CurrentVersion}";
    public Uri RepositoryUri { get; } = new(UpdateService.RepositoryUrl);
    public Uri LicenseUri { get; } = new("https://opensource.org/license/mit");
    public bool IsInstalled => updates.IsInstalled;

    [ObservableProperty]
    public partial string Status { get; set; } = updates.IsInstalled
        ? "Recherchez une nouvelle version de Mixdeck."
        : "Mises à jour désactivées en développement.";

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck), nameof(CanInstall))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    public partial bool IsAvailable { get; set; }

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial bool IsInstalling { get; set; }

    public bool CanCheck => IsInstalled && !IsBusy;
    public bool CanInstall => IsAvailable && !IsBusy;

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (!CanCheck) return;
        IsBusy = true;
        HasError = IsAvailable = HasNotes = false;
        Notes = string.Empty;
        Status = "Recherche d’une mise à jour…";
        try
        {
            var result = await updates.CheckAsync();
            HasError = result.Error is not null;
            IsAvailable = result.IsAvailable && !HasError;
            Status = HasError ? "La recherche a échoué. Réessayez plus tard."
                : IsAvailable ? $"La version {result.Version} est disponible."
                : "Mixdeck est à jour.";
            Notes = IsAvailable ? result.NotesMarkdown ?? "Aucune note de version fournie." : string.Empty;
            HasNotes = IsAvailable;
        }
        catch (Exception)
        {
            HasError = true;
            Status = "Impossible de rechercher les mises à jour. Réessayez plus tard.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!CanInstall) return;
        IsBusy = IsInstalling = true;
        HasError = false;
        Progress = 0;
        Status = "Téléchargement de la mise à jour…";
        try
        {
            await updates.DownloadAndRestartAsync(new Progress<int>(value =>
            {
                Progress = Math.Clamp(value, 0, 100);
                if (value >= 100) Status = "Installation et redémarrage…";
            }));
        }
        catch (Exception)
        {
            HasError = true;
            Status = "L’installation a échoué. Vous pouvez réessayer.";
        }
        finally { IsBusy = IsInstalling = false; }
    }
}
