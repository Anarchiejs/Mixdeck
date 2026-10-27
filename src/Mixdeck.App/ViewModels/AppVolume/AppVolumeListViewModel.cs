using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mixdeck.App.Services;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.App.ViewModels.AppVolume;

/// <summary>Suivi actif uniquement pendant l'affichage. Activate/Deactivate sont appelés sur le thread UI.</summary>
public sealed partial class AppVolumeListViewModel(Func<IAppAudioSessions> createSessions, IUiDispatcher dispatcher)
    : ObservableObject, IDisposable
{
    private IAppAudioSessions? _source;
    public ObservableCollection<AppVolumeRowViewModel> Sessions { get; } = [];

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
                row = new AppVolumeRowViewModel(source, session);
                Sessions.Insert(i, row);
            }
            else
            {
                row.Synchronize(session);
                var oldIndex = Sessions.IndexOf(row);
                if (oldIndex != i) Sessions.Move(oldIndex, i);
            }
        }
        IsEmpty = Sessions.Count == 0;
    }
}
