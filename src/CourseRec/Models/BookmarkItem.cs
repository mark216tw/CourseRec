using System.Text.Json.Serialization;

namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 單筆課程時間書籤紀錄。
/// </summary>
public sealed record BookmarkItem(
    Guid Id,
    long TimestampTicks,
    string Note,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>
    /// 書籤對應的影片時間點。
    /// </summary>
    [JsonIgnore]
    public TimeSpan Timestamp => TimeSpan.FromTicks(Math.Max(0L, TimestampTicks));

    /// <summary>
    /// 格式化時間顯示（HH:mm:ss）。
    /// </summary>
    [JsonIgnore]
    public string FormattedTimestamp => Timestamp.ToString(@"hh\:mm\:ss");
}
