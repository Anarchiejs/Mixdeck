using Mixdeck.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.ViewModels.Shared;

namespace Mixdeck.App.ViewModels.Pages;

/// <summary>La console utilise directement les canaux du mixeur partagé.</summary>
public sealed partial class MixerPageViewModel(MixerViewModel mixer) : ObservableObject
{
    public MixerViewModel Mixer { get; } = mixer;

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasError { get; set; }

    [RelayCommand]
    private async Task ApplyProfileAsync(ProfileItemViewModel? profile)
    {
        try { await Mixer.ApplyProfileCommand.ExecuteAsync(profile); }
        catch (Exception) { ShowError(Strings.Get("CS_054")); }
    }

    [RelayCommand]
    private async Task LaunchVoicemeeterAsync()
    {
        try { await Mixer.LaunchVoicemeeterCommand.ExecuteAsync(null); }
        catch (Exception) { ShowError(Strings.Get("CS_055")); }
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }
}
