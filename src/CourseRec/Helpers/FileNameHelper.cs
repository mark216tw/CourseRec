using System.Text;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 檔名前綴清理工具。
/// </summary>
public static class FileNameHelper
{
    public const string DefaultPrefix = "Course";

    public static string SanitizePrefix(string? prefix)
    {
        string trimmed = (prefix ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return DefaultPrefix;
        }

        char[] invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(trimmed.Length);
        foreach (char ch in trimmed)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        string sanitized = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? DefaultPrefix : sanitized;
    }
}
