using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 於主視窗工作列圖示疊加紅色錄影圓點，停止錄影時移除。
/// </summary>
public sealed class TaskbarIndicatorService : ITaskbarIndicatorService, IDisposable
{
    private const int OverlayIconSize = 32;

    private readonly ILocalizationService _localization;
    private readonly ILogger<TaskbarIndicatorService> _logger;

    private ITaskbarList3? _taskbar;
    private nint _windowHandle;
    private nint _indicatorIcon;
    private bool _iconLoadAttempted;
    private bool _isRecording;
    private bool _disposed;

    public TaskbarIndicatorService(
        ILocalizationService localization,
        ILogger<TaskbarIndicatorService> logger)
    {
        _localization = localization;
        _logger = logger;
    }

    public void AttachWindow(nint windowHandle)
    {
        _windowHandle = windowHandle;
        if (_isRecording)
        {
            ApplyOverlay(isRecording: true);
        }
    }

    public void SetRecording(bool isRecording)
    {
        if (_disposed || _isRecording == isRecording)
        {
            return;
        }

        _isRecording = isRecording;
        ApplyOverlay(isRecording);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_taskbar is not null)
        {
            try
            {
                _taskbar.SetOverlayIcon(_windowHandle, 0, string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清除工作列錄影指示失敗");
            }
        }

        if (_indicatorIcon != 0)
        {
            TaskbarNativeMethods.DestroyIconHandle(_indicatorIcon);
            _indicatorIcon = 0;
        }

        if (_taskbar is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(_taskbar);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "釋放工作列 COM 物件失敗");
            }

            _taskbar = null;
        }
    }

    private void ApplyOverlay(bool isRecording)
    {
        if (_windowHandle == 0)
        {
            return;
        }

        try
        {
            _taskbar ??= TaskbarNativeMethods.CreateTaskbarList();
            if (_taskbar is null)
            {
                return;
            }

            if (isRecording)
            {
                nint icon = EnsureIndicatorIcon();
                if (icon == 0)
                {
                    return;
                }

                _taskbar.SetOverlayIcon(
                    _windowHandle,
                    icon,
                    _localization.GetString("TaskbarRecordingOverlayDescription"));
            }
            else
            {
                _taskbar.SetOverlayIcon(_windowHandle, 0, string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "更新工作列錄影指示失敗");
        }
    }

    private nint EnsureIndicatorIcon()
    {
        if (_indicatorIcon != 0 || _iconLoadAttempted)
        {
            return _indicatorIcon;
        }

        _iconLoadAttempted = true;
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "RecordingIndicator.ico");
        if (!File.Exists(iconPath))
        {
            _logger.LogWarning("找不到工作列錄影指示圖示：{IconPath}", iconPath);
            return 0;
        }

        _indicatorIcon = TaskbarNativeMethods.LoadIconFromFile(iconPath, OverlayIconSize);
        if (_indicatorIcon == 0)
        {
            _logger.LogWarning("載入工作列錄影指示圖示失敗：{IconPath}", iconPath);
        }

        return _indicatorIcon;
    }
}
