using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 建立並管理大型錄影倒數覆蓋視窗的生命週期。
/// </summary>
public sealed class CountdownOverlayService : ICountdownOverlayService
{
    private readonly ILocalizationService _localization;
    private readonly IThemeService _themeService;
    private readonly ILogger<CountdownOverlayWindow> _windowLogger;
    private readonly ILogger<CountdownOverlayService> _logger;

    private CountdownOverlayWindow? _window;

    public CountdownOverlayService(
        ILocalizationService localization,
        IThemeService themeService,
        ILogger<CountdownOverlayWindow> windowLogger,
        ILogger<CountdownOverlayService> logger)
    {
        _localization = localization;
        _themeService = themeService;
        _windowLogger = windowLogger;
        _logger = logger;
    }

    public void ShowOrUpdate(int remainingSeconds, CaptureTarget? target)
    {
        if (remainingSeconds <= 0)
        {
            Hide();
            return;
        }

        try
        {
            bool isNewWindow = false;
            CountdownOverlayWindow? window = _window;
            if (window is null)
            {
                window = new CountdownOverlayWindow(_localization, _windowLogger);
                window.Closed += OnWindowClosed;
                _window = window;
                _themeService.RegisterWindow(window);
                isNewWindow = true;
            }

            window.UpdateCountdown(remainingSeconds, target);
            if (isNewWindow)
            {
                window.ShowWithoutActivating();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顯示錄影倒數覆蓋視窗失敗");
            Hide();
        }
    }

    public void Hide()
    {
        CountdownOverlayWindow? window = _window;
        _window = null;
        if (window is null)
        {
            return;
        }

        try
        {
            window.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "關閉錄影倒數覆蓋視窗失敗");
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (sender is not CountdownOverlayWindow window)
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
}
