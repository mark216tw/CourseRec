namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 隨附於錄影檔旁的 <c>.bookmarks.json</c> 文件結構。
/// </summary>
public sealed class BookmarkDocument
{
    public string VideoFileName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public long TotalDurationTicks { get; set; }

    public List<BookmarkItem> Bookmarks { get; set; } = [];
}
