using Mixdeck.App.Localization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Pages.Settings;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.Views.Pages;

public sealed partial class SettingsPage : Page
{
    private bool listening;
    private bool dialogOpen;
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsPageViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.HasError) && ViewModel.HasError)
                DispatcherQueue.TryEnqueue(() => PageError.StartBringIntoView());
        };
    }
    public SettingsPageViewModel ViewModel { get; }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Refresh();
        if (listening) return;
        listening = true;
        App.GetService<ISettingsStore>().Changed += OnSettingsChanged;
        ViewModel.Mixer.PropertyChanged += OnMixerChanged;
    }
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        listening = false;
        App.GetService<ISettingsStore>().Changed -= OnSettingsChanged;
        ViewModel.Mixer.PropertyChanged -= OnMixerChanged;
    }
    private void OnMixerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerViewModel.Layout)) ViewModel.Refresh();
    }
    private void OnSettingsChanged(object? sender, AppSettings settings) => DispatcherQueue.TryEnqueue(ViewModel.RefreshHotkeyWarnings);
    private async void OnWindowsAccent(object sender, RoutedEventArgs e) => await ViewModel.SetAccentAsync(null);
    private async void OnCustomAccent(object sender, RoutedEventArgs e)
    {
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var picker = new ColorPicker
            {
                Language = Strings.Language,
                Color = ThemeService.TryParseColor(ViewModel.AccentColor, out var color) ? color : Colors.DodgerBlue,
                IsAlphaEnabled = false,
                IsAlphaSliderVisible = false,
                IsAlphaTextInputVisible = false,
                IsMoreButtonVisible = false,
                MinWidth = 280,
                MaxWidth = 400,
            };
            var error = new InfoBar { Severity = InfoBarSeverity.Error, IsClosable = false, Title = Strings.Get("Settings_CannotSave"), Message = Strings.Get("Settings_CheckData") };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(error);
            content.Children.Add(picker);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Language = Strings.Language,
                Title = Strings.Get("Settings_AccentTitle"),
                Content = new ScrollViewer { Content = content, HorizontalScrollMode = ScrollMode.Disabled },
                PrimaryButtonText = Strings.Get("Settings_Save"),
                CloseButtonText = Strings.Get("Settings_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
            };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    var selected = picker.Color;
                    args.Cancel = !await ViewModel.SetAccentAsync($"#{selected.R:X2}{selected.G:X2}{selected.B:X2}");
                    error.IsOpen = args.Cancel;
                }
                finally { deferral.Complete(); }
            };
            await dialog.ShowAsync();
        }
        catch (Exception) { ViewModel.ReportError(Strings.Get("Settings_AccentError")); }
        finally { dialogOpen = false; }
    }

    private async void OnEditHotkey(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not HotkeyRowViewModel row) return;
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            await ViewModel.FlushAsync();
            var recorder = new HotkeyRecorder(row.Action, row.TargetBus, (float)row.StepDb, ViewModel.Conflicts, ViewModel.ProfileChoices, row.TargetProfile?.Id,
                row.Binding?.DawTrack ?? 0, row.Binding?.DawSlot ?? 0);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Language = Strings.Language,
                Title = row.Name,
                Content = recorder,
                PrimaryButtonText = Strings.Get("Settings_Save"),
                CloseButtonText = Strings.Get("Settings_Cancel"),
                DefaultButton = ContentDialogButton.None,
                IsPrimaryButtonEnabled = false,
            };
            recorder.Captured += (_, _) => dialog.IsPrimaryButtonEnabled = recorder.Value is not null;
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    args.Cancel = recorder.Value is null || !await ViewModel.SaveHotkeyAsync(row, recorder.Value);
                    if (args.Cancel) recorder.ShowError(Strings.Get("Settings_HotkeySaveError"));
                }
                finally { deferral.Complete(); }
            };
            try
            {
                ViewModel.SuspendHotkeys();
                await dialog.ShowAsync();
            }
            catch (Exception) { ViewModel.ReportError(Strings.Get("Settings_HotkeyOpenError")); }
            finally
            {
                try { ViewModel.RestoreHotkeys(); }
                catch (Exception) { ViewModel.ReportError(Strings.Get("Settings_HotkeyRestoreError")); }
            }
        }
        catch (Exception) { ViewModel.ReportError(Strings.Get("Settings_HotkeyOpenError")); }
        finally { dialogOpen = false; }
    }
    private async void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is HotkeyRowViewModel row) await ViewModel.SaveHotkeyAsync(row, null);
    }
    private async void OnVisibilityToggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { IsLoaded: true, DataContext: ChannelViewModel channel } toggle) return;
        var store = App.GetService<ISettingsStore>();
        var isBus = channel is BusViewModel;
        var current = isBus ? store.Current.Buses : store.Current.Strips;
        var existing = current.FirstOrDefault(c => c.Index == channel.Index);
        var previous = existing?.ShowInFlyout ?? (!isBus || channel.Index == 0);
        var visible = toggle.IsOn;
        if (previous == visible) return;
        var success = await ViewModel.SaveAsync(s =>
        {
            var list = isBus ? s.Buses : s.Strips;
            var saved = list.FirstOrDefault(c => c.Index == channel.Index) ?? new ChannelCustomization(channel.Index);
            var updated = list.Where(c => c.Index != channel.Index).Append(saved with { ShowInFlyout = visible }).ToArray();
            return isBus ? s with { Buses = updated } : s with { Strips = updated };
        });
        if (!success)
        {
            var (name, icon) = channel.GetCustomization();
            channel.ApplyCustomization(name, icon, previous);
        }
    }
}
