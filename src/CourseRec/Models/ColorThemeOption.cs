using Windows.UI;

namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 主題顏色選項；以強調色色塊供選單顯示。
/// </summary>
public sealed class ColorThemeOption
{
    public ColorThemeOption(
        AppColorTheme value,
        string displayText,
        Color accentColor)
    {
        Value = value;
        DisplayText = displayText;
        AccentColor = accentColor;
    }

    public AppColorTheme Value { get; }

    public string DisplayText { get; }

    public Color AccentColor { get; }

    public override string ToString() => DisplayText;
}
