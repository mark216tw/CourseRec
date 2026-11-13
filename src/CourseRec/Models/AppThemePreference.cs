namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 舊版合併式顯示主題偏好，僅保留供設定檔遷移使用。
/// </summary>
public enum AppThemePreference
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

    /// <summary>
    /// 海洋藍（淺色）。
    /// </summary>
    OceanLight = 3,

    /// <summary>
    /// 海洋藍（深色）。
    /// </summary>
    OceanDark = 4,

    /// <summary>
    /// 森林綠（淺色）。
    /// </summary>
    ForestLight = 5,

    /// <summary>
    /// 森林綠（深色）。
    /// </summary>
    ForestDark = 6,

    /// <summary>
    /// 暖陽橘（淺色）。
    /// </summary>
    SunsetLight = 7,

    /// <summary>
    /// 暖陽橘（深色）。
    /// </summary>
    SunsetDark = 8,

    /// <summary>
    /// 薰衣草紫（淺色）。
    /// </summary>
    LavenderLight = 9,

    /// <summary>
    /// 薰衣草紫（深色）。
    /// </summary>
    LavenderDark = 10,

    /// <summary>
    /// 護眼黃。
    /// </summary>
    EyeCareYellow = 11,

    /// <summary>
    /// 護眼綠。
    /// </summary>
    EyeCareGreen = 12,
}
