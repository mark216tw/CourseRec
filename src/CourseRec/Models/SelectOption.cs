namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 下拉選單選項包裝（搭配多語系顯示文字與強型別值）。
/// </summary>
public sealed record SelectOption<T>(T Value, string DisplayText)
{
    public override string ToString() => DisplayText;
}
