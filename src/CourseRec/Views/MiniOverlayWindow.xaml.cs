using System.ComponentModel;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 迷你錄影控制列（支援透過 <c>WDA_EXCLUDEFROMCAPTURE</c> 排除自身入鏡）。
/// </summary>
public sealed partial class MiniOverlayWindow : Window
{
    private const double FallbackWidthDip = 240;
    private const double FallbackHeightDip = 40;
    private const double DefaultDpi = 96d;
    private const int SizeTolerancePixels = 2;

    private readonly ILogger<MiniOverlayWindow> _logger;
    private readonly ISettingsService _settingsService;

    private bool _isDragging;
    private bool _isResizing;
    private bool _hasRestoredPosition;
    private WindowNativeMethods.Point _dragStartCursor;
    private PointInt32 _dragStartWindowPosition;
    private MiniOverlayPosition? _lastSavedPosition;

    public MiniOverlayWindow(
        RecorderViewModel viewModel,
        ILocalizationService localization,
        ISettingsService settingsService,
        bool excludeFromCapture,
        ILogger<MiniOverlayWindow> logger)
    {
        ViewModel = viewModel;
        _settingsService = settingsService;
        _logger = logger;
        InitializeComponent();
        Title = localization.GetString("MiniOverlayTitle");

        ConfigureCompactTopMostWindow();
        SetExcludeFromCapture(excludeFromCapture);
        ApplyWindowIcon();

        // 第一幀尚未完成尺寸與位置校正前先隱藏內容，避免預設大視窗短暫出現後才縮小。
        RootBorder.Opacity = 0;
        PrepareInitialBounds();

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnWindowClosed;
    }

    public RecorderViewModel ViewModel { get; }

    public nint WindowHandle => WindowNative.GetWindowHandle(this);

    public void SetExcludeFromCapture(bool exclude)
    {
        uint affinity = exclude
            ? WindowNativeMethods.WdaExcludeFromCapture
            : WindowNativeMethods.WdaNone;
        WindowNativeMethods.SetWindowDisplayAffinity(WindowHandle, affinity);
    }

    private void ConfigureCompactTopMostWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        }
    }

    private void ApplyWindowIcon()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }
    }

    private void OnRootBorderLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ResizeWindowToContent();
            RestorePositionIfNeeded();
        }
        finally
        {
            RootBorder.Opacity = 1;
        }
    }

    private void OnRootBorderSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResizeWindowToContent();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecorderViewModel.StatusBadgeText)
            or nameof(RecorderViewModel.IsFinalizing))
        {
            DispatcherQueue.TryEnqueue(ResizeWindowToContent);
        }
    }

    private void ResizeWindowToContent()
    {
        if (_isResizing || _isDragging || RootBorder.XamlRoot is null)
        {
            return;
        }

        RootBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size desired = RootBorder.DesiredSize;
        if (desired.Width <= 0 || desired.Height <= 0)
        {
            return;
        }

        double scale = RootBorder.XamlRoot.RasterizationScale;
        if (scale <= 0)
        {
            scale = 1d;
        }

        int width = Math.Max(1, (int)Math.Ceiling(desired.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(desired.Height * scale));

        SizeInt32 current = AppWindow.ClientSize;
        if (Math.Abs(current.Width - width) <= SizeTolerancePixels
            && Math.Abs(current.Height - height) <= SizeTolerancePixels)
        {
            return;
        }

        _isResizing = true;
        try
        {
            AppWindow.ResizeClient(new SizeInt32(width, height));
        }
        finally
        {
            _isResizing = false;
        }
    }

    private void PrepareInitialBounds()
    {
        MiniOverlayPosition? saved = _settingsService.Current.MiniOverlayPosition;
        try
        {
            if (saved is not null)
            {
                AppWindow.Move(new PointInt32(saved.Left, saved.Top));
            }

            double scale = GetRasterizationScale();
            Size naturalDip = MeasureNaturalDip();
            bool hasSavedSize = saved is { WidthDip: > 0, HeightDip: > 0 };
            double widthDip = hasSavedSize ? saved!.WidthDip : naturalDip.Width;
            double heightDip = hasSavedSize ? saved!.HeightDip : naturalDip.Height;

            int width = Math.Max(1, (int)Math.Ceiling(widthDip * scale));
            int height = Math.Max(1, (int)Math.Ceiling(heightDip * scale));
            AppWindow.ResizeClient(new SizeInt32(width, height));

            if (saved is not null)
            {
                (int left, int top) = ClampToWorkArea(saved.Left, saved.Top, AppWindow.Size);
                AppWindow.Move(new PointInt32(left, top));
                _hasRestoredPosition = true;
                _lastSavedPosition = hasSavedSize
                    ? new MiniOverlayPosition(
                        left,
                        top,
                        Math.Round(width / scale, 2),
                        Math.Round(height / scale, 2))
                    : null;
            }
            else
            {
                _hasRestoredPosition = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "預先設定迷你控制列大小與位置失敗");
        }
    }

    private Size MeasureNaturalDip()
    {
        try
        {
            RootBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size desired = RootBorder.DesiredSize;
            if (desired.Width > 0 && desired.Height > 0)
            {
                return desired;
            }
        }
        catch
        {
            // 尚未載入視覺樹時量測可能失敗，改用預設尺寸。
        }

        return new Size(FallbackWidthDip, FallbackHeightDip);
    }

    private double GetRasterizationScale()
    {
        double scale = RootBorder.XamlRoot?.RasterizationScale ?? 0d;
        if (scale <= 0)
        {
            uint dpi = WindowNativeMethods.GetDpiForWindow(WindowHandle);
            scale = dpi > 0 ? dpi / DefaultDpi : 1d;
        }

        return scale;
    }

    private static (int Left, int Top) ClampToWorkArea(int left, int top, SizeInt32 size)
    {
        var desired = new RectInt32(left, top, size.Width, size.Height);
        RectInt32 workArea = DisplayArea.GetFromRect(desired, DisplayAreaFallback.Nearest).WorkArea;
        int maxLeft = Math.Max(workArea.X, workArea.X + workArea.Width - size.Width);
        int maxTop = Math.Max(workArea.Y, workArea.Y + workArea.Height - size.Height);
        return (Math.Clamp(left, workArea.X, maxLeft), Math.Clamp(top, workArea.Y, maxTop));
    }

    private void RestorePositionIfNeeded()
    {
        if (_hasRestoredPosition)
        {
            return;
        }

        _hasRestoredPosition = true;
        MiniOverlayPosition? saved = _settingsService.Current.MiniOverlayPosition;
        if (saved is null)
        {
            return;
        }

        (int left, int top) = ClampToWorkArea(saved.Left, saved.Top, AppWindow.Size);
        AppWindow.Move(new PointInt32(left, top));
        _lastSavedPosition = new MiniOverlayPosition(left, top, saved.WidthDip, saved.HeightDip);
    }

    private async Task PersistPositionAsync()
    {
        try
        {
            PointInt32 position = AppWindow.Position;
            SizeInt32 clientSize = AppWindow.ClientSize;
            double scale = GetRasterizationScale();
            var current = new MiniOverlayPosition(
                position.X,
                position.Y,
                Math.Round(clientSize.Width / scale, 2),
                Math.Round(clientSize.Height / scale, 2));
            if (current == _lastSavedPosition)
            {
                return;
            }

            await _settingsService.SaveMiniOverlayPositionAsync(current);
            _lastSavedPosition = current;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "儲存迷你控制列位置失敗");
        }
    }

    private void OnDragAreaPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(RootBorder).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (HasButtonAncestor(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (!WindowNativeMethods.GetCursorPos(out WindowNativeMethods.Point cursor))
        {
            return;
        }

        _isDragging = true;
        _dragStartCursor = cursor;
        _dragStartWindowPosition = AppWindow.Position;
        RootBorder.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnDragAreaPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        if (!WindowNativeMethods.GetCursorPos(out WindowNativeMethods.Point cursor))
        {
            return;
        }

        int x = _dragStartWindowPosition.X + (cursor.X - _dragStartCursor.X);
        int y = _dragStartWindowPosition.Y + (cursor.Y - _dragStartCursor.Y);
        PointInt32 current = AppWindow.Position;
        if (x == current.X && y == current.Y)
        {
            return;
        }

        AppWindow.Move(new PointInt32(x, y));
        e.Handled = true;
    }

    private void OnDragAreaPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        RootBorder.ReleasePointerCapture(e.Pointer);
        _ = PersistPositionAsync();
        e.Handled = true;
    }

    private void OnDragAreaPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isDragging = false;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _ = PersistPositionAsync();
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnWindowClosed;
    }

    private static bool HasButtonAncestor(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is Button)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private async void OnStopRecordingClicked(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
        try
        {
            await ViewModel.StopRecordingCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "從迷你控制列停止錄影失敗");
        }
    }

    private void OnCloseOverlayClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static void ShowMainWindow()
    {
        App.GetService<INavigationCoordinator>().NavigateTo(NavigationPageKey.Recorder);
        if (Application.Current is App app)
        {
            app.MainWindowInstance?.BringToFront();
        }
    }
}
