namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 多語系資源讀取介面（支援 WinUI 3 ResourceLoader 與單元測試後備讀取）。
/// </summary>
public interface ILocalizationService
{
    string GetString(string key);

    string Format(string key, params object[] args);
}
