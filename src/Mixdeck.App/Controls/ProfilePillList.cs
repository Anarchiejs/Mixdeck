using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Mixdeck.App.Controls;

public sealed class ProfilePillInvokedEventArgs(object item) : EventArgs
{
    public object Item { get; } = item;
}

public sealed class ProfilePillList : Control
{
    private ListView? list;
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(object), typeof(ProfilePillList), new PropertyMetadata(null));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(object), typeof(ProfilePillList), new PropertyMetadata(null, (d, _) => ((ProfilePillList)d).SyncSelection()));
    public object? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public object? SelectedItem { get => GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public event TypedEventHandler<ProfilePillList, ProfilePillInvokedEventArgs>? ItemInvoked;
    public ProfilePillList() { DefaultStyleKey = typeof(ProfilePillList); ControlResources.Load(this); }
    protected override void OnApplyTemplate()
    {
        if (list is not null) { list.ItemClick -= Invoked; list.SelectionChanged -= SelectionChanged; list.PreviewKeyDown -= Navigate; }
        base.OnApplyTemplate();
        list = GetTemplateChild("PART_List") as ListView;
        if (list is not null) { list.ItemClick += Invoked; list.SelectionChanged += SelectionChanged; list.PreviewKeyDown += Navigate; SyncSelection(); }
    }
    private void SyncSelection() { if (list is not null) list.SelectedItem = SelectedItem; }
    private void Invoked(object sender, ItemClickEventArgs e)
    {
        ItemInvoked?.Invoke(this, new ProfilePillInvokedEventArgs(e.ClickedItem));
        SyncSelection();
    }
    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Différé pour laisser ListView terminer son traitement du clic.
        DispatcherQueue.TryEnqueue(SyncSelection);
    }
    private void Navigate(object sender, KeyRoutedEventArgs e)
    {
        if (list is null || list.Items.Count == 0) return;
        int index = Math.Max(0, list.Items.IndexOf(SelectedItem));
        if (FocusManager.GetFocusedElement(XamlRoot) is ListViewItem focused)
            index = list.IndexFromContainer(focused);
        index = e.Key switch
        {
            VirtualKey.Right => Math.Min(list.Items.Count - 1, index + 1),
            VirtualKey.Left => Math.Max(0, index - 1),
            VirtualKey.Home => 0,
            VirtualKey.End => list.Items.Count - 1,
            VirtualKey.Enter or VirtualKey.Space => index,
            _ => -1
        };
        if (index < 0) return;
        object item = list.Items[index];
        list.ScrollIntoView(item);
        (list.ContainerFromItem(item) as ListViewItem)?.Focus(FocusState.Keyboard);
        ItemInvoked?.Invoke(this, new ProfilePillInvokedEventArgs(item));
        SyncSelection();
        e.Handled = true;
    }
}
