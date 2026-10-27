using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;

namespace Mixdeck.App.Controls;

public sealed class ChannelNameCommittedEventArgs(string name) : EventArgs
{
    public string Name { get; } = name;
}

public sealed class ChannelHeader : Control
{
    private TextBox? editor;
    private TextBlock? label;
    public static readonly DependencyProperty ChannelNameProperty = DependencyProperty.Register(nameof(ChannelName), typeof(string), typeof(ChannelHeader), new PropertyMetadata("Canal"));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(ChannelHeader), new PropertyMetadata("\uE8D6"));
    public string ChannelName { get => (string)GetValue(ChannelNameProperty); set => SetValue(ChannelNameProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public event TypedEventHandler<ChannelHeader, ChannelNameCommittedEventArgs>? NameCommitted;
    public ChannelHeader()
    {
        DefaultStyleKey = typeof(ChannelHeader); ControlResources.Load(this); IsTabStop = true;
        DoubleTapped += (_, e) => { Edit(); e.Handled = true; };
        KeyDown += OnKeyDown;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(this, "F2 ou double-clic pour renommer le canal");
    }
    protected override void OnApplyTemplate()
    {
        if (editor is not null) editor.LostFocus -= EditorLostFocus;
        base.OnApplyTemplate();
        editor = GetTemplateChild("PART_Editor") as TextBox;
        label = GetTemplateChild("PART_Label") as TextBlock;
        if (editor is not null) editor.LostFocus += EditorLostFocus;
    }
    private void EditorLostFocus(object sender, RoutedEventArgs e) => Finish(false);
    private void Edit()
    {
        if (editor is null || label is null) return;
        editor.Text = ChannelName; editor.Visibility = Visibility.Visible; label.Visibility = Visibility.Collapsed;
        editor.Focus(FocusState.Programmatic); editor.SelectAll();
    }
    private void Finish(bool commit)
    {
        if (editor is null || label is null || editor.Visibility != Visibility.Visible) return;
        string name = editor.Text.Trim();
        editor.Visibility = Visibility.Collapsed; label.Visibility = Visibility.Visible;
        if (commit && name.Length > 0) { ChannelName = name; NameCommitted?.Invoke(this, new(name)); }
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.F2) { Edit(); e.Handled = true; }
        else if (editor?.Visibility == Visibility.Visible && e.Key is VirtualKey.Enter or VirtualKey.Escape)
        { Finish(e.Key == VirtualKey.Enter); Focus(FocusState.Programmatic); e.Handled = true; }
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new HeaderPeer(this);
    private sealed class HeaderPeer(ChannelHeader owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ChannelHeader);
        protected override string GetNameCore() => string.IsNullOrEmpty(base.GetNameCore()) ? owner.ChannelName : base.GetNameCore();
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
    }
}
