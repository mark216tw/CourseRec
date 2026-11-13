using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 全域快捷鍵（<c>RegisterHotKey</c>）與視窗子類別化（<c>SetWindowSubclass</c>）Win32 P/Invoke。
/// </summary>
public static partial class HotkeyNativeMethods
{
    public const uint WmHotkey = 0x0312;
    public const uint WmNcdestroy = 0x0082;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    public delegate nint SubclassProc(
        nint hWnd,
        uint uMsg,
        nuint wParam,
        nint lParam,
        nuint uIdSubclass,
        nuint dwRefData);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass,
        nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc pfnSubclass,
        nuint uIdSubclass);

    [LibraryImport("comctl32.dll")]
    public static partial nint DefSubclassProc(nint hWnd, uint uMsg, nuint wParam, nint lParam);
}
