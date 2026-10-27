using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Mixdeck.App.Controls;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.AppVolume;
using Mixdeck.Core.Abstractions;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.Platform.Taskbar;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Mixdeck.App.Views.Flyout;

/// <summary>
/// Panneau rapide ancré au-dessus de l'horloge, comme le panneau de volume de Windows 11 : s'ouvre au clic sur
/// l'icône, se ferme au clic ailleurs ou avec Échap.
/// </summary>
public sealed partial class FlyoutWindow : Window
{
    private const double WidthDip = 360;
    private const double MarginDip = 12;
    private const double SlideDip = 24;

    // Un clic sur l'icône pendant que le panneau est ouvert le désactive (donc le ferme) juste avant d'arriver :
    // sans ce délai, le même clic le rouvrirait aussitôt.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly ITaskbarLocator _taskbar;
    private readonly IShellService _shell;
    private readonly AppVolumeListViewModel _appVolumes;
    private readonly ISettingsStore _settings;
    private IDisposable? _meters;
    private DateTime _hiddenAt = DateTime.MinValue;
    private TaskbarEdge _edge = TaskbarEdge.Bottom;
    private Storyboard? _openAnimation;

    public FlyoutWindow(MixerViewModel mixer, ITaskbarLocator taskbar, IShellService shell, ThemeService theme,
        AppVolumeListViewModel appVolumes, ISettingsStore settings)
    {
        Mixer = mixer;
        _appVolumes = appVolumes;
        _settings = settings;
        _taskbar = taskbar;
        _shell = shell;
        InitializeComponent(); Title = Mixdeck.App.Localization.Strings.Get("FlyoutWindow001.Title");

        WindowInterop.MakeToolWindow(this, alwaysOnTop: true);
        theme.Register(this);
        Activated += OnActivated;
        AppWindow.Closing += (_, e) =>
        {
            // Fermer = cacher : le panneau vit aussi longtemps que l'appli.
            e.Cancel = true;
            Hide();
        };
    }

    public MixerViewModel Mixer { get; }

    public bool IsOpen { get; private set; }

    public void Toggle()
    {
        if (IsOpen)
        {
            Hide();
        }
        else if (DateTime.UtcNow - _hiddenAt > ReopenGuard)
        {
            Show();
        }
    }

    public void Show()
    {
        PlaceNearTray();
        _meters ??= Mixer.AcquireMeters();
        ShowAppVolumes(_settings.Current.ShowAppVolumes);
        AppWindow.Show();
        Activate();
        IsOpen = true;
        PlayOpenAnimation();

        // La première mesure précède l'initialisation des liaisons x:Bind (bandeau, lignes masquées) :
        // on recale la taille une fois la mise en page faite.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (IsOpen)
            {
                PlaceNearTray();
            }
        });
        ProfilePills.Focus(FocusState.Programmatic);
    }

    public void Hide()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        _hiddenAt = DateTime.UtcNow;
        _meters?.Dispose();
        _meters = null;
        ShowAppVolumes(false);
        _openAnimation?.Stop();
        _openAnimation = null;
        AppWindow.Hide();
    }

    /// <summary>Le suivi des sessions audio ne tourne que panneau ouvert (aucun coût au repos).</summary>
    private void ShowAppVolumes(bool show)
    {
        if (show)
        {
            AppVolumes.Visibility = Visibility.Visible;
            AppVolumes.ViewModel = _appVolumes;
            _appVolumes.Activate();
        }
        else
        {
            _appVolumes.Deactivate();
            AppVolumes.ViewModel = null;
            AppVolumes.Visibility = Visibility.Collapsed;
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated && IsOpen)
        {
            Hide();
        }
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    private async void OnProfileInvoked(ProfilePillList sender, ProfilePillInvokedEventArgs args)
    {
        if (args.Item is ProfileItemViewModel profile)
        {
            await Mixer.ApplyProfileCommand.ExecuteAsync(profile);
        }
    }

    private void OnOpenConsoleClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _shell.ShowMainWindow(ShellPage.Mixer);
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e)
    {
        Hide();
        _shell.ShowMainWindow(ShellPage.Settings);
    }

    /// <summary>
    /// Place le panneau dans le coin de la zone de travail le plus proche de l'horloge, sur l'écran où l'on a
    /// cliqué, à la bonne échelle pour son DPI.
    /// </summary>
    private void PlaceNearTray()
    {
        GetCursorPos(out var cursor);
        var placement = _taskbar.GetForPoint(cursor.X, cursor.Y);
        _edge = placement.Edge;
        var scale = placement.Scale;
        var area = placement.WorkArea;

        // Hauteur : celle que demande le contenu, bornée à 80 % de la hauteur de l'écran.
        Root.Measure(new Size(WidthDip, double.PositiveInfinity));
        var maxHeightDip = area.Height / scale * 0.8;
        var heightDip = Math.Clamp(Root.DesiredSize.Height, 240, maxHeightDip);

        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(heightDip * scale);
        var margin = (int)Math.Round(MarginDip * scale);

        var x = placement.Edge == TaskbarEdge.Left ? area.Left + margin : area.Right - width - margin;
        var y = placement.Edge == TaskbarEdge.Top ? area.Top + margin : area.Bottom - height - margin;
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    /// <summary>Glissement depuis la barre des tâches + fondu, 250 ms, décélération (courbe Fluent).</summary>
    private void PlayOpenAnimation()
    {
        var (fromX, fromY) = _edge switch
        {
            TaskbarEdge.Top => (0.0, -SlideDip),
            TaskbarEdge.Left => (-SlideDip, 0.0),
            TaskbarEdge.Right => (SlideDip, 0.0),
            _ => (0.0, SlideDip),
        };

        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 };
        var duration = new Duration(TimeSpan.FromMilliseconds(250));
        _openAnimation?.Stop();
        var storyboard = new Storyboard();
        _openAnimation = storyboard;
        AddAnimation(storyboard, SlideTransform, "X", fromX, 0, duration, ease);
        AddAnimation(storyboard, SlideTransform, "Y", fromY, 0, duration, ease);
        AddAnimation(storyboard, Root, nameof(UIElement.Opacity), 0, 1, new Duration(TimeSpan.FromMilliseconds(160)), null);
        storyboard.Begin();
    }

    private static void AddAnimation(Storyboard storyboard, DependencyObject target, string property, double from, double to, Duration duration, EasingFunctionBase? ease)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = duration, EasingFunction = ease };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point32
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point32 point);
}
