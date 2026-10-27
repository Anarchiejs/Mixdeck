// IID et versions : voir AudioPolicyIds.cs (EarTrumpet, commit f492b8fa6459d9c5b442ad07ea28d70e9db7f493).
// Les deux IID ont la même disposition : IUnknown (3), IInspectable (3), 19 méthodes privées,
// puis SetPersistedDefaultAudioEndpoint (25), Get... (26), ClearAll... (27).
// Appels ABI explicites : le marshalling WinRT InterfaceIsIInspectable/HString n'est plus fourni par .NET.
// Aucun slot n'est appelé avant activation ET QueryInterface de l'IID exact. Aucun essai de slot inconnu.
using System.Runtime.InteropServices;

namespace Mixdeck.Platform.Audio.AudioPolicy;

internal enum AudioPolicyRole { Console = 0, Multimedia = 1 }

internal interface IAudioPolicySession : IDisposable
{
    string? Get(int processId, AudioPolicyRole role);
    void Set(int processId, AudioPolicyRole role, string? persistedId);
}

internal sealed unsafe class AudioPolicySession : IAudioPolicySession
{
    private readonly PrivateApiApartment _apartment;
    private readonly Action<string>? _trace;
    private nint _factory;

    internal AudioPolicySession(Action<string>? trace)
    {
        _trace = trace;
        var version = Environment.OSVersion.Version;
        var iid = version.Major >= 10 ? AudioPolicyIds.ForBuild(version.Build) : null;
        if (iid is null) throw new PlatformNotSupportedException("API de routage disponible à partir de Windows 10 1803.");
        trace?.Invoke($"AudioPolicy Windows={version}, IID={iid:B}");
        _apartment = new PrivateApiApartment();
        nint activated = 0;
        try
        {
            using var classId = new PrivateApiString("Windows.Media.Internal.AudioPolicyConfig");
            PrivateApiNative.Check(PrivateApiNative.RoGetActivationFactory(classId.Handle, iid.Value, out activated), "RoGetActivationFactory", trace);
            var queryId = iid.Value;
            PrivateApiNative.Check(Marshal.QueryInterface(activated, in queryId, out _factory), $"QueryInterface {queryId:B}", trace);
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            if (activated != 0) Marshal.Release(activated);
        }
    }

    public string? Get(int processId, AudioPolicyRole role)
    {
        ObjectDisposedException.ThrowIf(_factory == 0, this);
        nint result = 0;
        try
        {
            var get = (delegate* unmanaged[Stdcall]<nint, uint, int, int, nint*, int>)(*(nint**)_factory)[26];
            PrivateApiNative.Check(get(_factory, (uint)processId, 0, (int)role, &result), $"GetPersistedDefaultAudioEndpoint pid={processId} eRender {role}", _trace);
            var value = PrivateApiString.Read(result);
            _trace?.Invoke($"Relu {role}: {value ?? "<default>"}");
            return value;
        }
        finally
        {
            PrivateApiNative.WindowsDeleteString(result);
        }
    }

    public void Set(int processId, AudioPolicyRole role, string? persistedId)
    {
        ObjectDisposedException.ThrowIf(_factory == 0, this);
        using var value = new PrivateApiString(persistedId);
        var set = (delegate* unmanaged[Stdcall]<nint, uint, int, int, nint, int>)(*(nint**)_factory)[25];
        PrivateApiNative.Check(set(_factory, (uint)processId, 0, (int)role, value.Handle),
            $"SetPersistedDefaultAudioEndpoint pid={processId} eRender {role} valeur={persistedId ?? "<default>"}", _trace);
    }

    // Déclaration conservée pour documenter l'ABI ; volontairement absente du routeur et de la CLI.
    // ClearAll efface les préférences de TOUTES les applications et n'est jamais un retour au défaut d'un PID.
    internal int ClearAllPersistedApplicationDefaultEndpoints()
    {
        ObjectDisposedException.ThrowIf(_factory == 0, this);
        var clear = (delegate* unmanaged[Stdcall]<nint, int>)(*(nint**)_factory)[27];
        return clear(_factory);
    }

    public void Dispose()
    {
        if (_factory != 0) Marshal.Release(_factory);
        _factory = 0;
        _apartment.Dispose();
    }
}
