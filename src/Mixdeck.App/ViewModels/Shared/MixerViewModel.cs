using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Mixdeck.App.Services;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Automation;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>
/// État partagé du mixeur pour toute l'interface (panneau rapide et console) : une seule instance.
/// Suit les relectures de Voicemeeter, les profils et les réglages ; pilote les vumètres seulement quand une
/// fenêtre qui en affiche est visible.
/// </summary>
public sealed partial class MixerViewModel : ObservableObject, IDisposable
{
    private readonly IMixer _mixer;
    private readonly IProfileService _profiles;
    private readonly ISettingsStore _settings;
    private readonly AutomationCoordinator _automation;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<MixerViewModel> _logger;
    private readonly IOsdService _osd;
    private int _meterUsers;
    private TimeSpan _lastRender;
    private bool _savingCustomization;

    public MixerViewModel(
        IMixer mixer,
        IProfileService profiles,
        ISettingsStore settings,
        AutomationCoordinator automation,
        IUiDispatcher ui,
        ILogger<MixerViewModel> logger,
        IOsdService osd)
    {
        _mixer = mixer;
        _profiles = profiles;
        _settings = settings;
        _automation = automation;
        _ui = ui;
        _logger = logger;
        _osd = osd;

        ConnectionState = mixer.State;
        ApplySnapshot(mixer.Snapshot);
        RefreshProfiles();

        _mixer.StateChanged += OnMixerStateChanged;
        _mixer.SnapshotChanged += OnMixerSnapshotChanged;
        _profiles.ProfilesChanged += OnProfilesChanged;
        _profiles.ActiveChanged += OnActiveProfileChanged;
        _settings.Changed += OnSettingsChanged;
    }

    public ObservableCollection<StripViewModel> Strips { get; } = [];

    public ObservableCollection<BusViewModel> Buses { get; } = [];

    public MixerLayout Layout { get; private set; } = MixerLayout.Banana;

