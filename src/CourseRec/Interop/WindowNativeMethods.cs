using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 視窗前景切換、游標查詢與擷取排除（<c>WDA_EXCLUDEFROMCAPTURE</c>）Win32 P/Invoke。
/// </summary>
public static partial class WindowNativeMethods
{
    public const uint WdaNone = 0x00000000;
    public const uint WdaExcludeFromCapture = 0x00000011;
    public const int SwRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowDisplayAffinity(nint hWnd, uint dwAffinity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out Point lpPoint);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hWnd);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    public static void TrySetImmersiveDarkMode(nint hwnd, bool isDark)
    {
        if (hwnd == 0)
        {
            return;
        }

        int value = isDark ? 1 : 0;
        const int immersiveDarkModeAttribute = 20;
        const int legacyImmersiveDarkModeAttribute = 19;
        const int valueSize = 4;

        if (DwmSetWindowAttribute(hwnd, immersiveDarkModeAttribute, ref value, valueSize) != 0)
        {
            DwmSetWindowAttribute(hwnd, legacyImmersiveDarkModeAttribute, ref value, valueSize);
        }
    }
}
