using System.Runtime.InteropServices;

namespace Mixdeck.Platform.Audio.AudioPolicy;

internal static partial class PrivateApiNative
{
    internal const int NoInterface = unchecked((int)0x80004002);
    internal const int NotImplemented = unchecked((int)0x80004001);
    internal const int ClassNotRegistered = unchecked((int)0x80040154);
    internal static readonly Guid UnknownId = new("00000000-0000-0000-c000-000000000046");

    internal static bool IsUnavailable(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException
        or PlatformNotSupportedException || ex.HResult is NoInterface or NotImplemented or ClassNotRegistered;

    internal static bool IsExpected(Exception ex) => ex is COMException or ExternalException or ArgumentException
        or InvalidCastException or InvalidOperationException or NotSupportedException or TypeLoadException
        or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException or System.Security.SecurityException;

    internal static void Check(int hr, string operation, Action<string>? trace)
    {
        trace?.Invoke($"{operation}: HRESULT=0x{hr:X8}");
        if (hr < 0) throw new COMException(operation, hr);
    }

    [LibraryImport("combase.dll")]
    internal static partial int RoInitialize(uint type);
    [LibraryImport("combase.dll")]
    internal static partial void RoUninitialize();
    [LibraryImport("combase.dll")]
    internal static partial int RoGetActivationFactory(nint classId, in Guid iid, out nint factory);
    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int WindowsCreateString(string value, uint length, out nint result);
    [LibraryImport("combase.dll")]
    internal static partial int WindowsDeleteString(nint value);
    [LibraryImport("combase.dll")]
    internal static partial nint WindowsGetStringRawBuffer(nint value, out uint length);
    [LibraryImport("ole32.dll")]
    internal static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint result);
}

// Chaque opération crée et libère ses objets COM sur le même thread. Un appartement déjà STA est conservé.
internal sealed class PrivateApiApartment : IDisposable
{
    private bool _initialized;

    internal PrivateApiApartment()
    {
        int hr = PrivateApiNative.RoInitialize(1);
        if (hr == unchecked((int)0x80010106)) return; // RPC_E_CHANGED_MODE : COM déjà initialisé en STA.
        PrivateApiNative.Check(hr, "RoInitialize", null);
        _initialized = true;
    }

    public void Dispose()
    {
        if (!_initialized) return;
        _initialized = false;
        PrivateApiNative.RoUninitialize();
    }
}

internal sealed class PrivateApiString : IDisposable
{
    internal nint Handle { get; }

    internal PrivateApiString(string? value)
    {
        if (value is null) return; // HSTRING nul = sortie par défaut, pas une remise à zéro globale.
        PrivateApiNative.Check(PrivateApiNative.WindowsCreateString(value, (uint)value.Length, out var handle), "WindowsCreateString", null);
        Handle = handle;
    }

    internal static string? Read(nint handle)
    {
        if (handle == 0) return null;
        var buffer = PrivateApiNative.WindowsGetStringRawBuffer(handle, out uint length);
        return length == 0 ? null : Marshal.PtrToStringUni(buffer, checked((int)length));
    }

    public void Dispose() => PrivateApiNative.WindowsDeleteString(Handle);
}
