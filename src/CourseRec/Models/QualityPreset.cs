namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 錄影畫質與空間預設檔。
/// </summary>
public enum QualityPreset
{
    /// <summary>
    /// 簡報／會議省空間（上限 1080p、15 fps、低碼率清晰文字）。
    /// </summary>
    SlidePresentation = 0,

    /// <summary>
    /// 標準流暢（原生偶數對齊解析度、30 fps、動態影像適用）。
    /// </summary>
    StandardSmooth = 1,
}
