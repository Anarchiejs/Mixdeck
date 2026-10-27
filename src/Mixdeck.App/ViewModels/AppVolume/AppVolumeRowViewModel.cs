using Mixdeck.App.Localization;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.App.ViewModels.AppVolume;

public sealed partial class AppVolumeRowViewModel : ObservableObject, IDisposable
{
    private readonly IAppAudioSessions _source;
    private bool _syncing;
    private bool _disposed;
    private long? _volumeEdited;
    private long? _muteEdited;
    private string? _iconPath;

    public AppVolumeRowViewModel(IAppAudioSessions source, AppAudioSession session, IAppAudioRouter? router = null)
    {
        _source = source;
        Id = session.Id;
        ProcessId = session.ProcessId;
        Router = router;
        // Sons système et processus inconnus : Windows ne permet pas de leur choisir une sortie.
        CanRoute = router is not null && session.ProcessId != 0 && !session.IsSystemSounds;
        Synchronize(session);
    }

    public string Id { get; }

    public int ProcessId { get; }

    /// <summary>Choix de la sortie de cette application (« Envoyer vers… ») ; null si indisponible.</summary>
    public IAppAudioRouter? Router { get; }

    [ObservableProperty]
    public partial string DisplayName { get; private set; } = string.Empty;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(VolumeLabel))]
    public partial double Volume { get; set; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(MuteLabel))]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial ImageSource? Icon { get; private set; }

    /// <summary>Point d'extension du routage L14 ; le bouton reste masqué jusque-là.</summary>
    [ObservableProperty]
    public partial bool CanRoute { get; set; }

    public string VolumeLabel => Volume.ToString("0", Strings.Culture) + " %";
    public string MuteLabel => (IsMuted ? Strings.Get("CS_024") : Strings.Get("CS_025")) + DisplayName;

    internal void Synchronize(AppAudioSession session)
    {
        _syncing = true;
        try
        {
            DisplayName = session.DisplayName;
            OnPropertyChanged(nameof(MuteLabel));
            if (_volumeEdited is null || Environment.TickCount64 - _volumeEdited >= 400)
                Volume = Math.Clamp(session.Volume * 100d, 0, 100);
            if (_muteEdited is null || Environment.TickCount64 - _muteEdited >= 400)
                IsMuted = session.Mute;
        }
        finally { _syncing = false; }
        if (_iconPath != session.IconPath)
        {
            _iconPath = session.IconPath;
            Icon = null;
            _ = LoadIconAsync(session.IconPath);
        }
    }

    public void Dispose() => _disposed = true;

    partial void OnVolumeChanged(double value)
    {
        if (_syncing || _disposed) return;
        _volumeEdited = Environment.TickCount64;
        _source.SetVolume(Id, (float)(Math.Clamp(value, 0, 100) / 100));
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (_syncing || _disposed) return;
        _muteEdited = Environment.TickCount64;
        _source.SetMute(Id, value);
    }

    private async Task LoadIconAsync(string? path)
    {
        var icon = await AppVolumeIconCache.GetAsync(path);
        if (!_disposed && _iconPath == path) Icon = icon;
    }
}
