using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Mixdeck.App.Views.Pages.Common;

/// <summary>Sélecteur commun : boutons natifs, libellés accessibles et navigation clavier.</summary>
public static class IconPicker
{
    private static readonly (string Glyph, string Name)[] Icons =
    [
        ("\uE720", Strings.Get("CS_104")), ("\uE7F6", Strings.Get("CS_105")), ("\uE767", Strings.Get("CS_106")),
        ("\uE7FC", Strings.Get("CS_107")), ("\uE8D6", Strings.Get("CS_010")), ("\uE8F2", Strings.Get("CS_108")),
        ("\uE774", Strings.Get("CS_109")), ("\uE714", Strings.Get("CS_110")), ("\uE8D3", Strings.Get("CS_111")),
        ("\uE765", Strings.Get("CS_112")), ("\uE7F4", Strings.Get("CS_113")), ("\uE8B7", Strings.Get("CS_114")),
        ("\uE93E", Strings.Get("CS_115")), ("\uE768", Strings.Get("CS_116")), ("\uE7C1", Strings.Get("CS_117")),
        ("\uE704", Strings.Get("CS_118")), ("\uE723", Strings.Get("CS_119")), ("\uE902", Strings.Get("CS_120")),
        ("\uE734", Strings.Get("CS_121")), ("\uE7EE", Strings.Get("CS_122")), ("\uE80F", Strings.Get("CS_123")),
        ("\uE708", Strings.Get("CS_124")), ("\uE945", Strings.Get("CS_125")), ("\uE713", Strings.Get("CS_014")),
    ];

    public static Microsoft.UI.Xaml.Controls.Flyout Create(Action<string> selected)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.Flyout { Placement = FlyoutPlacementMode.Bottom };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = Strings.Get("CS_126"), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        var grid = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
        for (var i = 0; i < 6; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < Icons.Length; i++)
        {
            var (glyph, name) = Icons[i];
            var button = new Button { Width = 40, Height = 40, Content = new FontIcon { Glyph = glyph, FontSize = 20 } };
            AutomationProperties.SetName(button, name);
            ToolTipService.SetToolTip(button, name);
            button.Click += (_, _) => { flyout.Hide(); selected(glyph); };
            Grid.SetRow(button, i / 6);
            Grid.SetColumn(button, i % 6);
            grid.Children.Add(button);
        }
        panel.Children.Add(grid);
        flyout.Content = panel;
        return flyout;
    }
}
