using System.Runtime.InteropServices.WindowsRuntime;
using Com.VibeCode.CourseRec.Models;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 將 <see cref="IconData"/> 轉為可綁定的 <see cref="WriteableBitmap"/>。
/// </summary>
public sealed partial class IconDataToImageSourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not IconData icon
            || icon.Width <= 0
            || icon.Height <= 0
            || icon.Bgra.Length != icon.ExpectedLength)
        {
            return null!;
        }

        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        using (Stream pixelStream = bitmap.PixelBuffer.AsStream())
        {
            pixelStream.Write(icon.Bgra, 0, icon.Bgra.Length);
        }

        return bitmap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
