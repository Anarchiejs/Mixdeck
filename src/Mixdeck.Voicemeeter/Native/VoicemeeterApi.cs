using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mixdeck.Voicemeeter.Native;

public sealed partial class VoicemeeterApi : IVoicemeeterApi
{
    private const string Library = "Mixdeck.Voicemeeter.Remote";

    static VoicemeeterApi() => NativeLibrary.SetDllImportResolver(typeof(VoicemeeterApi).Assembly, ResolveLibrary);

    private static nint ResolveLibrary(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        var directory = VoicemeeterLocator.FindInstallationDirectory();
        if (directory is not null && NativeLibrary.TryLoad(Path.Combine(directory, VoicemeeterLocator.LibraryFileName), out var handle))
            return handle;
        throw new DllNotFoundException("La DLL Remote de Voicemeeter est introuvable.");
    }

    public int Login() => LoginNative();
    public int Logout() => LogoutNative();
    public int RunVoicemeeter(int type) => RunNative(type);
    public int GetVoicemeeterType(out int type) => TypeNative(out type);
    public int GetVoicemeeterVersion(out int version) => VersionNative(out version);
    public int IsParametersDirty() => DirtyNative();
    public int GetParameterFloat(string name, out float value) => GetFloatNative(name, out value);
    public unsafe int GetParameterString(string name, out string value)
    {
        char* buffer = stackalloc char[512];
        new Span<char>(buffer, 512).Clear();
        var result = GetStringNative(name, buffer);
        var length = 0;
        while (length < 512 && buffer[length] != '\0') length++;
        value = result == 0 ? new string(buffer, 0, length) : string.Empty;
        return result;
    }
    public int SetParameterFloat(string name, float value) => SetFloatNative(name, value);
    public int SetParameterString(string name, string value) => SetStringNative(name, value);
    public int SetParameters(string script) => ScriptNative(script);
    public int GetLevel(int type, int channel, out float value) => LevelNative(type, channel, out value);
    public int InputGetDeviceNumber() => InputNumberNative();
    public int OutputGetDeviceNumber() => OutputNumberNative();
    public int InputGetDeviceDesc(int index, out int type, out string name, out string hardwareId) =>
        GetDeviceDesc(true, index, out type, out name, out hardwareId);
    public int OutputGetDeviceDesc(int index, out int type, out string name, out string hardwareId) =>
        GetDeviceDesc(false, index, out type, out name, out hardwareId);

    private static unsafe int GetDeviceDesc(bool input, int index, out int type, out string name, out string hardwareId)
    {
        char* nameBuffer = stackalloc char[256];
        char* idBuffer = stackalloc char[256];
        new Span<char>(nameBuffer, 256).Clear();
        new Span<char>(idBuffer, 256).Clear();
        var result = input ? InputDescNative(index, out type, nameBuffer, idBuffer)
            : OutputDescNative(index, out type, nameBuffer, idBuffer);
        name = result == 0 ? BoundedString(nameBuffer) : string.Empty;
        hardwareId = result == 0 ? BoundedString(idBuffer) : string.Empty;
        return result;
    }

    private static unsafe string BoundedString(char* buffer)
    {
        var length = 0;
        while (length < 256 && buffer[length] != '\0') length++;
        return new string(buffer, 0, length);
    }

    [LibraryImport(Library, EntryPoint = "VBVMR_Input_GetDeviceNumber")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int InputNumberNative();
    [LibraryImport(Library, EntryPoint = "VBVMR_Output_GetDeviceNumber")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int OutputNumberNative();
    [LibraryImport(Library, EntryPoint = "VBVMR_Input_GetDeviceDescW", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe partial int InputDescNative(int index, out int type, char* name, char* hardwareId);
    [LibraryImport(Library, EntryPoint = "VBVMR_Output_GetDeviceDescW", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe partial int OutputDescNative(int index, out int type, char* name, char* hardwareId);

    [LibraryImport(Library, EntryPoint = "VBVMR_Login")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int LoginNative();
    [LibraryImport(Library, EntryPoint = "VBVMR_Logout")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int LogoutNative();
    [LibraryImport(Library, EntryPoint = "VBVMR_RunVoicemeeter")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int RunNative(int type);
    [LibraryImport(Library, EntryPoint = "VBVMR_GetVoicemeeterType")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int TypeNative(out int type);
    [LibraryImport(Library, EntryPoint = "VBVMR_GetVoicemeeterVersion")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int VersionNative(out int version);
    [LibraryImport(Library, EntryPoint = "VBVMR_IsParametersDirty")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int DirtyNative();
    [LibraryImport(Library, EntryPoint = "VBVMR_GetParameterFloat")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int GetFloatNative([MarshalAs(UnmanagedType.LPStr)] string name, out float value);
    [LibraryImport(Library, EntryPoint = "VBVMR_GetParameterStringW")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe partial int GetStringNative([MarshalAs(UnmanagedType.LPStr)] string name, char* value);
    [LibraryImport(Library, EntryPoint = "VBVMR_SetParameterFloat")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int SetFloatNative([MarshalAs(UnmanagedType.LPStr)] string name, float value);
    [LibraryImport(Library, EntryPoint = "VBVMR_SetParameterStringW")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int SetStringNative([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string value);
    [LibraryImport(Library, EntryPoint = "VBVMR_SetParametersW", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int ScriptNative(string script);
    [LibraryImport(Library, EntryPoint = "VBVMR_GetLevel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int LevelNative(int type, int channel, out float value);
}
