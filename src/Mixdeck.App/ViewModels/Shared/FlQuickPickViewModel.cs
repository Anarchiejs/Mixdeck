using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.App.Services;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>
/// Choix rapide de la chaîne d'effets du micro dans le panneau : seules les pistes FL qui portent des plugins sont
/// proposées. Un bouton = l'interrupteur vert de la piste dans le mixeur de FL (inverse du muet) : allumer une piste
/// coupe les autres ; rappuyer sur la piste allumée la coupe.
/// </summary>
public sealed partial class FlQuickPickViewModel : ObservableObject
{
    private readonly IDaw _daw;
    private readonly IUiDispatcher _ui;

    public FlQuickPickViewModel(IDaw daw, IUiDispatcher ui)
    {
        _daw = daw; _ui = ui;
        // Vit aussi longtemps que le panneau, donc que l'appli : abonnement sans désabonnement.
        daw.StateChanged += (_, _) => PostSync();
        daw.TrackChanged += (_, _) => PostSync();
        Sync();
    }

    public ObservableCollection<FlQuickTrackViewModel> Tracks { get; } = [];
    [ObservableProperty] public partial bool IsVisible { get; private set; }

    private void PostSync() => _ui.Post(Sync);

    private void Sync()
    {
        var connected = _daw.State == DawConnectionState.Connected;
        var snapshot = connected
            ? _daw.Tracks.Values.Where(t => t.Slots.Count > 0).OrderBy(t => t.Index).ToArray()
            : [];
        var indexes = snapshot.Select(t => t.Index).ToHashSet();
        foreach (var row in Tracks.Where(t => !indexes.Contains(t.Index)).ToArray()) Tracks.Remove(row);
        foreach (var track in snapshot)
        {
            var row = Tracks.FirstOrDefault(t => t.Index == track.Index);
            if (row is null) { row = new(this, track.Index); Tracks.Insert(Tracks.TakeWhile(t => t.Index < track.Index).Count(), row); }
            row.Update(track.Index == 0 ? "Master" : track.Name, !track.Muted);
        }
        IsVisible = Tracks.Count > 0;
    }

    internal void Toggle(int track)
    {
        var target = Tracks.FirstOrDefault(t => t.Index == track);
        if (target is null) return;
        // Piste déjà allumée : on la coupe. Sinon on l'allume et on coupe toutes les autres.
        var turnOn = !target.Active;
        foreach (var row in Tracks)
        {
            var active = turnOn ? row.Index == track : row.Active && row.Index != track;
            if (active != row.Active) _daw.SetMute(row.Index, !active);
            // Affichage immédiat ; l'état renvoyé par FL recale ensuite les boutons.
            row.Update(row.Name, active);
        }
        // Un clic sur un bouton le bascule à l'écran même si Active ne change pas : forcer le réaffichage.
        foreach (var row in Tracks) row.Refresh();
    }
}

public sealed partial class FlQuickTrackViewModel(FlQuickPickViewModel owner, int index) : ObservableObject
{
    public int Index => index;
    [ObservableProperty] public partial string Name { get; private set; } = "";
    [ObservableProperty] public partial bool Active { get; private set; }
    public void Update(string name, bool active) { Name = name; Active = active; }
    public void Refresh() => OnPropertyChanged(nameof(Active));
    [RelayCommand] private void Toggle() => owner.Toggle(index);
}
