using Mixdeck.Core.Abstractions;
using Mixdeck.Platform.Audio.AudioPolicy;

namespace Mixdeck.Platform.Audio;

/// <summary>Appareils audio actifs vus par Windows (API publique MMDevice).</summary>
public static class WindowsAudioDevices
{
    /// <summary>
    /// Micros et autres entrées actives, avec leur nom convivial : c'est le nom qu'attend Voicemeeter en WDM.
    /// Sert quand Voicemeeter ne liste aucune entrée (constaté avec Banana 2.1.1.9).
    /// </summary>
    public static IReadOnlyList<AudioEndpoint> GetCaptureEndpoints() => RenderEndpoints.Read(trace: null, flow: 1);
}