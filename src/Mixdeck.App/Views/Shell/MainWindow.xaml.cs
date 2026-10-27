using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Mixdeck.App.Services;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Pages;
using Windows.Graphics;

namespace Mixdeck.App.Views.Shell;

/// <summary>Console complète : navigation entre Console, Routage, Profils, Règles, Réglages, À propos.</summary>
public sealed partial class MainWindow : Window
{
    private static readonly IReadOnlyDictionary<ShellPage, Type> PageTypes = new Dictionary<ShellPage, Type>
    {
        [ShellPage.Mixer] = typeof(MixerPage),
        [ShellPage.Routing] = typeof(RoutingPage),
        [ShellPage.Profiles] = typeof(ProfilesPage),
        [ShellPage.Rules] = typeof(RulesPage),
        [ShellPage.Settings] = typeof(SettingsPage),
        [ShellPage.About] = typeof(AboutPage),
    };

    private IDisposable? _meters;

    public MainWindow(MixerViewModel mixer, ThemeService theme)
    {
        Mixer = mixer;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Mixdeck.ico"));
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        theme.Register(this);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 820;
            presenter.PreferredMinimumHeight = 560;
        }

        ResizeForDpi(1180, 760);
        AppWindow.Changed += OnAppWindowChanged;
        AppWindow.Closing += (_, e) =>
        {
            // Fermer la console laisse Mixdeck dans la barre des tâches.
            e.Cancel = true;
            AppWindow.Hide();
            UpdateMeters();
        };
    }

    public MixerViewModel Mixer { get; }

    public void ShowPage(ShellPage? page)
    {
        Navigate(page ?? (ContentFrame.Content is null ? ShellPage.Mixer : CurrentPage));
        AppWindow.Show();
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        UpdateMeters();
    }

    private ShellPage CurrentPage { get; set; } = ShellPage.Mixer;

    private void Navigate(ShellPage page)
    {
        if (ContentFrame.Content is not null && CurrentPage == page)
        {
            return;
        }

        CurrentPage = page;
        ContentFrame.Navigate(PageTypes[page], null, new EntranceNavigationTransitionInfo());
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag } && Enum.TryParse<ShellPage>(tag, out var page))
        {
            Navigate(page);
        }
    }

    // Garde l'élément sélectionné du menu en phase quand la navigation vient du code (menu de l'icône, panneau).
    private void OnFrameNavigated(object sender, NavigationEventArgs e)
    {
        var tag = PageTypes.First(p => p.Value == e.SourcePageType).Key.ToString();
        var item = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .FirstOrDefault(i => (string)i.Tag == tag);
        if (!ReferenceEquals(Navigation.SelectedItem, item))
        {
            Navigation.SelectedItem = item;
        }
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange || args.DidVisibilityChange)
        {
            UpdateMeters();
        }
    }

    /// <summary>Vumètres actifs seulement quand la console est visible et pas réduite.</summary>
    private void UpdateMeters()
    {
        var visible = AppWindow.IsVisible
            && AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized };
        if (visible && _meters is null)
        {
            _meters = Mixer.AcquireMeters();
        }
        else if (!visible && _meters is not null)
        {
            _meters.Dispose();
            _meters = null;
        }
    }

    private void ResizeForDpi(int widthDip, int heightDip)
    {
        var scale = WindowInterop.GetDpi(this) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(widthDip * scale), (int)(heightDip * scale)));
        if (AppWindow.Presenter is OverlappedPresenter)
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new PointInt32(
                area.X + (area.Width - AppWindow.Size.Width) / 2,
                area.Y + (area.Height - AppWindow.Size.Height) / 2));
        }
    }
}
