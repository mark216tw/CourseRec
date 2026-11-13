namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 錄影工作台狀態機狀態。
/// </summary>
public enum RecordingState
{
    /// <summary>
    /// 待命中（可預覽畫面與監控音量）。
    /// </summary>
    Idle = 0,

    /// <summary>
    /// 開錄前 3 秒倒數中。
    /// </summary>
    Countdown = 1,

    /// <summary>
    /// 正在錄製寫入中。
    /// </summary>
    Recording = 2,

    /// <summary>
    /// 錄影暫停中（自動扣除暫停時間差）。
    /// </summary>
    Paused = 3,

    /// <summary>
    /// 正在安全封裝並寫入檔尾與書籤。
    /// </summary>
    Finalizing = 4,
}
