using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Pages;

namespace Mixdeck.App.Views.Pages.Settings;

public sealed partial class RuleEditor : UserControl
{
    private readonly string[] processes;
    public RuleEditor(RuleEditorViewModel viewModel, string[] processes)
    {
        ViewModel = viewModel;
        this.processes = processes;
        InitializeComponent();
    }
    public RuleEditorViewModel ViewModel { get; }
    private void OnProcessChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            sender.ItemsSource = processes.Where(p => p.Contains(sender.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Take(20).ToArray();
    }
    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args) => sender.Text = (string)args.SelectedItem;
}
