namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 主視窗還原位置、大小與最大化狀態。
/// </summary>
public sealed record WindowPlacement(
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsMaximized);
