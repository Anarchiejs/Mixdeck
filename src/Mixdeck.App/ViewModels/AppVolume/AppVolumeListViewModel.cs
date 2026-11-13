using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mixdeck.App.Services;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Audio;

namespace Mixdeck.App.ViewModels.AppVolume;

/// <summary>Suivi actif uniquement pendant l'affichage. Activate/Deactivate sont appelés sur le thread UI.</summary>
public sealed partial class AppVolumeListViewModel(Func<IAppAudioSessions> createSessions, IUiDispatcher dispatcher,
    IAppAudioRouter router)
    : ObservableObject, IDisposable
{
    private IAppAudioSessions? _source;
    private IAppAudioRouter? _routerSupported;
    public ObservableCollection<AppVolumeRowViewModel> Sessions { get; } = [];
    public ObservableCollection<AppVolumeRowViewModel> VisibleSessions { get; } = [];

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    partial void OnQueryChanged(string value) => RebuildVisible();

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    public void Activate()
    {
        if (_source is not null) return;
        _source = createSessions();
        _source.SessionsChanged += OnSessionsChanged;
        _source.Start();
        Synchronize(_source);
    }

    public void Deactivate()
    {
        var source = _source;
        _source = null;
        if (source is not null)
        {
            source.SessionsChanged -= OnSessionsChanged;
            source.Dispose();
        }
        foreach (var row in Sessions) row.Dispose();
        Sessions.Clear();
        VisibleSessions.Clear();
        IsEmpty = true;
    }

    public void Dispose() => Deactivate();

    private void OnSessionsChanged(object? sender, EventArgs args)
    {
        if (sender is IAppAudioSessions source) dispatcher.Post(() => Synchronize(source));
    }

    private void Synchronize(IAppAudioSessions source)
    {
        // Une notification déjà en file ne doit pas rouvrir le suivi après la fermeture du panneau.
        if (!ReferenceEquals(_source, source)) return;
        var snapshot = source.Sessions;
        var ids = snapshot.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
        for (var i = Sessions.Count - 1; i >= 0; i--)
        {
            if (ids.Contains(Sessions[i].Id)) continue;
            Sessions[i].Dispose();
            Sessions.RemoveAt(i);
        }
        for (var i = 0; i < snapshot.Count; i++)
        {
            var session = snapshot[i];
            var row = Sessions.FirstOrDefault(item => item.Id == session.Id);
            if (row is null)
            {
                row = new AppVolumeRowViewModel(source, session, _routerSupported ??= router.IsSupported ? router : null);
                Sessions.Insert(i, row);
            }
            else
            {
                row.Synchronize(session);
                var oldIndex = Sessions.IndexOf(row);
                if (oldIndex != i) Sessions.Move(oldIndex, i);
            }
        }
        RebuildVisible();
    }

    private void RebuildVisible()
    {
        var wanted = Sessions.Where(row => AppSessionFilter.Matches(row.DisplayName, row.ProcessName, Query)).ToList();
        for (var i = VisibleSessions.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(VisibleSessions[i])) VisibleSessions.RemoveAt(i);
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var row = wanted[i];
            var old = VisibleSessions.IndexOf(row);
            if (old < 0) VisibleSessions.Insert(i, row);
            else if (old != i) VisibleSessions.Move(old, i);
        }
        IsEmpty = wanted.Count == 0;
    }
}
