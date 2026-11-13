using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Capture;

/// <summary>
/// <c>Windows.Graphics.Capture</c> 螢幕／視窗擷取、即時預覽與 PNG 投影片快拍服務介面。
/// </summary>
public interface IScreenCaptureService : IDisposable
{
    CaptureTarget? ActiveTarget { get; }

    bool IsCapturing { get; }

    bool IsTargetMinimized { get; }

    long MinCaptureIntervalTicks { get; set; }

    bool IsPreviewEnabled { get; set; }

    int PreviewMaxDimension { get; set; }

    // 事件影格僅在回呼期間有效；非同步使用需 Retain 並 Dispose。
    event EventHandler<CapturedFrameData>? PreviewFrameAvailable;

    event EventHandler<bool>? TargetMinimizedChanged;

    event EventHandler? TargetClosed;

    bool StartCapture(CaptureTarget target);

    void StopCapture();

    /// <summary>取得最新影格的使用權；呼叫者必須 Dispose。</summary>
    CapturedFrameData? GetLatestFrameSnapshot();

    Task<string?> SaveSnapshotPngAsync(
        string outputDirectory,
        bool copyToClipboard = true,
        CancellationToken cancellationToken = default);
}
