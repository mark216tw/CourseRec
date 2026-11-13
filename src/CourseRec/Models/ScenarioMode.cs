namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 錄影情境模式。
/// </summary>
public enum ScenarioMode
{
    /// <summary>
    /// 純聽課模式：開啟課程系統／視窗聲音，強制靜音麥克風以防環境雜音。
    /// </summary>
    ListenOnly = 0,

    /// <summary>
    /// 參與會議模式：同時錄製課程系統／視窗聲音與麥克風發言。
    /// </summary>
    InteractiveMeeting = 1,
}