    public ObservableCollection<ProfileItemViewModel> Profiles { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    /// <summary>Affiche l'aide « aucun profil » du panneau.</summary>
    public bool ShowNoProfilesHint => Profiles.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected), nameof(IsDisconnected), nameof(ConnectionTitle), nameof(ConnectionMessage), nameof(CanLaunchVoicemeeter), nameof(CanToggleMic))]
    public partial MixerConnectionState ConnectionState { get; set; }

    [ObservableProperty]
    public partial ProfileItemViewModel? ActiveProfile { get; set; }

    /// <summary>Tranche pilotée par le gros bouton « Micro ».</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMicMuted), nameof(MicDisplayName))]
    public partial StripViewModel Mic { get; set; } = null!;

    public bool IsConnected => ConnectionState == MixerConnectionState.Connected;

    public bool IsDisconnected => !IsConnected;

    /// <summary>Vrai quand le dernier changement de nom, d'icône ou de visibilité n'a pas pu être enregistré.</summary>
    [ObservableProperty]
    public partial bool HasCustomizationError { get; set; }

    public string CustomizationErrorMessage => "Le changement s'applique jusqu'à la fermeture de Mixdeck. Vérifiez l'accès au dossier de données, puis modifiez à nouveau le nom ou l'icône.";

    public bool HasMic => _settings.Current.MicStripIndex >= 0 && _settings.Current.MicStripIndex < Strips.Count;

    public bool CanToggleMic => IsConnected && HasMic;

    public string MicDisplayName => HasMic ? Mic.DisplayName : "Micro non configuré";

    public bool IsMicMuted
    {
        get => HasMic && Mic.IsMuted;
        set { if (CanToggleMic) Mic.IsMuted = value; }
    }

    public bool CanLaunchVoicemeeter => ConnectionState == MixerConnectionState.VoicemeeterNotRunning;

    public string ConnectionTitle => ConnectionState switch
    {
        MixerConnectionState.Connected => "Connecté à Voicemeeter " + (Layout.VoicemeeterType switch { 1 => "Standard", 3 => "Potato", _ => "Banana" }),
        MixerConnectionState.VoicemeeterNotRunning => "Voicemeeter n'est pas lancé",
        MixerConnectionState.NotInstalled => "Voicemeeter est introuvable",
        MixerConnectionState.UnsupportedType => "Version de Voicemeeter non prise en charge",
        _ => "Connexion à Voicemeeter…",
    };

    public string ConnectionMessage => ConnectionState switch
    {
        MixerConnectionState.Connected => string.Empty,
        MixerConnectionState.VoicemeeterNotRunning => "Mixdeck se reconnectera tout seul dès que le moteur audio sera lancé.",
        MixerConnectionState.NotInstalled => "Installez Voicemeeter depuis vb-audio.com, puis relancez Mixdeck.",
        MixerConnectionState.UnsupportedType => "Mixdeck pilote Voicemeeter Standard, Banana et Potato. Cette version n’est pas prise en charge.",
        _ => string.Empty,
    };

    [RelayCommand]
    private void ToggleMic()
    {
        if (CanToggleMic) Mic.IsMuted = !Mic.IsMuted;
    }

    /// <summary>Clic milieu sur l'icône : même retour à l'écran que le raccourci clavier (le panneau, lui, montre déjà l'état).</summary>
    [RelayCommand]
    private void ToggleMicFromTray()
    {
        if (!CanToggleMic) return;
        Mic.IsMuted = !Mic.IsMuted;
        if (_settings.Current.ShowOsd)
        {
            _osd.Show(IsMicMuted ? "Micro coupé" : "Micro activé", IsMicMuted ? "\uF781" : "\uE720", IsMicMuted);
        }
    }

    [RelayCommand]
    private async Task LaunchVoicemeeterAsync()
    {
        if (!await _mixer.LaunchVoicemeeterAsync())
        {
            _logger.LogWarning("Voicemeeter n'a pas pu être lancé");
        }
    }

    [RelayCommand]
    private async Task ApplyProfileAsync(ProfileItemViewModel? profile)
    {
        if (profile is not null)
        {
            await _automation.ApplyManualProfileAsync(profile.Id);
        }
    }

    [RelayCommand]
    private async Task CaptureProfileAsync(string? name)
    {
        var profile = await _profiles.CaptureCurrentAsync(string.IsNullOrWhiteSpace(name) ? NextProfileName() : name);
        await _automation.ApplyManualProfileAsync(profile.Id);
    }

    /// <summary>
    /// Active les vumètres tant que le jeton n'est pas libéré. Plusieurs fenêtres peuvent en demander en même
    /// temps ; la lecture des niveaux s'arrête quand plus personne n'en a besoin (CPU ≈ 0 au repos).
    /// </summary>
    public IDisposable AcquireMeters()
    {
        if (_meterUsers++ == 0)
        {
            _mixer.LevelsEnabled = true;
            _lastRender = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        return new MeterLease(this);
    }

    private void ReleaseMeters()
    {
        if (--_meterUsers > 0)
        {
            return;
        }

        _meterUsers = 0;
        CompositionTarget.Rendering -= OnRendering;
        _mixer.LevelsEnabled = false;
        foreach (var channel in Strips.Cast<ChannelViewModel>().Concat(Buses))
        {
            channel.ResetMeter();
        }
    }

    private void OnRendering(object? sender, object e)
    {
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.FromMilliseconds(Environment.TickCount64);
        var elapsed = _lastRender == TimeSpan.Zero ? TimeSpan.FromMilliseconds(16) : now - _lastRender;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        _lastRender = now;
        var frame = _mixer.Levels;
        if (frame.Layout != Layout) return;
        for (var i = 0; i < Strips.Count && i < frame.Strips.Count; i++)
        {
            Strips[i].UpdateMeter(frame.Strips[i], elapsed);
        }

        for (var i = 0; i < Buses.Count && i < frame.Buses.Count; i++)
        {
            Buses[i].UpdateMeter(frame.Buses[i], elapsed);
        }
    }

    private void OnMixerStateChanged(object? sender, MixerConnectionState state) =>
        _ui.Post(() => ConnectionState = state);

    private void OnMixerSnapshotChanged(object? sender, MixerSnapshot snapshot) =>
        _ui.Post(() => ApplySnapshot(snapshot));

    private void OnProfilesChanged(object? sender, EventArgs e) => _ui.Post(RefreshProfiles);

    private void OnActiveProfileChanged(object? sender, Profile? profile) => _ui.Post(UpdateActiveProfile);

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (!_savingCustomization)
        {
            _ui.Post(() => ApplySettings(settings));
        }
    }

    private void ApplySnapshot(MixerSnapshot snapshot)
    {
        var layoutChanged = Layout != snapshot.Layout || Strips.Count != snapshot.Layout.StripCount;
        if (layoutChanged) RebuildChannels(snapshot.Layout);
        for (var i = 0; i < Strips.Count && i < snapshot.Strips.Count; i++)
        {
            Strips[i].Apply(snapshot.Strips[i]);
        }

        for (var i = 0; i < Buses.Count && i < snapshot.Buses.Count; i++)
        {
            Buses[i].Apply(snapshot.Buses[i]);
        }

        if (layoutChanged)
        {
            OnPropertyChanged(nameof(Layout));
            OnPropertyChanged(nameof(ConnectionTitle));
        }
        OnPropertyChanged(nameof(IsMicMuted));
    }

    private void RebuildChannels(MixerLayout layout)
    {
        if (Mic is not null) Mic.PropertyChanged -= OnMicPropertyChanged;
        foreach (var channel in Strips.Cast<ChannelViewModel>().Concat(Buses))
            channel.CustomizationChanged -= OnChannelCustomizationChanged;
        Layout = layout;
        Strips.Clear();
        Buses.Clear();
        foreach (var index in Enumerable.Range(0, layout.StripCount)) Strips.Add(new StripViewModel(_mixer, index, layout));
        foreach (var index in Enumerable.Range(0, layout.BusCount)) Buses.Add(new BusViewModel(_mixer, index, layout));
        foreach (var channel in Strips.Cast<ChannelViewModel>().Concat(Buses))
            channel.CustomizationChanged += OnChannelCustomizationChanged;
        ApplySettings(_settings.Current);
    }

    private void ApplySettings(AppSettings settings)
    {
        foreach (var strip in Strips)
        {
            var c = settings.StripCustomization(strip.Index);
            strip.ApplyCustomization(c.DisplayName, c.Icon, c.ShowInFlyout);
        }

        foreach (var bus in Buses)
        {
            var c = settings.BusCustomization(bus.Index);
            // Par défaut seul A1 est visible dans le panneau.
            var show = settings.Buses.Any(b => b.Index == bus.Index) ? c.ShowInFlyout : bus.Index == 0;
            bus.ApplyCustomization(c.DisplayName, c.Icon, show);
        }

        var micIndex = Math.Clamp(settings.MicStripIndex, 0, Strips.Count - 1);
        if (Mic is not null)
        {
            Mic.PropertyChanged -= OnMicPropertyChanged;
        }

        Mic = Strips[micIndex];
        Mic.PropertyChanged += OnMicPropertyChanged;
        OnPropertyChanged(nameof(HasMic));
        OnPropertyChanged(nameof(CanToggleMic));
        OnPropertyChanged(nameof(IsMicMuted));
        OnPropertyChanged(nameof(MicDisplayName));
    }

    private void OnMicPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StripViewModel.IsMuted))
        {
            OnPropertyChanged(nameof(IsMicMuted));
        }
        if (e.PropertyName == nameof(StripViewModel.DisplayName)) OnPropertyChanged(nameof(MicDisplayName));
    }

    private async void OnChannelCustomizationChanged(object? sender, EventArgs e)
    {
        try
        {
            _savingCustomization = true;
            // Garder les personnalisations des canaux absents pour une prochaine connexion à une autre édition.
            ChannelCustomization[] strips = [.. Strips.Select(ToCustomization), .. _settings.Current.Strips.Where(s => s.Index >= Strips.Count)];
            ChannelCustomization[] buses = [.. Buses.Select(ToCustomization), .. _settings.Current.Buses.Where(b => b.Index >= Buses.Count)];
            await _settings.UpdateAsync(s => s with { Strips = strips, Buses = buses });
            // Le magasin journalise ses échecs d'écriture sans lever d'exception : on vérifie ce qui a été retenu.
            HasCustomizationError = !_settings.Current.Strips.SequenceEqual(strips) || !_settings.Current.Buses.SequenceEqual(buses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Impossible d'enregistrer la personnalisation des tranches");
            HasCustomizationError = true;
        }
        finally
        {
            _savingCustomization = false;
        }
    }

    private static ChannelCustomization ToCustomization(ChannelViewModel channel)
    {
        var (name, icon) = channel.GetCustomization();
        return new ChannelCustomization(channel.Index, name, icon, channel.ShowInFlyout);
    }

    private void RefreshProfiles()
    {
        var existing = Profiles.ToDictionary(p => p.Id);
        Profiles.Clear();
        foreach (var profile in _profiles.Profiles)
        {
            if (existing.TryGetValue(profile.Id, out var item))
            {
                item.Update(profile);
                Profiles.Add(item);
            }
            else
            {
                Profiles.Add(new ProfileItemViewModel(profile));
            }
        }

        UpdateActiveProfile();
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(ShowNoProfilesHint));
    }

    private void UpdateActiveProfile()
    {
        var activeId = _profiles.Active?.Id;
        foreach (var item in Profiles)
        {
            item.IsActive = item.Id == activeId;
        }

        ActiveProfile = Profiles.FirstOrDefault(p => p.IsActive);
    }

    private string NextProfileName()
    {
        for (var n = Profiles.Count + 1; ; n++)
        {
            var name = $"Profil {n}";
            if (Profiles.All(p => p.Name != name))
            {
                return name;
            }
        }
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnRendering;
        _mixer.StateChanged -= OnMixerStateChanged;
        _mixer.SnapshotChanged -= OnMixerSnapshotChanged;
        _profiles.ProfilesChanged -= OnProfilesChanged;
        _profiles.ActiveChanged -= OnActiveProfileChanged;
        _settings.Changed -= OnSettingsChanged;
        Mic.PropertyChanged -= OnMicPropertyChanged;
        foreach (var channel in Strips.Cast<ChannelViewModel>().Concat(Buses))
        {
            channel.CustomizationChanged -= OnChannelCustomizationChanged;
        }
        _mixer.LevelsEnabled = false;
    }

    private sealed class MeterLease(MixerViewModel owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.ReleaseMeters();
            }
        }
    }
}
