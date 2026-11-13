using Microsoft.UI.Xaml.Data;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 將位元組數（<see cref="long"/>）轉換為易讀檔案大小字串的 XAML 轉換器。
/// </summary>
public sealed partial class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            long longBytes => FormatHelper.FormatBytes(longBytes),
            int intBytes => FormatHelper.FormatBytes(intBytes),
            _ => FormatHelper.FormatBytes(0),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
