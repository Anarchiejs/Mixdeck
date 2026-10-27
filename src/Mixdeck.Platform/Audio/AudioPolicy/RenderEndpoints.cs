// API publique MMDevice, disponible depuis Vista (Windows SDK mmdeviceapi.h / propsys.h).
// https://learn.microsoft.com/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator
// CLSID MMDeviceEnumerator BCDE0395-E52F-467C-8E3D-C4579291692E ; IID ci-dessous issus de ces en-têtes.
// PKEY_Device_FriendlyName : a45c254e-df1c-4efd-8020-67d146a850e0, PID 14 (functiondiscoverykeys_devpkey.h).
using System.Runtime.InteropServices;
using Mixdeck.Core.Abstractions;

namespace Mixdeck.Platform.Audio.AudioPolicy;

internal static partial class RenderEndpoints
{
    // La sonde s'en sert pour vérifier qu'elle choisit réellement UNE AUTRE sortie que celle du lecteur.
    internal static string ReadDefault(AudioPolicyRole role, Action<string>? trace)
    {
        using var apartment = new PrivateApiApartment();
        var classId = new Guid("bcde0395-e52f-467c-8e3d-c4579291692e");
        var iid = typeof(IDeviceEnumerator).GUID;
        nint pointer = 0, id = 0;
        IDeviceEnumerator? enumerator = null;
        IDevice? device = null;
        try
        {
            PrivateApiNative.Check(PrivateApiNative.CoCreateInstance(classId, 0, 1, iid, out pointer), "MMDeviceEnumerator", trace);
            enumerator = (IDeviceEnumerator)Marshal.GetObjectForIUnknown(pointer);
            PrivateApiNative.Check(enumerator.GetDefaultAudioEndpoint(0, (int)role, out device), $"GetDefaultAudioEndpoint eRender {role}", trace);
            PrivateApiNative.Check(device.GetId(out id), "IMMDevice.GetId défaut", trace);
            string result = Marshal.PtrToStringUni(id) ?? throw new InvalidOperationException("Sortie par défaut sans identifiant.");
            trace?.Invoke($"Sortie Windows par défaut {role}={result}");
            return result;
        }
        finally
        {
            Marshal.FreeCoTaskMem(id);
            Release(device);
            Release(enumerator);
            if (pointer != 0) Marshal.Release(pointer);
        }
    }

    /// <param name="flow">0 = sorties (eRender), 1 = entrées (eCapture).</param>
    internal static IReadOnlyList<AudioEndpoint> Read(Action<string>? trace, int flow = 0)
    {
        using var apartment = new PrivateApiApartment();
        var classId = new Guid("bcde0395-e52f-467c-8e3d-c4579291692e");
        var iid = typeof(IDeviceEnumerator).GUID;
        nint pointer = 0;
        IDeviceEnumerator? enumerator = null;
        IDeviceCollection? collection = null;
        try
        {
            PrivateApiNative.Check(PrivateApiNative.CoCreateInstance(classId, 0, 1, iid, out pointer), $"MMDeviceEnumerator IID={iid:B}", trace);
            enumerator = (IDeviceEnumerator)Marshal.GetObjectForIUnknown(pointer);
            PrivateApiNative.Check(enumerator.EnumAudioEndpoints(flow, 1, out collection), $"EnumAudioEndpoints flow={flow} ACTIVE", trace);
            PrivateApiNative.Check(collection.GetCount(out uint count), "IMMDeviceCollection.GetCount", trace);
            var endpoints = new List<AudioEndpoint>();
            for (uint index = 0; index < count; index++)
            {
                IDevice? device = null;
                IPropertyStore? properties = null;
                nint id = 0;
                var value = new PropertyVariant();
                try
                {
                    PrivateApiNative.Check(collection.Item(index, out device), $"IMMDeviceCollection.Item({index})", trace);
                    PrivateApiNative.Check(device.GetId(out id), "IMMDevice.GetId", trace);
                    string endpointId = Marshal.PtrToStringUni(id) ?? throw new InvalidOperationException("Sortie sans identifiant.");
                    PrivateApiNative.Check(device.OpenPropertyStore(0, out properties), "OpenPropertyStore STGM_READ", trace);
                    var key = new PropertyKey { FormatId = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                    PrivateApiNative.Check(properties.GetValue(key, out value), "PKEY_Device_FriendlyName", trace);
                    string name = value.Type == 31 ? Marshal.PtrToStringUni(value.String) ?? endpointId : endpointId;
                    endpoints.Add(new(endpointId, name));
                }
                catch (Exception ex) when (PrivateApiNative.IsExpected(ex))
                {
                    // Un périphérique peut disparaître pendant l'énumération ; conserver les autres.
                    trace?.Invoke($"Sortie {index} ignorée : HRESULT=0x{ex.HResult:X8}, {ex.Message}");
                }
                finally
                {
                    PropVariantClear(ref value);
                    Marshal.FreeCoTaskMem(id);
                    Release(properties);
                    Release(device);
                }
            }
            return endpoints;
        }
        finally
        {
            Release(collection);
            Release(enumerator);
            if (pointer != 0) Marshal.Release(pointer);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null) Marshal.ReleaseComObject(value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { internal Guid FormatId; internal uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropertyVariant
    {
        [FieldOffset(0)] internal ushort Type;
        [FieldOffset(8)] internal nint String;
    }
    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropertyVariant value);

    [ComImport, Guid("a95664d2-9614-4f35-a746-de8db63617e6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint stateMask, out IDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("0bd7a1be-7a1a-44db-8397-cc5392387b5e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IDevice device);
    }
    [ComImport, Guid("d666063f-1587-4e43-81f1-b948e807363f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        [PreserveSig] int Activate(in Guid iid, uint context, nint parameters, out nint result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId(out nint id);
    }
    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(in PropertyKey key, out PropertyVariant value);
    }
}
