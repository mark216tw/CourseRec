namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 工作列錄影中覆蓋圖示（紅點）控制。
/// </summary>
public interface ITaskbarIndicatorService
{
    void AttachWindow(nint windowHandle);

    void SetRecording(bool isRecording);
}
