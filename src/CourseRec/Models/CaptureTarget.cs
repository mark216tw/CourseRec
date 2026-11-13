namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 代表一個可被擷取的螢幕顯示器或應用程式視窗。
/// </summary>
public sealed record CaptureTarget(
    string Id,
    CaptureTargetKind Kind,
    string Title,
    string Subtitle,
    nint Handle,
    uint ProcessId,
    int Width,
    int Height)
{
    /// <summary>
    /// 是否支援單一視窗程序音訊隔離。
    /// </summary>
    public bool SupportsProcessAudioIsolation => Kind == CaptureTargetKind.Window && ProcessId != 0;

    /// <summary>
    /// 顯示用解析度字串。
    /// </summary>
    public string ResolutionText => $"{Width} × {Height}";

    /// <summary>
    /// 來源圖示（視窗為應用程式圖示、顯示器為系統螢幕圖示）；無法取得時為 <c>null</c>。
    /// </summary>
    public IconData? Icon { get; init; }

    public override string ToString() => Title;
}
