namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 管理迷你控制列視窗生命週期的服務介面。
/// </summary>
public interface IMiniOverlayService
{
    bool IsVisible { get; }

    void Show();

    void Toggle();

    void Close();
}
