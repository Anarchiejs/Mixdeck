using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Mixdeck.App.Services;
using Mixdeck.Platform.Taskbar;
using Windows.Graphics;

namespace Mixdeck.App.Views.Osd;

/// <summary>
/// Bulle discrète en bas de l'écran, au-dessus des jeux en mode fenêtré. Ne prend jamais le focus et laisse
/// passer les clics.
/// </summary>
public sealed partial class OsdWindow : Window
{
    private const int WidthDip = 260;
    private const int HeightDip = 52;
    private const int BottomMarginDip = 96;

    private readonly ITaskbarLocator _taskbar;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _visible;
    private Storyboard? _animation;

    public OsdWindow(ITaskbarLocator taskbar, ThemeService theme)
    {
        _taskbar = taskbar;
        InitializeComponent();
        WindowInterop.MakeToolWindow(this, alwaysOnTop: true);
        WindowInterop.MakeClickThrough(this);
        theme.Register(this);

        _hideTimer = DispatcherQueue.CreateTimer();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(1600);
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) => FadeOut();
    }

    public void Show(string text, string glyph, bool isWarning)
    {
        Message.Text = text;
        Glyph.Glyph = glyph;
        Glyph.Foreground = isWarning
            ? (Brush)Application.Current.Resources["MixdeckMutedBrush"]
            : (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];

        if (!_visible)
        {
            PlaceOnScreen();
            _animation?.Stop();
            Root.Opacity = 0;
            WindowInterop.GetAppWindow(this).Show(activateWindow: false);
            _visible = true;
        }

        // Toujours repartir vers l'opacité pleine depuis l'opacité actuelle : un fondu de sortie en cours (ou
        // interrompu) ne doit jamais laisser la bulle transparente.
        var current = Root.Opacity;
        _animation?.Stop();
        Root.Opacity = current;
        _animation = Animate(1, 120);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void PlaceOnScreen()
    {
        var placement = _taskbar.GetPrimary();
        var scale = placement.Scale;
        var width = (int)(WidthDip * scale);
        var height = (int)(HeightDip * scale);
        var area = placement.WorkArea;
        var x = area.Left + (area.Width - width) / 2;
        var y = area.Bottom - height - (int)(BottomMarginDip * scale);
        WindowInterop.GetAppWindow(this).MoveAndResize(new RectInt32(x, y, width, height));
    }

    private void FadeOut()
    {
        // Une seule animation à la fois : sinon la fin d'un fondu précédent fige l'opacité et la bulle reste invisible.
        var current = Root.Opacity;
        _animation?.Stop();
        Root.Opacity = current;
        var fade = Animate(0, 220);
        _animation = fade;
        fade.Completed += (_, _) =>
        {
            if (ReferenceEquals(_animation, fade))
            {
                WindowInterop.GetAppWindow(this).Hide();
                _visible = false;
            }
        };
    }

    private Storyboard Animate(double to, int milliseconds)
    {
        var animation = new DoubleAnimation
        {
            From = Root.Opacity,
            To = to,
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(animation, Root);
        Storyboard.SetTargetProperty(animation, nameof(UIElement.Opacity));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
        return storyboard;
    }
}
