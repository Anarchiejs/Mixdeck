using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Mixdeck.Platform.Interop;

internal static partial class Native
{
    internal delegate nint WindowProc(nint window, uint message, nuint wParam, nint lParam);
    internal delegate nint KeyboardProc(int code, nuint wParam, nint lParam);
    internal delegate void WinEventProc(nint hook, uint eventId, nint window, int objectId, int childId, uint threadId, uint time);
    internal delegate bool EnumWindowProc(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { internal nint Window; internal uint Id; internal nuint WParam; internal nint LParam; internal uint Time; internal Point Point; internal uint Private; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        internal uint Size, Style;
        internal nint Procedure;
        internal int ClassExtra, WindowExtra;
        internal nint Instance, Icon, Cursor, Background, MenuName, ClassName, SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData { internal uint VirtualKey, ScanCode, Flags, Time; internal nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal uint Size; internal Rect Monitor, Work; internal uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct AppBarData { internal uint Size; internal nint Window; internal uint Callback, Edge; internal Rect Rect; internal nint Param; }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)] internal static partial ushort RegisterClassEx(ref WindowClass value);
    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool UnregisterClass(string name, nint instance);
    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)] internal static partial nint CreateWindowEx(uint extendedStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool DestroyWindow(nint window);
    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")] internal static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)] internal static partial int GetMessage(out Message message, nint window, uint min, uint max);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool TranslateMessage(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static partial nint DispatchMessage(in Message message);
    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] internal static partial void PostQuitMessage(int exitCode);
    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)] internal static partial nint GetModuleHandle(string? name);
    [LibraryImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool UnregisterHotKey(nint window, int id);
    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] internal static partial nint SetWindowsHookEx(int type, nint callback, nint module, uint threadId);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool UnhookWindowsHookEx(nint hook);
    [LibraryImport("user32.dll")] internal static partial nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [LibraryImport("user32.dll")] internal static partial short GetAsyncKeyState(int key);
    [LibraryImport("user32.dll", SetLastError = true)] internal static partial nint SetWinEventHook(uint min, uint max, nint module, nint callback, uint processId, uint threadId, uint flags);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool UnhookWinEvent(nint hook);
    [LibraryImport("user32.dll")] internal static partial nint GetForegroundWindow();
    [LibraryImport("user32.dll")] internal static partial uint GetWindowThreadProcessId(nint window, out uint processId);
    [LibraryImport("kernel32.dll", SetLastError = true)] internal static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* path, ref uint size);
    [LibraryImport("user32.dll")] internal static partial nint MonitorFromPoint(Point point, uint flags);
    [LibraryImport("user32.dll")] internal static partial nint MonitorFromWindow(nint window, uint flags);
    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [LibraryImport("shcore.dll")] internal static partial int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [LibraryImport("shell32.dll")] internal static partial nuint SHAppBarMessage(uint message, ref AppBarData data);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool EnumWindows(nint callback, nint parameter);
    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")] internal static unsafe partial int GetClassName(nint window, char* name, int count);
    [LibraryImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static partial bool GetWindowRect(nint window, out Rect rect);
}
