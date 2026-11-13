using Com.VibeCode.CourseRec.Models;
using Microsoft.UI.Xaml;

namespace Com.VibeCode.CourseRec.Services.Settings;

/// <summary>
/// 動態切換視窗顯示模式與主題顏色的服務介面。
/// </summary>
public interface IThemeService
{
    AppDisplayMode CurrentDisplayMode { get; }

    AppColorTheme CurrentColorTheme { get; }

    void RegisterWindow(Window window);

    void UnregisterWindow(Window window);

    void ApplyTheme(AppDisplayMode displayMode, AppColorTheme colorTheme);
}
