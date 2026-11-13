using System.Text.Json.Serialization;

namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 使用者個人化設定模型（儲存於 <c>%AppData%\CourseRec\settings.json</c>）。
/// </summary>
public sealed class AppSettings
{
    public string OutputDirectory { get; set; } = string.Empty;

    public AppDisplayMode DisplayMode { get; set; } = AppDisplayMode.System;

    public AppColorTheme ColorTheme { get; set; } = AppColorTheme.Ocean;

    /// <summary>
    /// 舊版合併式主題設定，只用於讀取舊設定檔後轉換；寫入時忽略。
    /// </summary>
    [JsonPropertyName("themePreference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppThemePreference? LegacyThemePreference { get; set; }

    public ScenarioMode DefaultScenarioMode { get; set; } = ScenarioMode.ListenOnly;

    public QualityPreset DefaultQualityPreset { get; set; } = QualityPreset.SlidePresentation;

    public int DefaultAutoStopMinutes { get; set; }

    public int RecordingCountdownSeconds { get; set; } = 5;

    public bool IsLivePreviewEnabledByDefault { get; set; }

    public WindowPlacement? WindowPlacement { get; set; }

    public MiniOverlayPosition? MiniOverlayPosition { get; set; }

    public bool ExcludeOverlayFromCapture { get; set; } = true;

    public bool IsolateWindowAudioByDefault { get; set; } = true;

    public string RecordingFileNamePrefix { get; set; } = "Course";

    public string? LastEditorVideoPath { get; set; }

    public long LastEditorPositionTicks { get; set; }

    public LibrarySortOrder LibrarySortOrder { get; set; } = LibrarySortOrder.NewestFirst;

    public NavigationPageKey LastPage { get; set; } = NavigationPageKey.Recorder;

    public string ToggleStartStopHotkey { get; set; } = "Ctrl+Shift+R";

    public string TogglePauseHotkey { get; set; } = "Ctrl+Shift+P";

    public string AddBookmarkHotkey { get; set; } = "Ctrl+Shift+B";

    public string TakeSnapshotHotkey { get; set; } = "Ctrl+Shift+S";

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
