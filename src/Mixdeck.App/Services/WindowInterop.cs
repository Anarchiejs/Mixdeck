using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Mixdeck.App.Services;

/// <summary>Réglages Win32 des fenêtres que WinUI n'expose pas (panneau, OSD).</summary>
internal static partial class WindowInterop
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_LAYERED = 0x00080000;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    public static nint GetHandle(Window window) => WindowNative.GetWindowHandle(window);

    public static AppWindow GetAppWindow(Window window) =>
        AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(GetHandle(window)));

    /// <summary>Fenêtre sans barre de titre, coins arrondis, absente de la barre des tâches et d'Alt+Tab.</summary>
    public static void MakeToolWindow(Window window, bool alwaysOnTop)
    {
        var appWindow = GetAppWindow(window);
        var presenter = OverlappedPresenter.CreateForDialog();
        presenter.IsAlwaysOnTop = alwaysOnTop;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        // Bordure conservée : c'est elle qui donne les coins arrondis et l'ombre de Windows 11.
        presenter.SetBorderAndTitleBar(true, false);
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;

        var hwnd = GetHandle(window);
        AddExStyle(hwnd, WS_EX_TOOLWINDOW);
        var corner = DWMWCP_ROUND;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
    }

    /// <summary>Fenêtre qui ne prend jamais le focus et laisse passer les clics (OSD par-dessus les jeux).</summary>
    public static void MakeClickThrough(Window window) =>
        AddExStyle(GetHandle(window), WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED);

    public static uint GetDpi(Window window) => GetDpiForWindow(GetHandle(window));

    private static void AddExStyle(nint hwnd, long flags)
    {
        var style = GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        _ = SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (nint)((long)style | flags));
    }

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
