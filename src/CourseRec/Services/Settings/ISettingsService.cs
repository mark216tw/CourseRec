using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services.Settings;

/// <summary>
/// 使用者設定讀寫服務介面。
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task SaveWindowPlacementAsync(WindowPlacement placement, CancellationToken cancellationToken = default);

    Task SaveMiniOverlayPositionAsync(
        MiniOverlayPosition position,
        CancellationToken cancellationToken = default);

    Task SaveLibrarySortOrderAsync(
        LibrarySortOrder sortOrder,
        CancellationToken cancellationToken = default);

    Task SaveLastPageAsync(
        NavigationPageKey page,
        CancellationToken cancellationToken = default);

    Task SaveAppearanceAsync(
        AppDisplayMode displayMode,
        AppColorTheme colorTheme,
        CancellationToken cancellationToken = default);

    Task SaveRecordingPreferencesAsync(
        ScenarioMode scenarioMode,
        QualityPreset qualityPreset,
        int autoStopMinutes,
        CancellationToken cancellationToken = default);

    Task SaveLastEditorStateAsync(
        string? videoPath,
        long positionTicks,
        CancellationToken cancellationToken = default);
}
