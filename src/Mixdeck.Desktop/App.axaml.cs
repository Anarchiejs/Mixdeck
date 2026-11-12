using Avalonia;
using Microsoft.Extensions.Logging;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Mixdeck.Platform.Desktop.Startup;

namespace Mixdeck.Desktop;

public partial class App : Application
{
    private ShellWindow? _window;
    private TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Program.Host is { } host)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _window = new ShellWindow(host);
            desktop.MainWindow = _window;
            if (!Program.Options.Minimized) _window.Show();
            void Show() => Dispatcher.UIThread.Post(() =>
            {
                _window.Show();
                _window.WindowState = WindowState.Normal;
                _window.Activate();
            });
            host.ShowRequested += Show;
            if (Program.Gate is { } gate) gate.ShowRequested += Show;
            AttachTray(host, desktop);
            host.LanguageChanged += () => Dispatcher.UIThread.Post(() => AttachTray(host, desktop));
            desktop.Exit += (_, _) => host.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _ = host.StartAsync();
            _ = QuietUpdateCheckAsync(host);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void AttachTray(DesktopHost host, IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_tray is not null)
        {
            _tray.Dispose();
            _tray = null;
        }
        var open = new NativeMenuItem(DesktopText.Get("Menu_Open"));
        open.Click += (_, _) => host.RequestShow();
        var mute = new NativeMenuItem(DesktopText.Get("Menu_Mute"));
        mute.Click += async (_, _) => await host.ToggleInputMuteAsync();
        var quit = new NativeMenuItem(DesktopText.Get("Menu_Quit"));
        quit.Click += (_, _) =>
        {
            _window?.AllowClose();
            desktop.Shutdown();
        };
        _tray = new TrayIcon
        {
            ToolTipText = "Mixdeck",
            Icon = LoadIcon(),
            Menu = new NativeMenu { open, mute, quit },
        };
        _tray.Clicked += (_, _) => host.RequestShow();
        TrayIcon.SetIcons(this, new TrayIcons { _tray });
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Mixdeck/Assets/Mixdeck-256.png"));
            return new WindowIcon(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task QuietUpdateCheckAsync(DesktopHost host)
    {
        try
        {
            if (!host.Settings.Current.CheckForUpdates || !host.Updates.IsInstalled) return;
            var result = await host.Updates.CheckAsync();
            if (result.Available)
                host.Log.LogInformation("Mise à jour disponible : {Version}", result.Version);
        }
        catch (Exception exception)
        {
            host.Log.LogWarning(exception, "Recherche automatique de mise à jour non aboutie");
        }
    }
}
