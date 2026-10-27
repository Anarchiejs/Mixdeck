using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mixdeck.Core.Abstractions;
using Mixdeck.Core.Audio;
using Mixdeck.Core.Model;

namespace Mixdeck.App.ViewModels.Shared;

/// <summary>
/// Base commune d'une tranche d'entrée et d'un bus : gain, muet, nom, icône, vumètre.
/// Les écritures partent vers le mixeur ; les relectures de Voicemeeter reviennent par <see cref="Sync"/>
/// sans renvoyer d'écriture.
/// </summary>
public abstract partial class ChannelViewModel : ObservableObject
{
    // Pendant un glissement du curseur, une relecture plus ancienne ne doit pas faire sauter la valeur.
    private const long LocalEditGraceMilliseconds = 400;

    private readonly MeterBallistics _left = new();
    private readonly MeterBallistics _right = new();
    private long _lastGainEdit;
    private bool _syncing;

    protected ChannelViewModel(IMixer mixer, int index, MixerLayout? layout = null)
    {
        Mixer = mixer;
        Layout = layout ?? mixer.Layout;
        Index = index;
        DisplayName = string.Empty;
        Icon = string.Empty;
        Gain = 0;
    }

    protected IMixer Mixer { get; }

    protected MixerLayout Layout { get; }

    public int Index { get; }

    /// <summary>Nom affiché : nom personnalisé, sinon libellé Voicemeeter, sinon nom par défaut.</summary>
    [ObservableProperty]
    public partial string DisplayName { get; set; }

    /// <summary>Glyphe Segoe Fluent Icons.</summary>
    [ObservableProperty]
    public partial string Icon { get; set; }

    /// <summary>Gain en dB, −60 à +12.</summary>
    [ObservableProperty]
    public partial double Gain { get; set; }

    [ObservableProperty]
    public partial bool IsMuted { get; set; }

    [ObservableProperty]
    public partial bool ShowInFlyout { get; set; } = true;

    [ObservableProperty]
    public partial double LeftDb { get; set; } = -60;

    [ObservableProperty]
    public partial double RightDb { get; set; } = -60;

    [ObservableProperty]
    public partial double LeftPeakDb { get; set; } = -60;

    [ObservableProperty]
    public partial double RightPeakDb { get; set; } = -60;

    [ObservableProperty]
    public partial bool IsClipped { get; set; }

    /// <summary>Libellé lu dans Voicemeeter (peut être vide).</summary>
    public string VoicemeeterLabel { get; protected set; } = string.Empty;

    /// <summary>Levé quand l'utilisateur change le nom, l'icône ou la visibilité dans le panneau.</summary>
    public event EventHandler? CustomizationChanged;

    protected abstract string GainParameter { get; }

    protected abstract string MuteParameter { get; }

    protected abstract string DefaultName { get; }

    protected abstract string DefaultIcon { get; }

    /// <summary>Vrai quand la valeur vient de Voicemeeter (aucune écriture ne doit repartir).</summary>
    protected bool IsSyncing => _syncing;

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void ResetGain() => Gain = 0;

    partial void OnGainChanged(double value)
    {
        if (_syncing)
        {
            return;
        }

        _lastGainEdit = Environment.TickCount64;
        Mixer.SetFloat(GainParameter, Layout.ClampGain((float)value));
    }

    partial void OnIsMutedChanged(bool value)
    {
        if (!_syncing)
        {
            Mixer.SetFloat(MuteParameter, VmParam.FromBool(value));
        }
    }

    partial void OnDisplayNameChanged(string value)
    {
        if (_syncing)
        {
            return;
        }

        // Un nom vide revient au nom par défaut.
        HasCustomName = !string.IsNullOrWhiteSpace(value) && value != ResolveDefaultName();
        if (string.IsNullOrWhiteSpace(value))
        {
            Sync(() => DisplayName = ResolveDefaultName());
        }

        CustomizationChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnIconChanged(string value)
    {
        if (!_syncing)
        {
            HasCustomIcon = value != DefaultIcon;
            CustomizationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnShowInFlyoutChanged(bool value)
    {
        if (!_syncing)
        {
            CustomizationChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Applique les personnalisations enregistrées (nom, icône, visibilité).</summary>
    public void ApplyCustomization(string? displayName, string? icon, bool showInFlyout)
    {
        Sync(() =>
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ResolveDefaultName() : displayName;
            Icon = string.IsNullOrEmpty(icon) ? DefaultIcon : icon;
            ShowInFlyout = showInFlyout;
        });
        HasCustomName = !string.IsNullOrWhiteSpace(displayName);
        HasCustomIcon = !string.IsNullOrEmpty(icon);
    }

    /// <summary>Le nom affiché a été choisi par l'utilisateur (sinon il suit le libellé de Voicemeeter).</summary>
    public bool HasCustomName { get; private set; }

    public bool HasCustomIcon { get; private set; }

    /// <summary>Nom et icône à enregistrer (null = valeur par défaut).</summary>
    public (string? Name, string? Icon) GetCustomization() =>
        (HasCustomName ? DisplayName : null, HasCustomIcon ? Icon : null);

    /// <summary>Met à jour la balistique du vumètre ; appelé à chaque image quand les vumètres sont visibles.</summary>
    public void UpdateMeter(StereoLevel level, TimeSpan elapsed)
    {
        _left.Update(level.Left, elapsed);
        _right.Update(level.Right, elapsed);
        LeftDb = _left.LevelDb;
        RightDb = _right.LevelDb;
        LeftPeakDb = _left.PeakHoldDb;
        RightPeakDb = _right.PeakHoldDb;
        IsClipped = _left.Clipped || _right.Clipped;
    }

    public void ResetMeter()
    {
        _left.Reset();
        _right.Reset();
        LeftDb = RightDb = LeftPeakDb = RightPeakDb = -60;
        IsClipped = false;
    }

    protected void SyncGain(float gain)
    {
        if (Environment.TickCount64 - _lastGainEdit < LocalEditGraceMilliseconds)
        {
            return;
        }

        Sync(() => Gain = Math.Round(gain, 1));
    }

    protected void SyncLabel(string label)
    {
        VoicemeeterLabel = label;
        if (!HasCustomName)
        {
            Sync(() => DisplayName = ResolveDefaultName());
        }
    }

    protected void Sync(Action apply)
    {
        _syncing = true;
        try
        {
            apply();
        }
        finally
        {
            _syncing = false;
        }
    }

    private string ResolveDefaultName() =>
        string.IsNullOrWhiteSpace(VoicemeeterLabel) ? DefaultName : VoicemeeterLabel;
}
