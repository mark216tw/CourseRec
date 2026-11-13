using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Services.Capture;

/// <summary>
/// 透過 Win32 與 DWM API 枚舉可擷取的多螢幕與背景視窗實作。
/// </summary>
public sealed unsafe class CaptureEnumerationService : ICaptureEnumerationService
{
    private readonly ILogger<CaptureEnumerationService>? _logger;
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;
    private readonly ConcurrentDictionary<uint, (string Name, string? ExecutablePath)> _processCache = new();

    public CaptureEnumerationService(ILogger<CaptureEnumerationService>? logger = null)
    {
        _logger = logger;
    }

    public IReadOnlyList<CaptureTarget> GetMonitors()
    {
        var results = new List<CaptureTarget>();
        int index = 1;

        CaptureNativeMethods.EnumDisplayMonitors(
            0,
            0,
            (nint hMonitor, nint _, ref CaptureNativeMethods.Rect rect, nint _) =>
            {
                var info = new CaptureNativeMethods.MonitorInfoExW
                {
                    Size = Marshal.SizeOf<CaptureNativeMethods.MonitorInfoExW>(),
                };

                string deviceName;
                bool isPrimary = false;
                if (CaptureNativeMethods.GetMonitorInfoW(hMonitor, ref info))
                {
                    deviceName = new string(info.DeviceName).TrimEnd('\0');
                    isPrimary = (info.Flags & 1u) != 0;
                    rect = info.Monitor;
                }
                else
                {
                    deviceName = $"DISPLAY{index}";
                }

                int width = Math.Max(320, rect.Width);
                int height = Math.Max(240, rect.Height);
                string primaryBadge = isPrimary ? " ★" : string.Empty;
                string title = $"#{index} {deviceName}{primaryBadge} ({width}×{height})";

                results.Add(new CaptureTarget(
                    Id: $"monitor:{hMonitor}",
                    Kind: CaptureTargetKind.Monitor,
                    Title: title,
                    Subtitle: $"{width} × {height}",
                    Handle: hMonitor,
                    ProcessId: 0,
                    Width: width,
                    Height: height)
                {
                    Icon = TryGetIcon(() => WindowIconHelper.GetMonitorIcon(), "monitor"),
                });

                index++;
                return true;
            },
            0);

        return results;
    }

    public IReadOnlyList<CaptureTarget> GetCapturableWindows()
    {
        _processCache.Clear();
        var results = new List<CaptureTarget>();
        nint shellWindow = CaptureNativeMethods.GetShellWindow();

        CaptureNativeMethods.EnumWindows(
            (nint hWnd, nint _) =>
            {
                if (hWnd == 0 || hWnd == shellWindow || !CaptureNativeMethods.IsWindowVisible(hWnd))
                {
                    return true;
                }

                long exStyle = CaptureNativeMethods.GetWindowLongPtr(hWnd, CaptureNativeMethods.GwlExStyle);
                if ((exStyle & CaptureNativeMethods.WsExToolWindow) != 0)
                {
                    return true;
                }

                if (CaptureNativeMethods.DwmGetWindowAttribute(
                        hWnd,
                        CaptureNativeMethods.DwmaCloaked,
                        out int cloaked,
                        sizeof(int)) == 0 && cloaked != 0)
                {
                    return true;
                }

                int titleLen = CaptureNativeMethods.GetWindowTextLength(hWnd);
                if (titleLen <= 0)
                {
                    return true;
                }

                CaptureNativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0 || pid == _currentProcessId)
                {
                    return true;
                }

                char[] titleChars = new char[titleLen + 1];
                int copied = CaptureNativeMethods.GetWindowText(hWnd, titleChars, titleChars.Length);
                if (copied <= 0)
                {
                    return true;
                }

                string windowTitle = new string(titleChars, 0, copied).Trim();
                if (string.IsNullOrWhiteSpace(windowTitle))
                {
                    return true;
                }

                int width = 1280;
                int height = 720;
                if (CaptureNativeMethods.GetWindowRect(hWnd, out CaptureNativeMethods.Rect rect)
                    && rect.Width >= 120
                    && rect.Height >= 90)
                {
                    width = rect.Width;
                    height = rect.Height;
                }

                TryGetProcessInfo(pid, out string procName, out string? executablePath);
                string displayTitle = string.IsNullOrEmpty(procName)
                    ? windowTitle
                    : $"[{procName}] {windowTitle}";

                results.Add(new CaptureTarget(
                    Id: $"window:{hWnd}",
                    Kind: CaptureTargetKind.Window,
                    Title: displayTitle,
                    Subtitle: $"PID {pid} · {width}×{height}",
                    Handle: hWnd,
                    ProcessId: pid,
                    Width: width,
                    Height: height)
                {
                    Icon = TryGetIcon(() => WindowIconHelper.GetWindowIcon(hWnd, pid, executablePath), displayTitle),
                });

                return true;
            },
            0);

        return results;
    }

    private IconData? TryGetIcon(Func<IconData?> factory, string context)
    {
        try
        {
            return factory();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "取得來源圖示失敗：{Context}", context);
            return null;
        }
    }

    private void TryGetProcessInfo(uint pid, out string name, out string? executablePath)
    {
        if (_processCache.TryGetValue(pid, out (string Name, string? ExecutablePath) cached))
        {
            name = cached.Name;
            executablePath = cached.ExecutablePath;
            return;
        }

        try
        {
            using var proc = Process.GetProcessById((int)pid);
            name = proc.ProcessName;
            try
            {
                executablePath = proc.MainModule?.FileName;
            }
            catch
            {
                executablePath = null;
            }
        }
        catch
        {
            name = string.Empty;
            executablePath = null;
        }

        _processCache[pid] = (name, executablePath);
    }
}
