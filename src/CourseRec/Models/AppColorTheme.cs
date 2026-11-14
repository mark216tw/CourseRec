namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 主題顏色偏好；實際明暗色階依 <see cref="AppDisplayMode"/> 決定。
/// </summary>
public enum AppColorTheme
{
    /// <summary>
    /// 海洋藍。
    /// </summary>
    Ocean = 0,

    /// <summary>
    /// 護眼黃。
    /// </summary>
    EyeCareYellow = 1,

    /// <summary>
    /// 護眼綠（已停用，僅保留舊設定檔讀取相容；載入時自動轉為 <see cref="EyeCareYellow"/>）。
    /// </summary>
    EyeCareGreen = 2,

    /// <summary>
    /// 森林綠。
    /// </summary>
    Forest = 3,

    /// <summary>
    /// 暖陽橘。
    /// </summary>
    Sunset = 4,

    /// <summary>
    /// 薰衣草紫。
    /// </summary>
    Lavender = 5,

    /// <summary>
    /// 玫瑰粉。
    /// </summary>
    Rose = 6,

    /// <summary>
    /// 薄荷綠。
    /// </summary>
    Mint = 7,

    /// <summary>
    /// 石墨灰。
    /// </summary>
    Graphite = 8,

    /// <summary>
    /// 可可棕。
    /// </summary>
    Cocoa = 9,

    /// <summary>
    /// 天空藍（已停用，僅保留舊設定檔讀取相容；載入時自動轉為 <see cref="Indigo"/>）。
    /// </summary>
    Sky = 10,

    /// <summary>
    /// 靛藍。
    /// </summary>
    Indigo = 11,
}
