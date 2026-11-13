using Microsoft.UI.Xaml.Data;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 布林值反轉 XAML 轉換器。
/// </summary>
public sealed partial class BooleanNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool flag ? !flag : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool flag && !flag;
    }
}
