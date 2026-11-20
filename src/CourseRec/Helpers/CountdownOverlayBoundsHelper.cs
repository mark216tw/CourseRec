namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 計算大型倒數覆蓋層在目標螢幕或目標視窗上的置中位置，並限制於工作區可視範圍內。
/// </summary>
public static class CountdownOverlayBoundsHelper
{
    public static (int Left, int Top) CalculateCenteredPosition(
        int targetX,
        int targetY,
        int targetWidth,
        int targetHeight,
        int overlayWidth,
        int overlayHeight,
        int workAreaX,
        int workAreaY,
        int workAreaWidth,
        int workAreaHeight)
    {
        int safeOverlayWidth = Math.Max(1, overlayWidth);
        int safeOverlayHeight = Math.Max(1, overlayHeight);

        int desiredLeft = targetX + ((targetWidth - safeOverlayWidth) / 2);
        int desiredTop = targetY + ((targetHeight - safeOverlayHeight) / 2);

        int maxLeft = Math.Max(workAreaX, workAreaX + workAreaWidth - safeOverlayWidth);
        int maxTop = Math.Max(workAreaY, workAreaY + workAreaHeight - safeOverlayHeight);

        return (
            Math.Clamp(desiredLeft, workAreaX, maxLeft),
            Math.Clamp(desiredTop, workAreaY, maxTop));
    }
}
