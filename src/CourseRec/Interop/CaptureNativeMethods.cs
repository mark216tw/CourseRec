using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 螢幕顯示器／視窗枚舉與 <see cref="GraphicsCaptureItem"/> 建立之原生 Win32 / WinRT 互通方法。
/// </summary>
public static unsafe partial class CaptureNativeMethods
{
    public const int GwlExStyle = -20;
    public const long WsExToolWindow = 0x00000080L;
    public const uint DwmaCloaked = 14;

    private static readonly Guid IidGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IidGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    public delegate bool MonitorEnumProc(nint hMonitor, nint hdcMonitor, ref Rect lprcMonitor, nint dwData);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Math.Max(0, Right - Left);

        public readonly int Height => Math.Max(0, Bottom - Top);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoExW
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
        public fixed char DeviceName[32];
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfnEnum, nint dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(nint hMonitor, ref MonitorInfoExW lpmi);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out Rect lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, int length, out nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint hstring);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    public static GraphicsCaptureItem? TryCreateItemForWindow(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
        {
            return null;
        }

        nint factory = GetCaptureInteropFactory();
        if (factory == 0)
        {
            return null;
        }

        try
        {
            Guid itemIid = IidGraphicsCaptureItem;
            nint itemPtr = 0;
            var createForWindow = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[3];
            int hr = createForWindow(factory, hwnd, &itemIid, &itemPtr);
            if (hr < 0 || itemPtr == 0)
            {
                return null;
            }

            try
            {
                return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                ReleaseComPointer(itemPtr);
            }
        }
        finally
        {
            ReleaseComPointer(factory);
        }
    }

    public static GraphicsCaptureItem? TryCreateItemForMonitor(nint hMonitor)
    {
        if (hMonitor == 0)
        {
            return null;
        }

        nint factory = GetCaptureInteropFactory();
        if (factory == 0)
        {
            return null;
        }

        try
        {
            Guid itemIid = IidGraphicsCaptureItem;
            nint itemPtr = 0;
            var createForMonitor = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(nint**)factory)[4];
            int hr = createForMonitor(factory, hMonitor, &itemIid, &itemPtr);
            if (hr < 0 || itemPtr == 0)
            {
                return null;
            }

            try
            {
                return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);
            }
            finally
            {
                ReleaseComPointer(itemPtr);
            }
        }
        finally
        {
            ReleaseComPointer(factory);
        }
    }

    private static nint GetCaptureInteropFactory()
    {
        const string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
        int hr = WindowsCreateString(className, className.Length, out nint hstring);
        if (hr < 0 || hstring == 0)
        {
            return 0;
        }

        try
        {
            hr = RoGetActivationFactory(hstring, in IidGraphicsCaptureItemInterop, out nint factory);
            return hr >= 0 ? factory : 0;
        }
        finally
        {
            WindowsDeleteString(hstring);
        }
    }

    internal static void ReleaseComPointer(nint ptr)
    {
        if (ptr != 0)
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)ptr)[2];
            release(ptr);
        }
    }
}
