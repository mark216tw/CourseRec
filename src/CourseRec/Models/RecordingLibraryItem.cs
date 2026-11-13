namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 錄影檔案庫單一項目模型。
/// </summary>
public sealed record RecordingLibraryItem(
    string FilePath,
    string FileName,
    DateTimeOffset CreatedAtLocal,
    TimeSpan Duration,
    long FileSizeBytes,
    int BookmarkCount,
    string BookmarkFilePath,
    string BookmarkBadgeText,
    string BookmarkSearchText = "")
{
    public bool HasBookmarks => BookmarkCount > 0;

    public string CreatedAtText => CreatedAtLocal.ToString("yyyy-MM-dd HH:mm");

    public string DurationText => Duration.ToString(@"hh\:mm\:ss");
}
