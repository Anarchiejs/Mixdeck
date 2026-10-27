using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Mixdeck.App.Controls;

public sealed class MuteToggle : ToggleButton
{
    public static readonly DependencyProperty GlyphOnProperty = DependencyProperty.Register(nameof(GlyphOn), typeof(string), typeof(MuteToggle), new PropertyMetadata("\uE767", Changed));
    public static readonly DependencyProperty GlyphOffProperty = DependencyProperty.Register(nameof(GlyphOff), typeof(string), typeof(MuteToggle), new PropertyMetadata("\uE74F", Changed));
    public string GlyphOn { get => (string)GetValue(GlyphOnProperty); set => SetValue(GlyphOnProperty, value); }
    public string GlyphOff { get => (string)GetValue(GlyphOffProperty); set => SetValue(GlyphOffProperty, value); }
    public MuteToggle()
    {
        DefaultStyleKey = typeof(MuteToggle);
        ControlResources.Load(this);
        RegisterPropertyChangedCallback(IsCheckedProperty, (_, _) => UpdateGlyph());
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, Strings.Get("CS_003"));
    }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MuteToggle)d).UpdateGlyph();
    protected override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateGlyph(); }
    private void UpdateGlyph()
    {
        if (GetTemplateChild("PART_Glyph") is FontIcon icon) icon.Glyph = IsChecked == true ? GlyphOff : GlyphOn;
    }
}
