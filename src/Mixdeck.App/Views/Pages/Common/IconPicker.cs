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
        ("\uE720", "Microphone"), ("\uE7F6", "Casque"), ("\uE767", "Haut-parleur"),
        ("\uE7FC", "Jeu"), ("\uE8D6", "Musique"), ("\uE8F2", "Conversation"),
        ("\uE774", "Navigateur"), ("\uE714", "Film"), ("\uE8D3", "Guitare"),
        ("\uE765", "Synthétiseur"), ("\uE7F4", "Ordinateur"), ("\uE8B7", "Téléphone"),
        ("\uE93E", "Enregistrement"), ("\uE768", "Lecture"), ("\uE7C1", "Diffusion"),
        ("\uE704", "Réseau"), ("\uE723", "Bureau"), ("\uE902", "Vidéo"),
        ("\uE734", "Favori"), ("\uE7EE", "Travail"), ("\uE80F", "Maison"),
        ("\uE708", "Nuit"), ("\uE945", "Écoute"), ("\uE713", "Réglages"),
    ];

    public static Microsoft.UI.Xaml.Controls.Flyout Create(Action<string> selected)
    {
        var flyout = new Microsoft.UI.Xaml.Controls.Flyout { Placement = FlyoutPlacementMode.Bottom };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Choisir une icône", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
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
