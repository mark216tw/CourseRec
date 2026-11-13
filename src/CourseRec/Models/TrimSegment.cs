using System.Text.Json.Serialization;

namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 表示一段時間區間（用於保留區間或待剪除的中場休息區間）。
/// </summary>
public sealed record TrimSegment(
    long StartTicks,
    long EndTicks,
    string Label)
{
    [JsonIgnore]
    public TimeSpan Start => TimeSpan.FromTicks(Math.Max(0L, StartTicks));

    [JsonIgnore]
    public TimeSpan End => TimeSpan.FromTicks(Math.Max(StartTicks, EndTicks));

    [JsonIgnore]
    public TimeSpan Duration => End - Start;

    [JsonIgnore]
    public string RangeDisplay => $"{Start:hh\\:mm\\:ss} – {End:hh\\:mm\\:ss}";
}
