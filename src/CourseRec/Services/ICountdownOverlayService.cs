using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 管理錄影開始前在 Windows 畫面中央顯示的大型倒數計時覆蓋視窗。
/// </summary>
public interface ICountdownOverlayService
{
    void ShowOrUpdate(int remainingSeconds, CaptureTarget? target);

    void Hide();
}
