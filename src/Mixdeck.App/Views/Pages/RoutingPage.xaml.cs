using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Mixdeck.App.Controls;
using Mixdeck.App.ViewModels.Pages;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Pages.Common;
using Windows.System;

namespace Mixdeck.App.Views.Pages;

public sealed partial class RoutingPage : Page
{
    public RoutingPage()
    {
        ViewModel = App.GetService<RoutingPageViewModel>();
        InitializeComponent();
        BuildMatrix();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public RoutingPageViewModel ViewModel { get; }
    public bool IsDisconnected => !ViewModel.Mixer.IsConnected;
    private readonly RouteButton[,] _buttons = new RouteButton[5, 5];
    private readonly Border[] _rows = new Border[5];
    private readonly Border[] _columns = new Border[5];

    private void BuildMatrix()
    {
        Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(168) });
        Matrix.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < 5; i++)
        {
            Matrix.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80, GridUnitType.Star), MinWidth = 76 });
            Matrix.RowDefinitions.Add(new RowDefinition { Height = new GridLength(64) });
            if (ViewModel.Mixer.Buses[i].IsVirtual)
            {
                var backdrop = (Border)((DataTemplate)Resources["VirtualColumn"]).LoadContent();
                Grid.SetColumn(backdrop, i + 1); Grid.SetRowSpan(backdrop, 6);
                Matrix.Children.Add(backdrop);
            }
            var column = (Border)((DataTemplate)Resources["MatrixHighlight"]).LoadContent();
            Grid.SetColumn(column, i + 1); Grid.SetRowSpan(column, 6);
            Matrix.Children.Add(column); _columns[i] = column;
            var row = (Border)((DataTemplate)Resources["MatrixHighlight"]).LoadContent();
            Grid.SetRow(row, i + 1); Grid.SetColumnSpan(row, 6);
            Matrix.Children.Add(row); _rows[i] = row;
        }
        Put(new TextBlock { Text = "Entrées / Sorties", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center }, 0, 0);
        for (var bus = 0; bus < 5; bus++)
        {
            var output = ViewModel.Mixer.Buses[bus];
            var header = new StackPanel { Spacing = 4, Margin = new Thickness(4, 12, 4, 12), HorizontalAlignment = HorizontalAlignment.Center };
            header.Children.Add(new TextBlock { Text = output.BusName, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], HorizontalAlignment = HorizontalAlignment.Center });
            header.Children.Add(new TextBlock { Tag = output, Text = output.DisplayName, MaxWidth = 100, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
            var kind = (TextBlock)((DataTemplate)Resources["ColumnKind"]).LoadContent();
            kind.Text = output.IsVirtual ? "Virtuelle" : "Physique";
            header.Children.Add(kind);
            Put(header, 0, bus + 1);
        }
        for (var strip = 0; strip < 5; strip++)
        {
            var input = ViewModel.Mixer.Strips[strip];
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new FontIcon { Glyph = input.Icon, FontSize = 20, Tag = input });
            label.Children.Add(new TextBlock { Text = input.DisplayName, Tag = input, MaxWidth = 128, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            Put(label, strip + 1, 0);
            for (var bus = 0; bus < 5; bus++)
            {
                var route = input.Routes[bus];
                var cell = new RoutingCell { Route = route, VerticalAlignment = VerticalAlignment.Center };
                var button = cell.Button;
                button.Loaded += (_, _) => UpdateAccessibleName(button, route);
                button.PointerEntered += (_, _) => Highlight(route.StripIndex, route.BusIndex);
                button.PointerExited += (_, _) => HighlightFocused();
                button.GotFocus += (_, _) => Highlight(route.StripIndex, route.BusIndex);
                button.LostFocus += (_, _) => Highlight(-1, -1);
                button.PreviewKeyDown += OnCellKeyDown;
                Put(cell, strip + 1, bus + 1); _buttons[strip, bus] = button;
            }
        }
        ActualThemeChanged += (_, _) => HighlightFocused();
    }

    private void Put(FrameworkElement child, int row, int column) { Grid.SetRow(child, row); Grid.SetColumn(child, column); Matrix.Children.Add(child); }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Mixer.PropertyChanged += OnMixerChanged;
        foreach (var channel in ViewModel.Mixer.Strips.Cast<ChannelViewModel>().Concat(ViewModel.Mixer.Buses)) channel.PropertyChanged += OnChannelChanged;
        RefreshLabels();
        Bindings.Update();
        HighlightFocused();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.Mixer.PropertyChanged -= OnMixerChanged;
        foreach (var channel in ViewModel.Mixer.Strips.Cast<ChannelViewModel>().Concat(ViewModel.Mixer.Buses)) channel.PropertyChanged -= OnChannelChanged;
    }
    private void OnMixerChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(MixerViewModel.IsConnected)) Bindings.Update(); }
    private void OnChannelChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(ChannelViewModel.DisplayName) or nameof(ChannelViewModel.Icon)) RefreshLabels(); }
    private void RefreshLabels()
    {
        foreach (var panel in Matrix.Children.OfType<StackPanel>())
            foreach (var child in panel.Children.OfType<FrameworkElement>())
                if (child.Tag is ChannelViewModel channel)
                {
                    if (child is TextBlock text) { text.Text = channel.DisplayName; ToolTipService.SetToolTip(text, channel.DisplayName); }
                    if (child is FontIcon icon) icon.Glyph = channel.Icon;
                }
        foreach (var button in _buttons) UpdateAccessibleName(button, (RouteViewModel)button.Tag);
    }
    private void UpdateAccessibleName(RouteButton button, RouteViewModel route) => AutomationProperties.SetName(button,
        $"{ViewModel.Mixer.Strips[route.StripIndex].DisplayName} vers {route.BusName}, {ViewModel.Mixer.Buses[route.BusIndex].DisplayName}");

    private void Highlight(int row, int column)
    {
        for (var i = 0; i < 5; i++)
        {
            _rows[i].Opacity = i == row ? 1 : 0;
            _columns[i].Opacity = i == column ? 1 : 0;
        }
    }
    private void HighlightFocused()
    {
        if (XamlRoot is not null && FocusManager.GetFocusedElement(XamlRoot) is RouteButton { Tag: RouteViewModel route }) Highlight(route.StripIndex, route.BusIndex);
        else Highlight(-1, -1);
    }
    private void OnCellKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var route = (RouteViewModel)((RouteButton)sender).Tag;
        var row = route.StripIndex; var column = route.BusIndex;
        switch (e.Key)
        {
            case VirtualKey.Left: column = Math.Max(0, column - 1); break;
            case VirtualKey.Right: column = Math.Min(4, column + 1); break;
            case VirtualKey.Up: row = Math.Max(0, row - 1); break;
            case VirtualKey.Down: row = Math.Min(4, row + 1); break;
            case VirtualKey.Home: column = 0; break;
            case VirtualKey.End: column = 4; break;
            default: return;
        }
        _buttons[row, column].Focus(FocusState.Keyboard);
        e.Handled = true;
    }
}
