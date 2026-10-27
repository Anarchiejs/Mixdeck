using Mixdeck.App.Localization;
using Microsoft.UI.Xaml;

namespace Mixdeck.App.Controls;

internal static class ControlResources
{
    // Le dictionnaire reste local : la galerie fonctionne avant l'intégration dans App.xaml.
    internal static void Load(FrameworkElement owner)
    {
        owner.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("ms-appx:///Controls/Controls.xaml")
        });
    }

    internal static string Db(double value) =>
        value.ToString("0.0", Strings.Culture).Replace('-', '−') + " dB";

    internal static object Get(FrameworkElement owner, string key) =>
        owner.Resources.TryGetValue(key, out var value) ? value : Application.Current.Resources[key];
}
