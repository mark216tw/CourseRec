namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 快剪編輯器匯出格式。
/// </summary>
public enum ExportFormat
{
    /// <summary>
    /// 剪輯後 MP4 課程影片（H.264 + AAC）。
    /// </summary>
    Mp4Video = 0,

    /// <summary>
    /// M4A 輕量純音訊檔（供 NotebookLM / Whisper 等 AI 逐字稿工具使用）。
    /// </summary>
    M4aAudioOnly = 1,
}
