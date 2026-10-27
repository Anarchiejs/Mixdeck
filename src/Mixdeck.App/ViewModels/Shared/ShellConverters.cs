using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>Fonctions x:Bind qui renvoient des objets d'interface (pinceaux).</summary>
public static class ShellConverters
{
    public static Brush ConnectionBrush(bool connected) =>
        (Brush)Application.Current.Resources[connected ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush"];

    public static string ConnectionShort(bool connected) => connected ? "Connecté" : "Hors ligne";
}
