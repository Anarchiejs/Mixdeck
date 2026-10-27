using CommunityToolkit.Mvvm.ComponentModel;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>Un bus de sortie : A1-A3 physiques, B1-B2 virtuels.</summary>
public sealed partial class BusViewModel : ChannelViewModel
{
    public BusViewModel(IMixer mixer, int index)
        : base(mixer, index)
    {
        ApplyCustomization(null, null, index == 0);
    }

    /// <summary>A1, A2, A3, B1, B2.</summary>
    public string BusName => BananaLayout.BusNames[Index];

    public bool IsVirtual => BananaLayout.IsVirtualBus(Index);

    public string KindText => IsVirtual ? "Sortie virtuelle" : "Sortie physique";

    [ObservableProperty]
    public partial bool IsMono { get; set; }

    [ObservableProperty]
    public partial bool EqOn { get; set; }

    protected override string GainParameter => VmParam.BusGain(Index);

    protected override string MuteParameter => VmParam.BusMute(Index);

    protected override string DefaultName => BusName;

    // Casque pour les sorties physiques, flux réseau pour les sorties virtuelles.
    protected override string DefaultIcon => IsVirtual ? "" : "";

    partial void OnIsMonoChanged(bool value)
    {
        if (!IsSyncing)
        {
            Mixer.SetFloat(VmParam.BusMono(Index), VmParam.FromBool(value));
        }
    }

    partial void OnEqOnChanged(bool value)
    {
        if (!IsSyncing)
        {
            Mixer.SetFloat(VmParam.BusEqOn(Index), VmParam.FromBool(value));
        }
    }

    public void Apply(BusState state)
    {
        SyncLabel(state.Label);
        SyncGain(state.Gain);
        Sync(() =>
        {
            IsMuted = state.Mute;
            IsMono = state.Mono;
            EqOn = state.EqOn;
        });
    }
}
