using Mixdeck.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.Services;
using Mixdeck.Core.Diagnostics;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.ViewModels.Pages;

public sealed partial class AboutPageViewModel(IUpdateService updates, MixdeckPaths paths) : ObservableObject
{
    public string Version => Strings.Format("CS_Version", updates.CurrentVersion);
    public Uri RepositoryUri { get; } = new(UpdateService.RepositoryUrl);
    public Uri LicenseUri { get; } = new("https://opensource.org/license/mit");
    public bool IsInstalled => updates.IsInstalled;

    [ObservableProperty]
    public partial string Status { get; set; } = updates.IsInstalled
        ? Strings.Get("CS_026")
        : Strings.Get("CS_027");

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

    public string? RedactedCrashReport() => CrashReportCopy.ReadLatest(paths.LogsDirectory);
    public bool CanInstall => IsAvailable && !IsBusy;

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (!CanCheck) return;
        IsBusy = true;
        HasError = IsAvailable = HasNotes = false;
        Notes = string.Empty;
        Status = Strings.Get("CS_028");
        try
        {
            var result = await updates.CheckAsync();
            HasError = result.Error is not null;
            IsAvailable = result.IsAvailable && !HasError;
            Status = HasError ? Strings.Get("CS_029")
                : IsAvailable ? Strings.Format("CS_UpdateAvailable", result.Version)
                : Strings.Get("CS_030");
            Notes = IsAvailable ? result.NotesMarkdown ?? Strings.Get("CS_031") : string.Empty;
            HasNotes = IsAvailable;
        }
        catch (Exception)
        {
            HasError = true;
            Status = Strings.Get("CS_032");
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
        Status = Strings.Get("CS_033");
        try
        {
            await updates.DownloadAndRestartAsync(new Progress<int>(value =>
            {
                Progress = Math.Clamp(value, 0, 100);
                if (value >= 100) Status = Strings.Get("CS_034");
            }));
        }
        catch (Exception)
        {
            HasError = true;
            Status = Strings.Get("CS_035");
        }
        finally { IsBusy = IsInstalling = false; }
    }
}
