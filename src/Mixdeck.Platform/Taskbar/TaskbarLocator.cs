using System.ComponentModel;
using System.Runtime.InteropServices;
using Mixdeck.Platform.Interop;

namespace Mixdeck.Platform.Taskbar;

public sealed class TaskbarLocator : ITaskbarLocator
{
    public TaskbarPlacement GetPrimary() => GetForMonitor(Native.MonitorFromPoint(new Native.Point(0, 0), 1));
    public TaskbarPlacement GetForPoint(int x, int y) => GetForMonitor(Native.MonitorFromPoint(new Native.Point(x, y), 2));

    private static TaskbarPlacement GetForMonitor(nint monitor)
    {
        Native.MonitorInfo info = new() { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastPInvokeError());
        uint dpi = 96;
        if (Native.GetDpiForMonitor(monitor, 0, out uint x, out _) >= 0) dpi = Math.Max(96, x);
        Native.AppBarData data = new() { Size = (uint)Marshal.SizeOf<Native.AppBarData>() };
        if ((Native.SHAppBarMessage(4, ref data) & 1) != 0) return Placement(default, TaskbarEdge.Bottom);
        // ABM_GETTASKBARPOS renseigne le rectangle, mais ne garantit pas hWnd.
        if (Native.SHAppBarMessage(5, ref data) != 0 && Native.MonitorFromPoint(new Native.Point(data.Rect.Left + (data.Rect.Right - data.Rect.Left) / 2, data.Rect.Top + (data.Rect.Bottom - data.Rect.Top) / 2), 2) == monitor)
            return Placement(ToPixel(data.Rect), Edge(data.Rect, info.Monitor));

        PixelRect bounds = default;
        Native.EnumWindowProc callback = (window, _) =>
        {
            if (IsSecondary(window) && Native.MonitorFromWindow(window, 2) == monitor && Native.GetWindowRect(window, out var rect))
            {
                bounds = ToPixel(rect);
                return false;
            }
            return true;
        };
        Native.EnumWindows(Marshal.GetFunctionPointerForDelegate(callback), 0);
        GC.KeepAlive(callback);
        var nativeBounds = new Native.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        return Placement(bounds, bounds.Width > 0 && bounds.Height > 0 ? Edge(nativeBounds, info.Monitor) : TaskbarEdge.Bottom);

        TaskbarPlacement Placement(PixelRect rect, TaskbarEdge edge) => new(edge, rect, ToPixel(info.Work), ToPixel(info.Monitor), dpi);
    }

    private static unsafe bool IsSecondary(nint window)
    {
        char* name = stackalloc char[64];
        int length = Native.GetClassName(window, name, 64);
        return new ReadOnlySpan<char>(name, length).SequenceEqual("Shell_SecondaryTrayWnd");
    }

    private static PixelRect ToPixel(Native.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);
    private static TaskbarEdge Edge(Native.Rect rect, Native.Rect monitor) => rect.Right - rect.Left >= rect.Bottom - rect.Top
        ? (Math.Abs(rect.Top - monitor.Top) <= Math.Abs(rect.Bottom - monitor.Bottom) ? TaskbarEdge.Top : TaskbarEdge.Bottom)
        : (Math.Abs(rect.Left - monitor.Left) <= Math.Abs(rect.Right - monitor.Right) ? TaskbarEdge.Left : TaskbarEdge.Right);
}
