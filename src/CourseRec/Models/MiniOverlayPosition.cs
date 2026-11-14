namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 迷你控制列的上次視窗位置與大小（大小以 DIP 為單位）。
/// </summary>
public sealed record MiniOverlayPosition(
    int Left,
    int Top,
    double WidthDip = 0,
    double HeightDip = 0);
