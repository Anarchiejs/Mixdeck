// ABI privée observée dans EarTrumpet (Windows 10 1803+ et Windows 11).
// Source : https://github.com/File-New-Project/EarTrumpet/tree/f492b8fa6459d9c5b442ad07ea28d70e9db7f493/EarTrumpet
// Interop/MMDeviceAPI/IAudioPolicyConfigFactoryVariantFor{Downlevel,21H2}.cs et
// Extensions/OperatingSystemExtensions.cs : le seuil « 21H2 » est 21390, PAS 19044.
// Validation sur Windows 11 23H2 (22631) à effectuer dans la session utilisateur avec la sonde L14.
namespace Mixdeck.Platform.Audio.AudioPolicy;

internal static class AudioPolicyIds
{
    internal static readonly Guid Downlevel = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");
    internal static readonly Guid Since21390 = new("ab3d4648-e242-459f-b02f-541c70306324");
    internal const string Prefix = @"\\?\SWD#MMDEVAPI#";
    internal const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    internal static Guid? ForBuild(int build) => build < 17134 ? null : build < 21390 ? Downlevel : Since21390;

    internal static bool IsEndpointId(string id) => !string.IsNullOrWhiteSpace(id)
        && !id.Any(char.IsControl) && !id.Contains('#') && !id.Contains('\\') && !id.Contains('/');

    internal static string? Pack(string? id)
    {
        if (id is null) return null;
        if (!IsEndpointId(id)) throw new ArgumentException("Identifiant de sortie audio invalide.", nameof(id));
        return Prefix + id + RenderSuffix;
    }

    internal static bool TryUnpack(string? persisted, out string? id)
    {
        id = null;
        if (string.IsNullOrEmpty(persisted)) return true;
        if (!persisted.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !persisted.EndsWith(RenderSuffix, StringComparison.OrdinalIgnoreCase)
            || persisted.Length <= Prefix.Length + RenderSuffix.Length) return false;
        var candidate = persisted[Prefix.Length..^RenderSuffix.Length];
        if (!IsEndpointId(candidate)) return false;
        id = candidate;
        return true;
    }
}
