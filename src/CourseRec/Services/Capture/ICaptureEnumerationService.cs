using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Capture;

/// <summary>
/// 螢幕顯示器與應用程式視窗清單枚舉服務介面。
/// </summary>
public interface ICaptureEnumerationService
{
    IReadOnlyList<CaptureTarget> GetMonitors();

    IReadOnlyList<CaptureTarget> GetCapturableWindows();
}
