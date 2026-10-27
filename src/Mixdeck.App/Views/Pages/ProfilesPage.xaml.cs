using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.Views.Pages.Common;
using Mixdeck.App.Views.Shell;
using Windows.Storage.Pickers;

namespace Mixdeck.App.Views.Pages;

public sealed partial class ProfilesPage : Page
{
    public ProfilesPage()
    {
        ViewModel = App.GetService<ProfilesPageViewModel>();
        InitializeComponent();
        Loaded += (_, _) => { ViewModel.Mixer.Profiles.CollectionChanged += OnProfilesChanged; Bindings.Update(); };
        Unloaded += (_, _) => ViewModel.Mixer.Profiles.CollectionChanged -= OnProfilesChanged;
    }

    public ProfilesPageViewModel ViewModel { get; }
    public bool IsEmpty => ViewModel.Mixer.Profiles.Count == 0;

    private void OnProfilesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Bindings.Update();
    private void OnProfilePrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs e)
    {
        if (e.Element is ProfileCard card) card.ViewModel = ViewModel;
    }

    private async void OnCreateClick(object sender, RoutedEventArgs e) => await ViewModel.RunAsync(async () =>
    {
        var name = await PageDialogs.AskNameAsync(this, "Nouveau profil", "Créer");
        if (name is not null) await ViewModel.CaptureAsync(name);
    });

    private async void OnImportClick(object sender, RoutedEventArgs e) => await ViewModel.RunAsync(async () =>
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.GetService<MainWindow>()));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        await using var stream = await file.OpenStreamForReadAsync();
        await ViewModel.ImportAsync(stream);
    });
}
