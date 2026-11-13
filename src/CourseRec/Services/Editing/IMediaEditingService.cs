using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Editing;

/// <summary>
/// 基於 <c>Windows.Media.Editing.MediaComposition</c> 的影片長度讀取、播放影格快拍與快剪匯出介面。
/// </summary>
public interface IMediaEditingService
{
    Task<TimeSpan> GetVideoDurationAsync(
        string videoFilePath,
        CancellationToken cancellationToken = default);

    Task<string?> CaptureVideoFrameToPngAsync(
        string videoFilePath,
        TimeSpan position,
        string outputDirectory,
        bool copyToClipboard = true,
        CancellationToken cancellationToken = default);

    Task ExportEditedMediaAsync(
        string sourceVideoPath,
        string destinationFilePath,
        ExportFormat format,
        IReadOnlyList<TrimSegment> keptSegments,
        double audioGainMultiplier,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
