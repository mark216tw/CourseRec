namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 顯示模式偏好（淺色／深色由主題顏色提供對應色階）。
/// </summary>
public enum AppDisplayMode
{
    /// <summary>
    /// 跟隨系統設定。
    /// </summary>
    System = 0,

    /// <summary>
    /// 淺色模式。
    /// </summary>
    Light = 1,

    /// <summary>
    /// 深色模式。
    /// </summary>
    Dark = 2,
}
