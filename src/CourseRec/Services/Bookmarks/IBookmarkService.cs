using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Bookmarks;

/// <summary>
/// 隨附 <c>.bookmarks.json</c> 讀寫與剪輯後時間戳自動校正服務介面。
/// </summary>
public interface IBookmarkService
{
    string GetBookmarkFilePath(string videoFilePath);

    Task<BookmarkDocument> LoadForVideoAsync(string videoFilePath, CancellationToken cancellationToken = default);

    Task SaveForVideoAsync(
        string videoFilePath,
        TimeSpan totalDuration,
        IReadOnlyList<BookmarkItem> bookmarks,
        CancellationToken cancellationToken = default);

    IReadOnlyList<TrimSegment> BuildKeptSegments(
        TimeSpan totalDuration,
        TimeSpan trimStart,
        TimeSpan trimEnd,
        IReadOnlyList<TrimSegment> cutSegments);

    IReadOnlyList<BookmarkItem> RecalculateBookmarksAfterEdit(
        IReadOnlyList<BookmarkItem> originalBookmarks,
        TimeSpan totalDuration,
        TimeSpan trimStart,
        TimeSpan trimEnd,
        IReadOnlyList<TrimSegment> cutSegments);
}
