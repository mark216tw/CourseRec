namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 表示已解碼的圖示像素資料（BGRA、預乘 alpha、由上而下排列）。
/// </summary>
public sealed record IconData(int Width, int Height, byte[] Bgra)
{
    /// <summary>
    /// 像素緩衝區預期長度（位元組）。
    /// </summary>
    public int ExpectedLength => Width * Height * 4;
}
