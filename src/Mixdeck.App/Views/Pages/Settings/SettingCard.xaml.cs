using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mixdeck.App.Views.Pages.Settings;

public sealed partial class SettingCard : UserControl
{
    public SettingCard() => InitializeComponent();
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata("\uE713"));
    public static readonly DependencyProperty ActionContentProperty = DependencyProperty.Register(nameof(ActionContent), typeof(UIElement), typeof(SettingCard), new PropertyMetadata(null));
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public UIElement? ActionContent { get => (UIElement?)GetValue(ActionContentProperty); set => SetValue(ActionContentProperty, value); }
}
