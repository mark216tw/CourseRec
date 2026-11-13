using Microsoft.UI.Xaml.Data;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 將 <see cref="TimeSpan"/> 轉換為 <c>HH:mm:ss</c> 格式的 XAML 轉換器。
/// </summary>
public sealed partial class TimeSpanFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is TimeSpan ts
            ? FormatHelper.FormatDuration(ts)
            : "00:00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
