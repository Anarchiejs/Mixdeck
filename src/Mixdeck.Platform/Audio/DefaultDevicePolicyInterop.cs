using System.Runtime.InteropServices;

namespace Mixdeck.Platform.Audio;

// Independent COM declaration of the undocumented Windows 7+ IPolicyConfig ABI.
// ResetDeviceFormat occupies slot 5; SetDefaultEndpoint is slot 13 (IUnknown included).
// This is not derived from any GPL project. The private API can fail on future Windows versions;
// failures are surfaced by the shared view model. See NOTICE.
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class DefaultDevicePolicyClient;

[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDefaultDevicePolicy
{
    [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out nint format);
    [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int defaults, out nint format);
    [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, nint endpoint, nint mix);
    [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int defaults, out long period, out long minimum);
    [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, in long period);
    [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);
    [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, nint mode);
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, in PropertyKey key, nint value);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, in PropertyKey key, nint value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
}

internal static class DefaultDeviceRoles
{
    // ERole from mmdeviceapi.h: eConsole=0, eMultimedia=1, eCommunications=2.
    internal static void Apply(string id, Func<string, int, int> set)
    {
        List<Exception> errors = [];
        for (var role = 0; role < 3; role++)
        {
            var hr = set(id, role);
            if (hr < 0) errors.Add(new COMException($"SetDefaultEndpoint role={role}", hr));
        }
        // Attempt every role even on a partial failure. A subsequent read restores the actual selection.
        if (errors.Count > 0) throw errors[0];
    }
}
