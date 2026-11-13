using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 從視窗控制代碼或程序執行檔擷取圖示，並轉換為可顯示的 <see cref="IconData"/>。
/// 所有方法皆為盡力而為，取不到時回傳 <c>null</c>，不影響來源列舉流程。
/// </summary>
public static unsafe class WindowIconHelper
{
    private const int MaxIconDimension = 128;
    private const int StockIconAttempts = 2;

    private static readonly object MonitorIconSync = new();
    private static readonly ConcurrentDictionary<string, IconData> _executableIconCache = new(StringComparer.OrdinalIgnoreCase);

    private static IconData? _monitorIcon;

    /// <summary>
    /// 取得指定視窗的圖示；依序嘗試視窗圖示、視窗類別圖示、執行檔圖示。
    /// </summary>
    public static IconData? GetWindowIcon(nint hWnd, uint processId)
    {
        return GetWindowIcon(hWnd, processId, executablePath: null);
    }

    public static IconData? GetWindowIcon(nint hWnd, uint processId, string? executablePath)
    {
        if (hWnd == 0)
        {
            return null;
        }

        try
        {
            nint hIcon = QueryWindowIcon(hWnd, CaptureIconNativeMethods.IconBig);
            if (hIcon == 0)
            {
                hIcon = QueryWindowIcon(hWnd, CaptureIconNativeMethods.IconSmall2);
            }

            if (hIcon == 0)
            {
                hIcon = CaptureIconNativeMethods.GetClassLongPtr(hWnd, CaptureIconNativeMethods.GclHicon);
            }

            if (hIcon == 0)
            {
                hIcon = CaptureIconNativeMethods.GetClassLongPtr(hWnd, CaptureIconNativeMethods.GclHiconSm);
            }

            if (hIcon != 0)
            {
                return TryConvert(hIcon);
            }

            return string.IsNullOrEmpty(executablePath)
                ? TryGetExecutableIcon(processId)
                : TryGetExecutableIcon(executablePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 取得系統「桌上型電腦」圖示，作為顯示器來源的圖示（快取共用）。
    /// </summary>
    public static IconData? GetMonitorIcon()
    {
        lock (MonitorIconSync)
        {
            if (_monitorIcon is not null)
            {
                return _monitorIcon;
            }

            try
            {
                var info = new CaptureIconNativeMethods.StockIconInfo
                {
                    Size = (uint)Marshal.SizeOf<CaptureIconNativeMethods.StockIconInfo>(),
                };

                for (int attempt = 0; attempt < StockIconAttempts; attempt++)
                {
                    int hr = CaptureIconNativeMethods.SHGetStockIconInfo(
                        CaptureIconNativeMethods.SiidDesktopPc,
                        CaptureIconNativeMethods.ShgsiIcon | CaptureIconNativeMethods.ShgsiLargeIcon,
                        out info);

                    if (hr < 0 || info.Icon == 0)
                    {
                        continue;
                    }

                    try
                    {
                        _monitorIcon = TryConvert(info.Icon);
                    }
                    finally
                    {
                        CaptureIconNativeMethods.DestroyIcon(info.Icon);
                    }

                    return _monitorIcon;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    private static nint QueryWindowIcon(nint hWnd, nint iconType)
    {
        nint sent = CaptureIconNativeMethods.SendMessageTimeout(
            hWnd,
            CaptureIconNativeMethods.WmGetIcon,
            iconType,
            0,
            CaptureIconNativeMethods.SmtoBlock | CaptureIconNativeMethods.SmtoAbortIfHung,
            100,
            out nint result);

        return sent == 0 ? 0 : result;
    }

    private static IconData? TryGetExecutableIcon(uint processId)
    {
        return processId == 0 ? null : TryGetExecutableIcon(TryGetExecutablePath(processId));
    }

    private static IconData? TryGetExecutableIcon(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath))
        {
            return null;
        }

        if (_executableIconCache.TryGetValue(executablePath, out IconData? cached))
        {
            return cached;
        }

        IconData? icon = LoadExecutableIcon(executablePath);
        if (icon is not null)
        {
            _executableIconCache.TryAdd(executablePath, icon);
        }

        return icon;
    }

    private static IconData? LoadExecutableIcon(string executablePath)
    {
        try
        {
            var info = default(CaptureIconNativeMethods.ShFileInfo);
            nint result = CaptureIconNativeMethods.SHGetFileInfo(
                executablePath,
                0,
                ref info,
                (uint)Marshal.SizeOf<CaptureIconNativeMethods.ShFileInfo>(),
                CaptureIconNativeMethods.ShgfiIcon | CaptureIconNativeMethods.ShgfiLargeIcon);

            if (result == 0 || info.Icon == 0)
            {
                return null;
            }

            try
            {
                return TryConvert(info.Icon);
            }
            finally
            {
                CaptureIconNativeMethods.DestroyIcon(info.Icon);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetExecutablePath(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static IconData? TryConvert(nint hIcon)
    {
        if (!CaptureIconNativeMethods.GetIconInfo(hIcon, out CaptureIconNativeMethods.IconInfo iconInfo))
        {
            return null;
        }

        try
        {
            if (iconInfo.ColorBitmap == 0)
            {
                return null;
            }

            if (CaptureIconNativeMethods.GetObject(
                    iconInfo.ColorBitmap,
                    Marshal.SizeOf<CaptureIconNativeMethods.Bitmap>(),
                    out CaptureIconNativeMethods.Bitmap bitmap) == 0)
            {
                return null;
            }

            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width <= 0
                || height <= 0
                || width > MaxIconDimension
                || height > MaxIconDimension)
            {
                return null;
            }

            byte[]? pixels = TryReadColorPixels(iconInfo.ColorBitmap, width, height);
            if (pixels is null)
            {
                return null;
            }

            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    hasAlpha = true;
                    break;
                }
            }

            if (!hasAlpha && !TryApplyMask(iconInfo.MaskBitmap, width, height, pixels))
            {
                return null;
            }

            PremultiplyAlpha(pixels);
            return new IconData(width, height, pixels);
        }
        finally
        {
            if (iconInfo.ColorBitmap != 0)
            {
                CaptureIconNativeMethods.DeleteObject(iconInfo.ColorBitmap);
            }

            if (iconInfo.MaskBitmap != 0)
            {
                CaptureIconNativeMethods.DeleteObject(iconInfo.MaskBitmap);
            }
        }
    }

    private static byte[]? TryReadColorPixels(nint hBitmap, int width, int height)
    {
        var header = new CaptureIconNativeMethods.BitmapInfoHeader
        {
            Size = (uint)sizeof(CaptureIconNativeMethods.BitmapInfoHeader),
            Width = width,
            Height = height,
            Planes = 1,
            BitCount = 32,
            Compression = CaptureIconNativeMethods.BiRgb,
            SizeImage = (uint)(width * height * 4),
        };

        int stride = width * 4;
        byte[] bottomUp = new byte[stride * height];
        nint hdc = CaptureIconNativeMethods.GetDC(0);
        if (hdc == 0)
        {
            return null;
        }

        try
        {
            fixed (byte* pBits = bottomUp)
            {
                if (CaptureIconNativeMethods.GetDIBits(
                        hdc,
                        hBitmap,
                        0,
                        (uint)height,
                        pBits,
                        &header,
                        CaptureIconNativeMethods.DibRgbColors) == 0)
                {
                    return null;
                }
            }
        }
        finally
        {
            CaptureIconNativeMethods.ReleaseDC(0, hdc);
        }

        return FlipVertically(bottomUp, stride, height);
    }

    private static bool TryApplyMask(nint hMask, int width, int height, byte[] pixels)
    {
        if (hMask == 0)
        {
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = byte.MaxValue;
            }

            return true;
        }

        var header = new CaptureIconNativeMethods.BitmapInfoHeader
        {
            Size = (uint)sizeof(CaptureIconNativeMethods.BitmapInfoHeader),
            Width = width,
            Height = height,
            Planes = 1,
            BitCount = 1,
            Compression = CaptureIconNativeMethods.BiRgb,
            SizeImage = 0,
        };

        int maskStride = ((width + 31) / 32) * 4;
        byte[] mask = new byte[maskStride * height];
        nint hdc = CaptureIconNativeMethods.GetDC(0);
        if (hdc == 0)
        {
            return false;
        }

        try
        {
            fixed (byte* pMask = mask)
            {
                if (CaptureIconNativeMethods.GetDIBits(
                        hdc,
                        hMask,
                        0,
                        (uint)height,
                        pMask,
                        &header,
                        CaptureIconNativeMethods.DibRgbColors) == 0)
                {
                    return false;
                }
            }
        }
        finally
        {
            CaptureIconNativeMethods.ReleaseDC(0, hdc);
        }

        for (int y = 0; y < height; y++)
        {
            int maskRow = (height - 1 - y) * maskStride;
            int pixelRow = y * width * 4;
            for (int x = 0; x < width; x++)
            {
                byte maskByte = mask[maskRow + (x / 8)];
                bool transparent = ((maskByte >> (7 - (x % 8))) & 1) != 0;
                pixels[pixelRow + (x * 4) + 3] = transparent ? (byte)0 : byte.MaxValue;
            }
        }

        return true;
    }

    private static byte[] FlipVertically(byte[] bottomUp, int stride, int height)
    {
        byte[] topDown = new byte[bottomUp.Length];
        for (int y = 0; y < height; y++)
        {
            Buffer.BlockCopy(bottomUp, (height - 1 - y) * stride, topDown, y * stride, stride);
        }

        return topDown;
    }

    private static void PremultiplyAlpha(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4)
        {
            int alpha = pixels[i + 3];
            if (alpha == byte.MaxValue)
            {
                continue;
            }

            if (alpha == 0)
            {
                pixels[i] = 0;
                pixels[i + 1] = 0;
                pixels[i + 2] = 0;
                continue;
            }

            pixels[i] = (byte)(pixels[i] * alpha / 255);
            pixels[i + 1] = (byte)(pixels[i + 1] * alpha / 255);
            pixels[i + 2] = (byte)(pixels[i + 2] * alpha / 255);
        }
    }
}

