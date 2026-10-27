using Mixdeck.App.Localization;
using System.ComponentModel;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.ViewModels.Shared;
using Windows.Storage.Pickers;
using Windows.System;

namespace Mixdeck.App.Views.Pages.Common;

public sealed partial class ProfileCard : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(ProfileItemViewModel), typeof(ProfileCard), new PropertyMetadata(null, OnItemChanged));
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(nameof(ViewModel), typeof(ProfilesPageViewModel), typeof(ProfileCard), new PropertyMetadata(null, (d, _) => ((ProfileCard)d).Bindings.Update()));
    public ProfileItemViewModel Item { get => (ProfileItemViewModel)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public ProfilesPageViewModel ViewModel { get => (ProfilesPageViewModel)GetValue(ViewModelProperty); set => SetValue(ViewModelProperty, value); }
    public string CreatedText => Item is null ? string.Empty : Strings.Get("CS_129") + Item.Profile.CreatedAt.ToLocalTime().ToString("D", Strings.Culture);
    public bool IsNotRenaming => NameEditor.Visibility != Visibility.Visible;
    private bool _subscribed;
    private readonly Windows.UI.ViewManagement.AccessibilitySettings _accessibility = new();

    public ProfileCard()
    {
        InitializeComponent();
        Loaded += (_, _) => { Subscribe(); _accessibility.HighContrastChanged += OnHighContrastChanged; UpdateColor(); };
        Unloaded += (_, _) => { Unsubscribe(); _accessibility.HighContrastChanged -= OnHighContrastChanged; };
        ActualThemeChanged += (_, _) => UpdateColor();
    }

    private static void OnItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var card = (ProfileCard)sender;
        if (e.OldValue is ProfileItemViewModel old) old.PropertyChanged -= card.OnItemPropertyChanged;
        card._subscribed = false;
        if (card.IsLoaded) card.Subscribe();
        card.Bindings.Update();
        card.UpdateColor();
    }
    private void Subscribe() { if (!_subscribed && Item is not null) { Item.PropertyChanged += OnItemPropertyChanged; _subscribed = true; } }
    private void Unsubscribe() { if (_subscribed) { Item.PropertyChanged -= OnItemPropertyChanged; _subscribed = false; } }
    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ProfileItemViewModel.Color)) UpdateColor(); }
    private void UpdateColor()
    {
        if (ColorDot is null) return;
        var highContrast = _accessibility.HighContrast;
        var custom = !highContrast && ThemeService.TryParseColor(Item?.Color, out _);
        if (custom && ThemeService.TryParseColor(Item?.Color, out var color)) ColorDot.Fill = new SolidColorBrush(color);
        ColorDot.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        AccentDot.Visibility = custom ? Visibility.Collapsed : Visibility.Visible;
    }
    private void OnHighContrastChanged(Windows.UI.ViewManagement.AccessibilitySettings sender, object args) => DispatcherQueue.TryEnqueue(UpdateColor);

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        Add(Strings.Get("CS_099"), "\uE8AC", () => DispatcherQueue.TryEnqueue(() => { NameEditor.Text = Item.Name; NameEditor.Visibility = Visibility.Visible; Bindings.Update(); NameEditor.Focus(FocusState.Programmatic); NameEditor.SelectAll(); }));
        Add(Strings.Get("CS_130"), "\uE8C8", async () => await ViewModel.DuplicateCommand.ExecuteAsync(Item));
        Add(Strings.Get("CS_100"), "\uE790", () => DispatcherQueue.TryEnqueue(() => IconPicker.Create(async glyph => await ViewModel.SaveAsync(Item, p => p with { Icon = glyph })).ShowAt(MoreButton)));
        Add(Strings.Get("CS_131"), "\uE2B1", () => DispatcherQueue.TryEnqueue(ShowColorPicker));
        Add(Strings.Get("CS_132"), "\uEDE1", Export);
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(Strings.Get("CS_097"), "\uE74D", Delete);
        menu.ShowAt(MoreButton);
        void Add(string text, string glyph, Action action)
        {
            var entry = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
            entry.Click += (_, _) => action();
            menu.Items.Add(entry);
        }
    }

    private async void OnNameKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape) { FinishRename(); e.Handled = true; }
        else if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            var name = NameEditor.Text.Trim();
            if (name.Length == 0) return;
            FinishRename();
            await ViewModel.SaveAsync(Item, p => p with { Name = name });
        }
    }
    private void OnNameLostFocus(object sender, RoutedEventArgs e) => FinishRename(false);
    private void FinishRename(bool focus = true)
    {
        if (NameEditor.Visibility != Visibility.Visible) return;
        NameEditor.Visibility = Visibility.Collapsed; Bindings.Update();
        if (focus) MoreButton.Focus(FocusState.Programmatic);
    }

    private void ShowColorPicker()
    {
        var picker = new ColorPicker { IsAlphaEnabled = false, IsMoreButtonVisible = false, IsColorSliderVisible = true, IsColorChannelTextInputVisible = true, MaxWidth = 300 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(picker, Strings.Get("CS_133"));
        if (ThemeService.TryParseColor(Item.Color, out var color)) picker.Color = color;
        else picker.Color = ((SolidColorBrush)Application.Current.Resources["AccentFillColorDefaultBrush"]).Color;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = Strings.Get("CS_133"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        panel.Children.Add(picker);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var accent = new Button { Content = Strings.Get("CS_134") };
        var save = new Button { Content = Strings.Get("CS_095") };
        actions.Children.Add(accent); actions.Children.Add(save); panel.Children.Add(actions);
        var flyout = new Microsoft.UI.Xaml.Controls.Flyout { Content = panel };
        accent.Click += async (_, _) => { flyout.Hide(); await ViewModel.SaveAsync(Item, p => p with { Color = null }); };
        save.Click += async (_, _) =>
        {
            var selected = picker.Color;
            flyout.Hide();
            await ViewModel.SaveAsync(Item, p => p with { Color = $"#{selected.R:X2}{selected.G:X2}{selected.B:X2}" });
        };
        flyout.ShowAt(MoreButton);
    }

    private async void Export() => await ViewModel.RunAsync(async () =>
    {
        var picker = new FileSavePicker { SuggestedFileName = SafeFileName(Item.Name), SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add(Strings.Get("CS_135"), new List<string> { ".mixdeck-profile.json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.GetService<IShellService>().MainWindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        await using var stream = await file.OpenStreamForWriteAsync();
        stream.SetLength(0);
        await ViewModel.ExportAsync(Item, stream);
    });

    private static string SafeFileName(string name)
    {
        var clean = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim().TrimEnd('.');
        return string.IsNullOrEmpty(clean) ? Strings.Get("CS_004") : clean;
    }

    private async void Delete()
    {
        await ViewModel.RunAsync(async () =>
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, RequestedTheme = ActualTheme, Title = Strings.Get("CS_136"),
                Content = Strings.Format("CS_DeleteProfile", Item.Name),
                PrimaryButtonText = Strings.Get("CS_097"), CloseButtonText = Strings.Get("CS_086"), DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                await ViewModel.DeleteAsync(Item);
        });
    }
}
