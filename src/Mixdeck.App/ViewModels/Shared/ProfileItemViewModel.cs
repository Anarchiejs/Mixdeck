using CommunityToolkit.Mvvm.ComponentModel;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>Un profil tel qu'affiché dans les pastilles du panneau et la page Profils.</summary>
public sealed partial class ProfileItemViewModel(Profile profile) : ObservableObject
{
    public Profile Profile { get; private set; } = profile;

    public string Id => Profile.Id;

    public string Name => Profile.Name;

    public string Icon => Profile.Icon;

    /// <summary>"#RRGGBB" ou null (couleur d'accent).</summary>
    public string? Color => Profile.Color;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    public void Update(Profile updated)
    {
        Profile = updated;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Color));
    }
}
