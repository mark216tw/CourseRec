using System.ComponentModel;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using WinRT.Interop;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 應用程式主視窗（含側邊導覽列、最小尺寸限制與關閉前未完成工作確認）。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 1040;
    private const int MinimumWindowHeight = 680;

    private readonly MainViewModel _viewModel;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<MainWindow> _logger;
    private RecorderViewModel? _recorderViewModel;
    private bool _allowClose;
    private bool _isShowingCloseDialog;
    private bool _isMinimized;
    private bool _suspendedByMinimize;
    private RectInt32? _lastNormalBounds;

    public MainWindow(
        MainViewModel viewModel,
        INavigationCoordinator navigationCoordinator,
        ISettingsService settingsService,
        ILocalizationService localization,
        ILogger<MainWindow> logger)
    {
        _viewModel = viewModel;
        _navigationCoordinator = navigationCoordinator;
        _settingsService = settingsService;
        _localization = localization;
        _logger = logger;

        InitializeComponent();
        Title = _viewModel.WindowTitle;

        ConfigureWindowSizing();
        ApplyWindowIcon();
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
        _navigationCoordinator.NavigationRequested += OnNavigationRequested;
        Closed += OnMainWindowClosed;
    }

    public void AttachRecorderViewModel(RecorderViewModel recorderViewModel)
    {
        ArgumentNullException.ThrowIfNull(recorderViewModel);
        if (ReferenceEquals(_recorderViewModel, recorderViewModel))
        {
            return;
        }

        if (_recorderViewModel is not null)
        {
            _recorderViewModel.PropertyChanged -= OnRecorderViewModelPropertyChanged;
        }

        _recorderViewModel = recorderViewModel;
        _recorderViewModel.PropertyChanged += OnRecorderViewModelPropertyChanged;
        UpdateWindowTitle();
    }

    public nint WindowHandle => WindowNative.GetWindowHandle(this);

    /// <summary>
    /// 在視窗顯示後才導覽至起始頁面，避免頁面 XAML 建構延後首次可見時間。
    /// </summary>
    public void NavigateToInitialPage(NavigationPageKey pageKey)
    {
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => SelectNavItemForKey(pageKey)))
        {
            SelectNavItemForKey(pageKey);
        }
    }

    public Func<Task>? BeforeForceStopRecordingAsync { get; set; }

    /// <summary>
    /// 切換側邊導覽列顯示狀態（播放器全螢幕時隱藏，離開時還原）。
    /// </summary>
    public void SetNavigationPaneVisible(bool isVisible)
    {
        MainNavView.IsPaneVisible = isVisible;
    }

    public void BringToFront()
    {
        nint hwnd = WindowHandle;
        if (WindowNativeMethods.IsIconic(hwnd))
        {
            WindowNativeMethods.ShowWindow(hwnd, WindowNativeMethods.SwRestore);
        }

        Activate();
        WindowNativeMethods.SetForegroundWindow(hwnd);
    }

    public async Task<bool> ConfirmDiscardEditorChangesAsync()
    {
        if (Content.XamlRoot is null)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = _localization.GetString("DialogDiscardEditorChangesTitle"),
            Content = _localization.GetString("DialogDiscardEditorChangesMessage"),
            PrimaryButtonText = _localization.GetString("DialogConfirmDiscardAndOpen"),
            CloseButtonText = _localization.GetString("DialogCancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ConfigureWindowSizing()
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinimumWindowWidth;
            presenter.PreferredMinimumHeight = MinimumWindowHeight;
        }

        if (!TryRestoreWindowPlacement())
        {
            var defaultSize = new SizeInt32(1280, 820);
            AppWindow.Resize(defaultSize);
            PointInt32 position = AppWindow.Position;
            _lastNormalBounds = new RectInt32(position.X, position.Y, defaultSize.Width, defaultSize.Height);
        }
    }

    private bool TryRestoreWindowPlacement()
    {
        WindowPlacement? placement = _settingsService.Current.WindowPlacement;
        if (placement is null || placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        var desired = new RectInt32(
            placement.Left,
            placement.Top,
            Math.Max(MinimumWindowWidth, placement.Width),
            Math.Max(MinimumWindowHeight, placement.Height));
        RectInt32 bounds = ClampToVisibleArea(desired);
        _lastNormalBounds = bounds;
        AppWindow.MoveAndResize(bounds);

        if (placement.IsMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }

        return true;
    }

    private static RectInt32 ClampToVisibleArea(RectInt32 desired)
    {
        RectInt32 workArea = DisplayArea.GetFromRect(desired, DisplayAreaFallback.Primary).WorkArea;
        int minimumWidth = Math.Min(MinimumWindowWidth, workArea.Width);
        int minimumHeight = Math.Min(MinimumWindowHeight, workArea.Height);
        int width = Math.Clamp(desired.Width, minimumWidth, workArea.Width);
        int height = Math.Clamp(desired.Height, minimumHeight, workArea.Height);
        int left = Math.Clamp(desired.X, workArea.X, workArea.X + workArea.Width - width);
        int top = Math.Clamp(desired.Y, workArea.Y, workArea.Y + workArea.Height - height);
        return new RectInt32(left, top, width, height);
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter statePresenter)
        {
            bool isNowMinimized = statePresenter.State == OverlappedPresenterState.Minimized;
            if (isNowMinimized != _isMinimized)
            {
                _isMinimized = isNowMinimized;
                HandleMinimizedStateChanged(isNowMinimized);
            }
        }

        if (!args.DidPositionChange && !args.DidSizeChange)
        {
            return;
        }

        if (sender.Presenter is OverlappedPresenter presenter
            && presenter.State != OverlappedPresenterState.Restored)
        {
            return;
        }

        PointInt32 position = sender.Position;
        SizeInt32 size = sender.Size;
        _lastNormalBounds = new RectInt32(position.X, position.Y, size.Width, size.Height);
    }

    private void HandleMinimizedStateChanged(bool isMinimized)
    {
        if (isMinimized)
        {
            if (!_viewModel.IsRecordingActive)
            {
                bool miniOverlayVisible = false;
                try
                {
                    miniOverlayVisible = App.GetService<IMiniOverlayService>().IsVisible;
                }
                catch
                {
                    // 迷你控制列服務尚未就緒時視為未顯示。
                }

                if (!miniOverlayVisible && _recorderViewModel is not null)
                {
                    _recorderViewModel.SuspendIdleCapture();
                    _suspendedByMinimize = true;
                }

                MemoryTrimHelper.RequestTrim(trimWorkingSet: true);
            }
        }
        else if (_suspendedByMinimize)
        {
            _suspendedByMinimize = false;
            if (_viewModel.CurrentPage == NavigationPageKey.Recorder)
            {
                _recorderViewModel?.ResumeIdleCapture();
            }
        }
    }

    private async Task SaveWindowPlacementAsync()
    {
        try
        {
            RectInt32 bounds = _lastNormalBounds ?? new RectInt32(
                AppWindow.Position.X,
                AppWindow.Position.Y,
                AppWindow.Size.Width,
                AppWindow.Size.Height);
            bool isMaximized = AppWindow.Presenter is OverlappedPresenter presenter
                && presenter.State == OverlappedPresenterState.Maximized;

            await _settingsService.SaveWindowPlacementAsync(new WindowPlacement(
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                isMaximized));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "儲存視窗位置與大小失敗");
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

    private void OnNavigationRequested(object? sender, NavigationPageKey pageKey)
    {
        if (!DispatcherQueue.TryEnqueue(() => SelectNavItemForKey(pageKey)))
        {
            SelectNavItemForKey(pageKey);
        }
    }

    private void OnRecorderViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RecorderViewModel.State)
            or nameof(RecorderViewModel.StatusBadgeText)
            or nameof(RecorderViewModel.ElapsedTimeText)
            or nameof(RecorderViewModel.IsFinalizing))
        {
            UpdateWindowTitle();
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _navigationCoordinator.NavigationRequested -= OnNavigationRequested;
        if (_recorderViewModel is not null)
        {
            _recorderViewModel.PropertyChanged -= OnRecorderViewModelPropertyChanged;
        }
    }

    private void UpdateWindowTitle()
    {
        if (_recorderViewModel is null || _recorderViewModel.State == RecordingState.Idle)
        {
            Title = _viewModel.WindowTitle;
            return;
        }

        Title = _localization.Format(
            "WindowTitleRecordingFormat",
            _recorderViewModel.StatusBadgeText,
            _recorderViewModel.ElapsedTimeText);
    }

    private void SelectNavItemForKey(NavigationPageKey key)
    {
        NavigationViewItem target = key switch
        {
            NavigationPageKey.Library => NavItemLibrary,
            NavigationPageKey.Editor => NavItemEditor,
            NavigationPageKey.Settings => NavItemSettings,
            NavigationPageKey.About => NavItemAbout,
            _ => NavItemRecorder,
        };

        if (!ReferenceEquals(MainNavView.SelectedItem, target))
        {
            MainNavView.SelectedItem = target;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is not string tag)
        {
            return;
        }

        NavigationPageKey pageKey = tag switch
        {
            "Library" => NavigationPageKey.Library,
            "Editor" => NavigationPageKey.Editor,
            "Settings" => NavigationPageKey.Settings,
            "About" => NavigationPageKey.About,
            _ => NavigationPageKey.Recorder,
        };

        _viewModel.CurrentPage = pageKey;

        Type targetPageType = pageKey switch
        {
            NavigationPageKey.Library => typeof(LibraryPage),
            NavigationPageKey.Editor => typeof(EditorPage),
            NavigationPageKey.Settings => typeof(SettingsPage),
            NavigationPageKey.About => typeof(AboutPage),
            _ => typeof(RecorderPage),
        };

        if (ContentFrame.CurrentSourcePageType != targetPageType)
        {
            ContentFrame.Navigate(targetPageType);
            if (ContentFrame.BackStackDepth > 0)
            {
                ContentFrame.BackStack.Clear();
            }
        }

        _ = SaveLastPageAsync(pageKey);
    }

    private async Task SaveLastPageAsync(NavigationPageKey page)
    {
        if (_settingsService.Current.LastPage == page)
        {
            return;
        }

        try
        {
            await _settingsService.SaveLastPageAsync(page);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "儲存上次瀏覽頁面失敗");
        }
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        await SaveWindowPlacementAsync();

        if (!_viewModel.IsRecordingActive && !_viewModel.HasUnsavedEditorChanges)
        {
            _allowClose = true;
            Close();
            return;
        }

        if (_isShowingCloseDialog || Content.XamlRoot is null)
        {
            return;
        }

        _isShowingCloseDialog = true;
        try
        {
            bool isRecording = _viewModel.IsRecordingActive;
            var dialog = new ContentDialog
            {
                XamlRoot = Content.XamlRoot,
                Title = _localization.GetString("DialogCloseWhileActiveTitle"),
                Content = _localization.GetString(
                    isRecording ? "DialogCloseWhileRecordingMessage" : "DialogCloseWhileUnsavedEditorMessage"),
                PrimaryButtonText = _localization.GetString(
                    isRecording ? "DialogConfirmStopAndExit" : "DialogConfirmDiscardAndExit"),
                CloseButtonText = _localization.GetString("DialogCancel"),
                DefaultButton = ContentDialogButton.Close,
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                if (isRecording && BeforeForceStopRecordingAsync is not null)
                {
                    await BeforeForceStopRecordingAsync();
                }

                _allowClose = true;
                await SaveWindowPlacementAsync();
                Close();
            }
        }
        finally
        {
            _isShowingCloseDialog = false;
        }
    }
}
