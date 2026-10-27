using System.Globalization;
using Mixdeck.Core.Model;
using Mixdeck.Core.Settings;

namespace Mixdeck.Core.Audio;

public sealed record MicPresetDefinition(MicPreset Preset, string Name, string Description,
    float Gate, float Comp, float ColorX, float ColorY);

/// <summary>Valeurs communes à l’interface et aux scripts Remote de Voicemeeter.</summary>
public static class MicPresets
{
    // Intellipan « Voice Color » est disponible sur les entrées physiques de Banana 2.x.
    // Color_x positif éclaircit la voix ; Color_y reste à zéro pour éviter l’effet spatial.
    // Denoiser et les réglages détaillés du compresseur appartiennent à Potato (version 3).
    // Référence : https://download.vb-audio.com/Download_CABLE/VoicemeeterRemoteAPI.pdf, liste des paramètres.
    public static IReadOnlyList<MicPresetDefinition> All { get; } = Array.AsReadOnly<MicPresetDefinition>(
    [
        new(MicPreset.Off, "Aucun", "Désactive la porte de bruit, la compression et la couleur de la voix.", 0, 0, 0, 0),
        new(MicPreset.Light, "Léger", "Réduit doucement les bruits faibles et lisse les écarts de voix.", 1, 1.5f, 0, 0),
        new(MicPreset.Voice, "Voix", "Atténue les bruits entre les phrases et rend la voix plus claire et régulière.", 2, 3, 0.15f, 0),
        new(MicPreset.Strong, "Fort", "Pour une pièce bruyante : porte de bruit marquée et voix plus présente.", 4, 5, 0.2f, 0),
    ]);

    public static string CreateScript(int strip, MicPreset preset, MixerLayout? layout = null)
    {
        layout ??= MixerLayout.Banana;
        if (strip < 0 || strip >= layout.PhysicalStripCount) throw new ArgumentOutOfRangeException(nameof(strip));
        var definition = All.FirstOrDefault(p => p.Preset == preset) ?? throw new ArgumentOutOfRangeException(nameof(preset));
        // Standard expose la couleur Intellipan, mais ni porte de bruit ni compresseur.
        if (layout.VoicemeeterType == 1)
            return string.Create(CultureInfo.InvariantCulture,
                $"{VmParam.StripColorX(strip)}={definition.ColorX};{VmParam.StripColorY(strip)}={definition.ColorY};");
        return string.Create(CultureInfo.InvariantCulture,
            $"{VmParam.StripGate(strip)}={definition.Gate};{VmParam.StripComp(strip)}={definition.Comp};{VmParam.StripColorX(strip)}={definition.ColorX};{VmParam.StripColorY(strip)}={definition.ColorY};");
    }
}
