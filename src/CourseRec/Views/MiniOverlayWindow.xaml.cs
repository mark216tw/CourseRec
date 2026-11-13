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
/// 迷你懸浮錄影控制列（支援透過 <c>WDA_EXCLUDEFROMCAPTURE</c> 排除自身入鏡）。
/// </summary>
public sealed partial class MiniOverlayWindow : Window
{
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
        ResizeWindowToContent();
        RestorePositionIfNeeded();
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
        if (Math.Abs(current.Width - width) <= 2 && Math.Abs(current.Height - height) <= 2)
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

        SizeInt32 size = AppWindow.Size;
        var desired = new RectInt32(saved.Left, saved.Top, size.Width, size.Height);
        RectInt32 workArea = DisplayArea.GetFromRect(desired, DisplayAreaFallback.Nearest).WorkArea;
        int maxLeft = Math.Max(workArea.X, workArea.X + workArea.Width - size.Width);
        int maxTop = Math.Max(workArea.Y, workArea.Y + workArea.Height - size.Height);
        int left = Math.Clamp(desired.X, workArea.X, maxLeft);
        int top = Math.Clamp(desired.Y, workArea.Y, maxTop);

        AppWindow.Move(new PointInt32(left, top));
        _lastSavedPosition = new MiniOverlayPosition(left, top);
    }

    private async Task PersistPositionAsync()
    {
        try
        {
            PointInt32 position = AppWindow.Position;
            var current = new MiniOverlayPosition(position.X, position.Y);
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
            _logger.LogError(ex, "從懸浮列停止錄影失敗");
        }
    }

    private void OnExpandMainWindowClicked(object sender, RoutedEventArgs e)
    {
        ShowMainWindow();
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
