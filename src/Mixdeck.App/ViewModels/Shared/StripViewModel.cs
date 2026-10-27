using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>Une tranche d'entrée : Strip[0..2] physiques, Strip[3..4] virtuelles (VAIO, AUX).</summary>
public sealed partial class StripViewModel : ChannelViewModel
{
    public StripViewModel(IMixer mixer, int index)
        : base(mixer, index)
    {
        Routes = new ObservableCollection<RouteViewModel>(
            Enumerable.Range(0, BananaLayout.BusCount).Select(bus => new RouteViewModel(mixer, index, bus)));
        ApplyCustomization(null, null, true);
    }

    public bool IsVirtual => BananaLayout.IsVirtualStrip(Index);

    public bool HasEq => IsVirtual;

    public string KindText => IsVirtual ? "Entrée virtuelle" : "Entrée physique";

    /// <summary>Routage vers A1, A2, A3, B1, B2.</summary>
    public ObservableCollection<RouteViewModel> Routes { get; }

    [ObservableProperty]
    public partial bool IsSolo { get; set; }

    /// <summary>Panoramique, −0,5 (gauche) à +0,5 (droite).</summary>
    [ObservableProperty]
    public partial double PanX { get; set; }

    [ObservableProperty]
    public partial double EqLow { get; set; }

    [ObservableProperty]
    public partial double EqMid { get; set; }

    [ObservableProperty]
    public partial double EqHigh { get; set; }

    protected override string GainParameter => VmParam.StripGain(Index);

    protected override string MuteParameter => VmParam.StripMute(Index);

    protected override string DefaultName => BananaLayout.DefaultStripLabels[Index];

    // Micro pour les entrées physiques, prise pour les entrées virtuelles.
    protected override string DefaultIcon => IsVirtual ? "" : "";

    partial void OnIsSoloChanged(bool value)
    {
        if (!IsSyncing)
        {
            Mixer.SetFloat(VmParam.StripSolo(Index), VmParam.FromBool(value));
        }
    }

    partial void OnPanXChanged(double value)
    {
        if (!IsSyncing)
        {
            Mixer.SetFloat(VmParam.StripPanX(Index), (float)Math.Clamp(value, -0.5, 0.5));
        }
    }

    partial void OnEqLowChanged(double value) => WriteEq(0, value);

    partial void OnEqMidChanged(double value) => WriteEq(1, value);

    partial void OnEqHighChanged(double value) => WriteEq(2, value);

    public void Apply(StripState state)
    {
        SyncLabel(state.Label);
        SyncGain(state.Gain);
        Sync(() =>
        {
            IsMuted = state.Mute;
            IsSolo = state.Solo;
            PanX = Math.Round(state.PanX, 2);
            if (state.EqGains.Count >= BananaLayout.VirtualStripEqBands)
            {
                EqLow = state.EqGains[0];
                EqMid = state.EqGains[1];
                EqHigh = state.EqGains[2];
            }
        });

        for (var bus = 0; bus < Routes.Count && bus < state.Routes.Count; bus++)
        {
            Routes[bus].Apply(state.Routes[bus]);
        }
    }

    private void WriteEq(int band, double value)
    {
        if (!IsSyncing && HasEq)
        {
            Mixer.SetFloat(VmParam.StripEqGain(Index, band), (float)Math.Clamp(value, -12, 12));
        }
    }
}

/// <summary>Une case de la matrice de routage : entrée → bus.</summary>
public sealed partial class RouteViewModel : ObservableObject
{
    private readonly IMixer _mixer;
    private bool _syncing;

    public RouteViewModel(IMixer mixer, int strip, int bus)
    {
        _mixer = mixer;
        StripIndex = strip;
        BusIndex = bus;
    }

    public int StripIndex { get; }

    public int BusIndex { get; }

    public string BusName => BananaLayout.BusNames[BusIndex];

    public bool IsVirtualBus => BananaLayout.IsVirtualBus(BusIndex);

    [ObservableProperty]
    public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value)
    {
        if (!_syncing)
        {
            _mixer.SetFloat(VmParam.StripRoute(StripIndex, BusIndex), VmParam.FromBool(value));
        }
    }

    public void Apply(bool isOn)
    {
        _syncing = true;
        try
        {
            IsOn = isOn;
        }
        finally
        {
            _syncing = false;
        }
    }
}
