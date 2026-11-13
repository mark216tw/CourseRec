using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 擷取視窗與系統顯示器圖示所需的 Win32 / GDI / Shell P/Invoke。
/// </summary>
public static unsafe partial class CaptureIconNativeMethods
{
    public const uint WmGetIcon = 0x007F;
    public const nint IconSmall = 0;
    public const nint IconBig = 1;
    public const nint IconSmall2 = 2;
    public const int GclHicon = -14;
    public const int GclHiconSm = -34;
    public const uint SmtoBlock = 0x0001;
    public const uint SmtoAbortIfHung = 0x0002;

    public const uint ShgfiIcon = 0x00000100;
    public const uint ShgfiLargeIcon = 0x00000000;
    public const uint ShgsiIcon = 0x00000100;
    public const uint ShgsiLargeIcon = 0x00000000;

    public const uint SiidDesktopPc = 179;
    public const uint BiRgb = 0;
    public const uint DibRgbColors = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct IconInfo
    {
        public int IsIcon;
        public uint HotspotX;
        public uint HotspotY;
        public nint MaskBitmap;
        public nint ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPerPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        public fixed char DisplayName[260];
        public fixed char TypeName[80];
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StockIconInfo
    {
        public uint Size;
        public nint Icon;
        public uint SystemImageIndex;
        public int IconIndex;
        public fixed char Path[260];
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)]
    public static partial nint SendMessageTimeout(
        nint hWnd,
        uint msg,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeout,
        out nint result);

    [LibraryImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    public static partial nint GetClassLongPtr(nint hWnd, int index);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetIconInfo(nint hIcon, out IconInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    public static partial int GetObject(nint handle, int size, out Bitmap bitmap);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial int GetDIBits(
        nint hdc,
        nint hbm,
        uint start,
        uint lines,
        byte* bits,
        BitmapInfoHeader* header,
        uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint handle);

    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint SHGetFileInfo(string path, uint attributes, ref ShFileInfo info, uint infoSize, uint flags);

    [DllImport("shell32.dll")]
    public static extern int SHGetStockIconInfo(uint stockId, uint flags, out StockIconInfo info);
}
