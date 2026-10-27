using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Mixdeck.App.Controls;
using Mixdeck.App.ViewModels.Shared;
using Windows.System;

namespace Mixdeck.App.Views.Pages.Common;

public sealed partial class ChannelCard : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ChannelViewModel), typeof(ChannelCard), new PropertyMetadata(null, OnModelChanged));
    public ChannelViewModel Model { get => (ChannelViewModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public StripViewModel Strip => (Model as StripViewModel)!;
    public BusViewModel Bus => (Model as BusViewModel)!;
    public bool IsStrip => Model is StripViewModel;
    public bool IsBus => Model is BusViewModel;
    public bool HasEq => Strip?.HasEq == true;
    public string KindText => Model switch { StripViewModel s => s.KindText, BusViewModel b => b.BusName + " · " + (b.IsVirtual ? Strings.Get("CS_098") : Strings.Get("CS_092")), _ => string.Empty };

    public ChannelCard() { InitializeComponent(); }

    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var card = (ChannelCard)sender;
        card.Bindings.Update();
        card.Card.ContextFlyout = args.NewValue is ChannelViewModel ? card.CreateMenu() : null;
    }

    private void OnNameCommitted(ChannelHeader sender, ChannelNameCommittedEventArgs args) => Model.DisplayName = args.Name;
    private void OnPanDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) { Strip.PanX = 0; e.Handled = true; }
    private void OnIconClick(object sender, RoutedEventArgs e) => ShowIcon((FrameworkElement)sender);
    private void ShowIcon(FrameworkElement target) => IconPicker.Create(glyph => Model.Icon = glyph).ShowAt(target);
    private void OnMenuClick(object sender, RoutedEventArgs e) => CreateMenu().ShowAt((FrameworkElement)sender);

    private MenuFlyout CreateMenu()
    {
        var menu = new MenuFlyout();
        var rename = new MenuFlyoutItem { Text = Strings.Get("CS_099"), Icon = new FontIcon { Glyph = "\uE8AC" } };
        rename.Click += (_, _) => Rename();
        menu.Items.Add(rename);
        var icon = new MenuFlyoutItem { Text = Strings.Get("CS_100"), Icon = new FontIcon { Glyph = "\uE790" } };
        icon.Click += (_, _) => DispatcherQueue.TryEnqueue(() => ShowIcon(Header));
        menu.Items.Add(icon);
        var show = new ToggleMenuFlyoutItem { Text = Strings.Get("CS_101"), IsChecked = Model.ShowInFlyout };
        menu.Opening += (_, _) => show.IsChecked = Model.ShowInFlyout;
        show.Click += (_, _) => Model.ShowInFlyout = show.IsChecked;
        menu.Items.Add(show);
        menu.Items.Add(new MenuFlyoutSeparator());
        var reset = new MenuFlyoutItem { Text = Strings.Get("CS_102") };
        reset.Click += (_, _) => Model.DisplayName = string.Empty;
        menu.Items.Add(reset);
        return menu;
    }

    private void Rename()
    {
        // Édition ancrée sur l’en-tête, sans quitter la tranche.
        var editor = new TextBox { Text = Model.DisplayName, Header = Strings.Get("CS_103"), MaxLength = 100, Width = 240 };
        var save = new Button { Content = Strings.Get("CS_099"), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(editor); panel.Children.Add(save);
        var flyout = new Microsoft.UI.Xaml.Controls.Flyout { Content = panel };
        void Commit() { Model.DisplayName = editor.Text.Trim(); flyout.Hide(); }
        save.Click += (_, _) => Commit();
        editor.KeyDown += (_, e) => { if (e.Key == VirtualKey.Enter) { Commit(); e.Handled = true; } };
        flyout.Opened += (_, _) => { editor.Focus(FocusState.Programmatic); editor.SelectAll(); };
        DispatcherQueue.TryEnqueue(() => flyout.ShowAt(Header));
    }
}
