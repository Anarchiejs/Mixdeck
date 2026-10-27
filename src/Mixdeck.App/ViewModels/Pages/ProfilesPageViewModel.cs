using Mixdeck.App.Localization;
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
                UnauthorizedAccessException => Strings.Get("CS_056"),
                System.Text.Json.JsonException => Strings.Get("CS_057"),
                IOException => Strings.Get("CS_058"),
                _ => Strings.Get("CS_059"),
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
        var baseName = item.Name + Strings.Get("CS_060");
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
