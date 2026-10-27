using Microsoft.UI;
using Microsoft.UI.Xaml;
using Mixdeck.Core.Settings;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Mixdeck.App.Services;

/// <summary>Thème clair / sombre / système et couleur d'accent, appliqués à toutes les fenêtres ouvertes.</summary>
public sealed class ThemeService
{
    private readonly List<WeakReference<Window>> _windows = [];
    private readonly UISettings _uiSettings = new();
    private ThemeMode _mode = ThemeMode.System;

    public event EventHandler? ThemeChanged;

    public ElementTheme ElementTheme => _mode switch
    {
        ThemeMode.Light => ElementTheme.Light,
        ThemeMode.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Thème réellement affiché (le thème système résolu).</summary>
    public bool IsDark => _mode switch
    {
        ThemeMode.Light => false,
        ThemeMode.Dark => true,
        _ => _uiSettings.GetColorValue(UIColorType.Background) == Colors.Black,
    };

    /// <summary>
    /// Couleur d'accent personnalisée : à appeler avant la création de la première fenêtre, les ressources
    /// système étant lues une fois par WinUI.
    /// </summary>
    public static void ApplyAccent(Application app, string? hex)
    {
        if (TryParseColor(hex, out var accent))
        {
            app.Resources["SystemAccentColor"] = accent;
            app.Resources["SystemAccentColorLight1"] = Shift(accent, 0.15);
            app.Resources["SystemAccentColorLight2"] = Shift(accent, 0.30);
            app.Resources["SystemAccentColorLight3"] = Shift(accent, 0.45);
            app.Resources["SystemAccentColorDark1"] = Shift(accent, -0.15);
            app.Resources["SystemAccentColorDark2"] = Shift(accent, -0.30);
            app.Resources["SystemAccentColorDark3"] = Shift(accent, -0.45);
        }
    }

    public void SetMode(ThemeMode mode)
    {
        _mode = mode;
        foreach (var window in Alive())
        {
            Apply(window);
        }

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Register(Window window)
    {
        _windows.Add(new WeakReference<Window>(window));
        Apply(window);
    }

    public static bool TryParseColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || hex[0] != '#'
            || !uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return false;
        }

        color = Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private void Apply(Window window)
    {
        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = ElementTheme;
        }
    }

    private IEnumerable<Window> Alive()
    {
        _windows.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in _windows)
        {
            if (weak.TryGetTarget(out var window))
            {
                yield return window;
            }
        }
    }

    private static Color Shift(Color c, double amount)
    {
        byte Mix(byte v) => (byte)Math.Clamp(amount >= 0 ? v + (255 - v) * amount : v * (1 + amount), 0, 255);
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }
}
