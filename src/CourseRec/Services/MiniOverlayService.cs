using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Com.VibeCode.CourseRec.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 集中管理迷你控制列視窗，讓控制列隨應用程式啟動與關閉。
/// </summary>
public sealed class MiniOverlayService : IMiniOverlayService
{
    private readonly RecorderViewModel _viewModel;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly ILogger<MiniOverlayWindow> _windowLogger;
    private readonly ILogger<MiniOverlayService> _logger;

    private MiniOverlayWindow? _window;

    public MiniOverlayService(
        RecorderViewModel viewModel,
        ILocalizationService localization,
        ISettingsService settingsService,
        IThemeService themeService,
        ILogger<MiniOverlayWindow> windowLogger,
        ILogger<MiniOverlayService> logger)
    {
        _viewModel = viewModel;
        _localization = localization;
        _settingsService = settingsService;
        _themeService = themeService;
        _windowLogger = windowLogger;
        _logger = logger;

        _settingsService.SettingsChanged += OnSettingsChanged;
    }

    public bool IsVisible => _window is not null;

    public void Show()
    {
        if (_window is not null)
        {
            return;
        }

        try
        {
            var window = new MiniOverlayWindow(
                _viewModel,
                _localization,
                _settingsService,
                _settingsService.Current.ExcludeOverlayFromCapture,
                _windowLogger);
            window.Closed += OnWindowClosed;

            _window = window;
            _themeService.RegisterWindow(window);
            window.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顯示迷你控制列失敗");
            Close();
        }
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            Close();
            return;
        }

        Show();
    }

    public void Close()
    {
        MiniOverlayWindow? window = _window;
        _window = null;
        window?.Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is not MiniOverlayWindow window)
        {
            return;
        }

        window.Closed -= OnWindowClosed;
        _themeService.UnregisterWindow(window);
        if (ReferenceEquals(_window, window))
        {
            _window = null;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        MiniOverlayWindow? window = _window;
        if (window is null)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_window, window))
            {
                window.SetExcludeFromCapture(settings.ExcludeOverlayFromCapture);
            }
        });
    }
}
