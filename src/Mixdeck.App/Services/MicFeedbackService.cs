using System.ComponentModel;
using System.Runtime.InteropServices;
using Mixdeck.App.ViewModels.Shared;
using Mixdeck.App.Views.Osd;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Settings;
using Mixdeck.Platform.Taskbar;

namespace Mixdeck.App.Services;

/// <summary>Badge et sons du micro, suivis même quand toutes les fenêtres sont fermées.</summary>
internal sealed partial class MicFeedbackService : IDisposable
{
    private readonly MixerViewModel _mixer;
    private readonly ISettingsStore _settings;
    private readonly IUiDispatcher _ui;
    private readonly ITaskbarLocator _taskbar;
    private readonly ThemeService _theme;
    private OsdWindow? _badge;
    private bool _lastMuted;
    private bool _wasConnected;
    private bool _disposed;

    public MicFeedbackService(MixerViewModel mixer, ISettingsStore settings, IUiDispatcher ui,
        ITaskbarLocator taskbar, ThemeService theme)
    {
        _mixer = mixer;
        _settings = settings;
        _ui = ui;
        _taskbar = taskbar;
        _theme = theme;
        _lastMuted = mixer.IsMicMuted;
        _wasConnected = mixer.CanToggleMic;
        mixer.PropertyChanged += OnMixerChanged;
        settings.Changed += OnSettingsChanged;
        Refresh();
    }

    private void OnMixerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MixerViewModel.IsMicMuted) or nameof(MixerViewModel.Mic)
            or nameof(MixerViewModel.ConnectionState) or nameof(MixerViewModel.HasMic)
            or nameof(MixerViewModel.CanToggleMic))) return;

        var muted = _mixer.IsMicMuted;
        if (_mixer.CanToggleMic && _wasConnected && muted != _lastMuted && _settings.Current.PlayMicSounds)
        {
            // SND_ASYNC | SND_NODEFAULT | SND_ALIAS : lecture Windows sans bloquer l'interface.
            _ = PlaySoundW(muted ? "SystemHand" : "SystemAsterisk", 0, 0x00010003);
        }
        _lastMuted = muted;
        _wasConnected = _mixer.CanToggleMic;
        Refresh();
    }

    private void OnSettingsChanged(object? sender, AppSettings settings) => _ui.Post(() =>
    {
        if (!_disposed) Refresh();
    });

    private void Refresh()
    {
        if (_settings.Current.ShowMicBadge && _mixer.CanToggleMic && _mixer.IsMicMuted)
        {
            _badge ??= new OsdWindow(_taskbar, _theme);
            _badge.ShowMicBadge();
        }
        else
        {
            _badge?.Hide();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mixer.PropertyChanged -= OnMixerChanged;
        _settings.Changed -= OnSettingsChanged;
        _badge?.Close();
        _badge = null;
    }

    [LibraryImport("winmm.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySoundW(string sound, nint module, uint flags);
}
