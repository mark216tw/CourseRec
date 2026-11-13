using System.Globalization;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 檔案大小與時間長度格式化工具。
/// </summary>
public static class FormatHelper
{
    public static string FormatBytes(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024.0 && unitIndex < units.Length - 1)
        {
            value /= 1024.0;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Format(CultureInfo.InvariantCulture, "{0:0} {1}", value, units[unitIndex])
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0} {1}", value, units[unitIndex]);
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        int totalHours = (int)duration.TotalHours;
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:D2}:{1:D2}:{2:D2}",
            totalHours,
            duration.Minutes,
            duration.Seconds);
    }
}
