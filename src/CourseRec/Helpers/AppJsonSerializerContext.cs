using System.Text.Json.Serialization;
using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 編譯期 JSON 序列化上下文，避免啟動與讀寫設定／書籤時觸發反射與動態 IL 產生。
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(BookmarkDocument))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
{
}
