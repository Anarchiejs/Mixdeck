using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Pages;

/// <summary>Opérations de profils, sérialisées et protégées contre les erreurs de stockage.</summary>
public sealed partial class ProfilesPageViewModel(MixerViewModel mixer, IProfileService profiles) : ObservableObject
{
    public MixerViewModel Mixer { get; } = mixer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBusy { get; set; }

    public bool IsIdle => !IsBusy;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    public async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        HasError = false;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            ErrorMessage = ex switch
            {
                UnauthorizedAccessException => "L’accès au fichier est refusé. Choisissez un dossier accessible ou vérifiez les droits d’accès, puis réessayez.",
                System.Text.Json.JsonException => "Ce fichier n’est pas un profil Mixdeck valide. Choisissez un fichier .mixdeck-profile.json exporté depuis Mixdeck.",
                IOException => "Impossible de lire ou d’enregistrer le profil. Vérifiez que le fichier est disponible et que le disque dispose d’espace libre.",
                _ => "L’opération n’a pas abouti. Vérifiez le fichier et la connexion à Voicemeeter, puis réessayez.",
            };
            HasError = true;
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private Task ApplyAsync(ProfileItemViewModel item) =>
        RunAsync(() => Mixer.ApplyProfileCommand.ExecuteAsync(item));

    [RelayCommand]
    private Task DuplicateAsync(ProfileItemViewModel item) => RunAsync(async () =>
    {
        var baseName = item.Name + " — copie";
        var name = baseName;
        for (var n = 2; Mixer.Profiles.Any(p => p.Name == name); n++) name = $"{baseName} {n}";
        await Task.Run(() => profiles.DuplicateAsync(item.Id, name));
    });

    public async Task CaptureAsync(string name) => await Task.Run(() => profiles.CaptureCurrentAsync(name.Trim()));

    public Task SaveAsync(ProfileItemViewModel item, Func<Profile, Profile> change) =>
        RunAsync(() =>
        {
            var updated = change(item.Profile);
            return Task.Run(() => profiles.SaveAsync(updated));
        });

    public Task DeleteAsync(ProfileItemViewModel item) => Task.Run(() => profiles.DeleteAsync(item.Id));

    // Appelés dans RunAsync : le sélecteur et le flux font partie de la même opération protégée.
    public Task ExportAsync(ProfileItemViewModel item, Stream destination) => Task.Run(() => profiles.ExportAsync(item.Id, destination));

    public async Task ImportAsync(Stream source) => await Task.Run(() => profiles.ImportAsync(source));
}
