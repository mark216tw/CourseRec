namespace Com.VibeCode.CourseRec.Models;

/// <summary>
/// 對應 <c>appsettings.json</c> 中 <c>CourseRecDefaults</c> 區段的初始預設值。
/// </summary>
public sealed class CourseRecDefaultOptions
{
    public const string SectionName = "CourseRecDefaults";

    public int DefaultAutoStopMinutes { get; set; }

    public int DefaultRecordingCountdownSeconds { get; set; } = 5;

    public bool DefaultIsLivePreviewEnabled { get; set; }

    public bool DefaultExcludeOverlayFromCapture { get; set; } = true;

    public QualityPreset DefaultQualityPreset { get; set; } = QualityPreset.SlidePresentation;

    public ScenarioMode DefaultScenarioMode { get; set; } = ScenarioMode.ListenOnly;

    public AppDisplayMode DefaultDisplayMode { get; set; } = AppDisplayMode.System;

    public AppColorTheme DefaultColorTheme { get; set; } = AppColorTheme.Ocean;

    public string ToggleStartStopHotkey { get; set; } = "Ctrl+Shift+R";

    public string TogglePauseHotkey { get; set; } = "Ctrl+Shift+P";

    public string AddBookmarkHotkey { get; set; } = "Ctrl+Shift+B";

    public string TakeSnapshotHotkey { get; set; } = "Ctrl+Shift+S";
}
