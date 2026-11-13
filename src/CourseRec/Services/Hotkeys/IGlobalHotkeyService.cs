using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Hotkeys;

/// <summary>
/// 全域快捷鍵註冊與解析服務介面。
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    event EventHandler<HotkeyAction>? HotkeyPressed;

    void AttachWindow(nint windowHandle);

    IReadOnlyList<HotkeyAction> ApplyHotkeys(AppSettings settings);

    void UnregisterAll();
}
