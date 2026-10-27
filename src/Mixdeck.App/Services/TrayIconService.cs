using Mixdeck.App.Localization;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Xaml.Controls;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Taskbar;

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
    private readonly MicFeedbackService _feedback;
    private ProfileItemViewModel? _profile;
    private BusViewModel? _bus;
    private bool _disposed;

    public TrayIconService(IShellService shell, MixerViewModel mixer, ISettingsStore settings,
        IUiDispatcher ui, ITaskbarLocator taskbar, ThemeService theme)
    {
        _shell = shell;
        _mixer = mixer;
        // Même durée de vie que l'icône, déjà créée au lancement et libérée à l'arrêt.
        _feedback = new MicFeedbackService(mixer, settings, ui, taskbar, theme);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        _normalIcon = new System.Drawing.Icon(Path.Combine(assets, "Mixdeck.ico"));
        _mutedIcon = new System.Drawing.Icon(Path.Combine(assets, "MixdeckMuted.ico"));
    }

    public void Create()
    {
        if (_disposed || _icon is not null) return;
        _micItem = new MenuFlyoutItem { Command = _mixer.ToggleMicCommand, Icon = new FontIcon { Glyph = "" } };
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = Strings.Get("CS_012"),
            Command = new RelayCommand(_shell.ToggleFlyout),
            Icon = new FontIcon { Glyph = "" },
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = Strings.Get("CS_013"),
            Command = new RelayCommand(() => _shell.ShowMainWindow(ShellPage.Mixer)),
            Icon = new FontIcon { Glyph = "" },
        });
        menu.Items.Add(_micItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = Strings.Get("CS_014"),
            Command = new RelayCommand(() => _shell.ShowMainWindow(ShellPage.Settings)),
            Icon = new FontIcon { Glyph = "" },
        });
        menu.Items.Add(new MenuFlyoutItem
        {
            Text = Strings.Get("CS_015"),
            Command = new RelayCommand(_shell.Exit),
            Icon = new FontIcon { Glyph = "" },
        });

        _icon = new TaskbarIcon
        {
            Icon = _normalIcon,
            // Menu Windows natif : le mode SecondWindow crée une fenêtre réduite visible dans la barre des tâches.
            ContextMenuMode = ContextMenuMode.PopupMenu,
            ContextFlyout = menu,
            LeftClickCommand = new RelayCommand(_shell.ToggleFlyout),
            MiddleClickCommand = _mixer.ToggleMicFromTrayCommand,
            NoLeftClickDelay = true,
        };
        _icon.ForceCreate(enablesEfficiencyMode: false);

        _mixer.PropertyChanged += OnMixerPropertyChanged;
        TrackBus();
        TrackProfile();
        Refresh();
    }

    private void OnMixerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MixerViewModel.Layout))
        {
            TrackBus();
            Refresh();
        }
        if (e.PropertyName is nameof(MixerViewModel.IsMicMuted) or nameof(MixerViewModel.ActiveProfile)
            or nameof(MixerViewModel.ConnectionState) or nameof(MixerViewModel.Mic)
            or nameof(MixerViewModel.HasMic) or nameof(MixerViewModel.CanToggleMic))
        {
            TrackProfile();
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
        _micItem.Text = !_mixer.HasMic ? Strings.Get("CS_016") : muted ? Strings.Get("CS_017") : Strings.Get("CS_018");
        _micItem.IsEnabled = _mixer.CanToggleMic;

        var status = !_mixer.IsConnected
            ? _mixer.ConnectionTitle
            : !_mixer.HasMic ? Strings.Get("CS_016") : muted ? Strings.Get("Core_MicMuted") : Strings.Get("Core_MicOn");
        var gain = _mixer.Buses[0].Gain.ToString("0.##", Strings.Culture).Replace("-", "−");
        var volume = _mixer.IsConnected ? $" · A1 {gain} dB" : string.Empty;
        var profile = _mixer.ActiveProfile is { } p ? "\n" + Strings.Format("Core_Profile", p.Name.Replace('\r', ' ').Replace('\n', ' ')) : string.Empty;
        var tooltip = $"Mixdeck · {status}{volume}{profile}";
        // La taille native est exprimée en caractères UTF-16 ; ne pas couper une paire de substitution.
        if (tooltip.Length > 127)
        {
            var length = char.IsHighSurrogate(tooltip[125]) ? 125 : 126;
            tooltip = tooltip[..length] + "…";
        }
        _icon.ToolTipText = tooltip;
    }

    private void OnBusPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BusViewModel.Gain)) Refresh();
    }

    private void TrackBus()
    {
        if (_bus is not null) _bus.PropertyChanged -= OnBusPropertyChanged;
        _bus = _mixer.Buses[0];
        _bus.PropertyChanged += OnBusPropertyChanged;
    }

    private void TrackProfile()
    {
        if (ReferenceEquals(_profile, _mixer.ActiveProfile)) return;
        if (_profile is not null) _profile.PropertyChanged -= OnProfilePropertyChanged;
        _profile = _mixer.ActiveProfile;
        if (_profile is not null) _profile.PropertyChanged += OnProfilePropertyChanged;
    }

    private void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProfileItemViewModel.Name)) Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mixer.PropertyChanged -= OnMixerPropertyChanged;
        if (_bus is not null) _bus.PropertyChanged -= OnBusPropertyChanged;
        _bus = null;
        if (_profile is not null) _profile.PropertyChanged -= OnProfilePropertyChanged;
        _profile = null;
        _feedback.Dispose();
        _icon?.Dispose();
        _icon = null;
        _normalIcon.Dispose();
        _mutedIcon.Dispose();
    }
}
