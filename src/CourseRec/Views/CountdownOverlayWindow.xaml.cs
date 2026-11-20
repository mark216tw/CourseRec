using System.Globalization;
using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Windows.Graphics;
using WinRT.Interop;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 錄影開始倒數計時的大型螢幕置中覆蓋視窗（置頂、不搶焦點、穿透點擊且排除自身入鏡）。
/// </summary>
public sealed partial class CountdownOverlayWindow : Window
{
    private const double OverlayWidthDip = 264d;
    private const double OverlayHeightDip = 264d;
    private const double DefaultDpi = 96d;
    private const byte OverlayAlpha = 238;

    private readonly ILocalizationService _localization;
    private readonly ILogger<CountdownOverlayWindow> _logger;

    public CountdownOverlayWindow(
        ILocalizationService localization,
        ILogger<CountdownOverlayWindow> logger)
    {
        _localization = localization;
        _logger = logger;
        InitializeComponent();

        Title = _localization.GetString("CountdownOverlayTitle");
        ConfigureOverlayWindow();
    }

    public nint WindowHandle => WindowNative.GetWindowHandle(this);

    public void UpdateCountdown(int remainingSeconds, CaptureTarget? target)
    {
        int clamped = Math.Max(0, remainingSeconds);
        CountdownNumberText.Text = clamped.ToString(CultureInfo.InvariantCulture);
        AutomationProperties.SetName(
            CountdownNumberText,
            _localization.Format("CountdownOverlayNumberAutomationFormat", clamped));

        PositionOnTarget(target);
        PlayTickAnimation();
    }

    public void ShowWithoutActivating()
    {
        AppWindow.Show(activateWindow: false);
    }

    private void ConfigureOverlayWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }

        AppWindow.IsShownInSwitchers = false;

        nint hwnd = WindowHandle;
        WindowNativeMethods.SetWindowDisplayAffinity(hwnd, WindowNativeMethods.WdaExcludeFromCapture);
        WindowNativeMethods.TryConfigureNonActivatingClickThrough(hwnd, OverlayAlpha);
    }

    private void PositionOnTarget(CaptureTarget? target)
    {
        try
        {
            (RectInt32 targetRect, RectInt32 workArea) = ResolveTargetAndWorkArea(target);

            // 先移到目標區域中心，確保跨螢幕不同 DPI 時取得目標螢幕的正確縮放比例。
            int probeX = targetRect.X + (targetRect.Width / 2);
            int probeY = targetRect.Y + (targetRect.Height / 2);
            AppWindow.Move(new PointInt32(probeX, probeY));

            double scale = GetRasterizationScale();
            int clientWidth = Math.Max(1, (int)Math.Ceiling(OverlayWidthDip * scale));
            int clientHeight = Math.Max(1, (int)Math.Ceiling(OverlayHeightDip * scale));
            AppWindow.ResizeClient(new SizeInt32(clientWidth, clientHeight));

            SizeInt32 outerSize = AppWindow.Size;
            (int left, int top) = CountdownOverlayBoundsHelper.CalculateCenteredPosition(
                targetRect.X,
                targetRect.Y,
                targetRect.Width,
                targetRect.Height,
                outerSize.Width,
                outerSize.Height,
                workArea.X,
                workArea.Y,
                workArea.Width,
                workArea.Height);

            AppWindow.Move(new PointInt32(left, top));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "調整倒數計時覆蓋視窗位置失敗");
        }
    }

    private static (RectInt32 TargetRect, RectInt32 WorkArea) ResolveTargetAndWorkArea(CaptureTarget? target)
    {
        if (target is { Kind: CaptureTargetKind.Monitor, Handle: not 0 }
            && TryGetMonitorWorkArea(target.Handle, out RectInt32 monitorWorkArea))
        {
            return (monitorWorkArea, monitorWorkArea);
        }

        if (target is { Kind: CaptureTargetKind.Window, Handle: not 0 }
            && CaptureNativeMethods.IsWindow(target.Handle)
            && !WindowNativeMethods.IsIconic(target.Handle)
            && CaptureNativeMethods.GetWindowRect(target.Handle, out CaptureNativeMethods.Rect windowRect)
            && windowRect.Width > 0
            && windowRect.Height > 0)
        {
            var rect = new RectInt32(windowRect.Left, windowRect.Top, windowRect.Width, windowRect.Height);
            RectInt32 nearestWorkArea = DisplayArea.GetFromRect(rect, DisplayAreaFallback.Primary).WorkArea;
            return (rect, nearestWorkArea);
        }

        RectInt32 primaryWorkArea = DisplayArea.Primary.WorkArea;
        return (primaryWorkArea, primaryWorkArea);
    }

    private static bool TryGetMonitorWorkArea(nint hMonitor, out RectInt32 workArea)
    {
        var info = new CaptureNativeMethods.MonitorInfoExW
        {
            Size = Marshal.SizeOf<CaptureNativeMethods.MonitorInfoExW>(),
        };

        if (CaptureNativeMethods.GetMonitorInfoW(hMonitor, ref info)
            && info.WorkArea.Width > 0
            && info.WorkArea.Height > 0)
        {
            workArea = new RectInt32(
                info.WorkArea.Left,
                info.WorkArea.Top,
                info.WorkArea.Width,
                info.WorkArea.Height);
            return true;
        }

        workArea = default;
        return false;
    }

    private double GetRasterizationScale()
    {
        uint dpi = WindowNativeMethods.GetDpiForWindow(WindowHandle);
        if (dpi > 0)
        {
            return dpi / DefaultDpi;
        }

        double xamlScale = RootBorder.XamlRoot?.RasterizationScale ?? 0d;
        return xamlScale > 0 ? xamlScale : 1d;
    }

    private void PlayTickAnimation()
    {
        try
        {
            CountdownTickStoryboard.Stop();
            CountdownTickStoryboard.Begin();
        }
        catch
        {
            // 視覺樹尚未就緒時略過縮放動畫，數字仍會正常顯示。
        }
    }
}
