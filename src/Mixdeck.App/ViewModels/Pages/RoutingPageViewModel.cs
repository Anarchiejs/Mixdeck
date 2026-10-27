using CommunityToolkit.Mvvm.ComponentModel;
using Mixdeck.App.ViewModels.Shared;

namespace Mixdeck.App.ViewModels.Pages;

/// <summary>Les lignes et les colonnes proviennent du même état que la console.</summary>
public sealed partial class RoutingPageViewModel(MixerViewModel mixer) : ObservableObject
{
    public MixerViewModel Mixer { get; } = mixer;
}
