using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.Views.Pages.Settings;

namespace Mixdeck.App.Views.Pages;

public sealed partial class RulesPage : Page
{
    private bool dialogOpen;
    public RulesPage()
    {
        ViewModel = App.GetService<RulesPageViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.HasError) && ViewModel.HasError)
                DispatcherQueue.TryEnqueue(() => PageError.StartBringIntoView());
        };
    }

    public RulesPageViewModel ViewModel { get; }
    private void OnLoaded(object sender, RoutedEventArgs e) => ViewModel.Refresh();
    private void OnCreate(object sender, RoutedEventArgs e) => UiAsync.Run(() => EditAsync(null));
    private void OnEdit(object sender, RoutedEventArgs e) => UiAsync.Run(() => EditAsync((sender as FrameworkElement)?.DataContext as RuleCardViewModel));
    private void OnMoveUp(object sender, RoutedEventArgs e) => UiAsync.Run(() => ViewModel.MoveUpCommand.ExecuteAsync((sender as FrameworkElement)?.DataContext));
    private void OnMoveDown(object sender, RoutedEventArgs e) => UiAsync.Run(() => ViewModel.MoveDownCommand.ExecuteAsync((sender as FrameworkElement)?.DataContext));

    private async Task EditAsync(RuleCardViewModel? item)
    {
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var editorModel = ViewModel.CreateEditor(item?.Rule);
            var editor = new RuleEditor(editorModel, await ViewModel.GetProcessesAsync());
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Language = Strings.Language,
                Title = item is null ? Strings.Get("CS_093") : Strings.Get("CS_094"),
                Content = editor,
                PrimaryButtonText = Strings.Get("CS_095"),
                CloseButtonText = Strings.Get("CS_086"),
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = editorModel.CanSave,
            };
            editorModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(RuleEditorViewModel.CanSave))
                    dialog.IsPrimaryButtonEnabled = editorModel.CanSave;
            };
            dialog.PrimaryButtonClick += async (_, args) =>
            {
                var deferral = args.GetDeferral();
                try
                {
                    args.Cancel = true;
                    if (!editorModel.CanSave) return;
                    editor.IsEnabled = false;
                    dialog.IsPrimaryButtonEnabled = false;
                    editorModel.HasError = !await ViewModel.SaveRuleAsync(editorModel.Build());
                    args.Cancel = editorModel.HasError;
                }
                finally
                {
                    editor.IsEnabled = true;
                    dialog.IsPrimaryButtonEnabled = editorModel.CanSave;
                    deferral.Complete();
                }
            };
            await dialog.ShowAsync();
        }
        catch (Exception) { ViewModel.HasError = true; }
        finally { dialogOpen = false; }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleCardViewModel item) return;
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Language = Strings.Language,
                Title = Strings.Get("CS_096"),
                Content = new TextBlock { Text = Strings.Format("CS_DeleteRule", item.Name), TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Strings.Get("CS_097"),
                CloseButtonText = Strings.Get("CS_086"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.DeleteAsync(item);
        }
        catch (Exception) { ViewModel.HasError = true; }
        finally { dialogOpen = false; }
    }
}
