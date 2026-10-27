using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Shared;

namespace Mixdeck.App.Services;

/// <summary>
/// Icône près de l'horloge : clic gauche = panneau rapide, clic droit = menu. L'icône passe au rouge quand le
/// micro est coupé.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly IShellService _shell;
    private readonly MixerViewModel _mixer;
    private readonly System.Drawing.Icon _normalIcon;
    private readonly System.Drawing.Icon _mutedIcon;
    private TaskbarIcon? _icon;
    private MenuFlyoutItem? _micItem;

    public TrayIconService(IShellService shell, MixerViewModel mixer)
    {
        _shell = shell;
        _mixer = mixer;
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        _normalIcon = new System.Drawing.Icon(Path.Combine(assets, "Mixdeck.ico"));
        _mutedIcon = new System.Drawing.Icon(Path.Combine(assets, "MixdeckMuted.ico"));
    }

    public void Create()
    {
        _micItem = new MenuFlyoutItem { Command = _mixer.ToggleMicCommand, Icon = new FontIcon { Glyph = "" } };
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Ouvrir le panneau",
            Command = new RelayCommand(_shell.ToggleFlyout),
            Icon = new FontIcon { Glyph = "" },
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Console",
            Command = new RelayCommand(() => _shell.ShowMainWindow(ShellPage.Mixer)),
            Icon = new FontIcon { Glyph = "" },
        });
        menu.Items.Add(_micItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Réglages",
            Command = new RelayCommand(() => _shell.ShowMainWindow(ShellPage.Settings)),
            Icon = new FontIcon { Glyph = "" },
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = "Quitter Mixdeck",
            Command = new RelayCommand(_shell.Exit),
            Icon = new FontIcon { Glyph = "" },
        });

        _icon = new TaskbarIcon
        {
            Icon = _normalIcon,
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(_shell.ToggleFlyout),
            NoLeftClickDelay = true,
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);

        _mixer.PropertyChanged += OnMixerPropertyChanged;
        Refresh();
    }

    private void OnMixerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MixerViewModel.IsMicMuted) or nameof(MixerViewModel.ActiveProfile)
            or nameof(MixerViewModel.ConnectionState) or nameof(MixerViewModel.Mic))
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        if (_icon is null || _micItem is null)
        {
            return;
        }

        var muted = _mixer.IsMicMuted;
        _icon.Icon = muted ? _mutedIcon : _normalIcon;
        _micItem.Text = muted ? "Activer le micro" : "Couper le micro";

        var status = !_mixer.IsConnected
            ? _mixer.ConnectionTitle
            : muted ? "Micro coupé" : "Micro actif";
        var profile = _mixer.ActiveProfile is { } p ? $"\nProfil : {p.Name}" : string.Empty;
        _icon.ToolTipText = $"Mixdeck — {status}{profile}";
    }

    public void Dispose()
    {
        _mixer.PropertyChanged -= OnMixerPropertyChanged;
        _icon?.Dispose();
        _normalIcon.Dispose();
        _mutedIcon.Dispose();
    }
}
