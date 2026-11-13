using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services.Hotkeys;
using Com.VibeCode.CourseRec.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;

namespace Com.VibeCode.CourseRec.ViewModels;

/// <summary>
/// 設定頁面 ViewModel。
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _suppressAppearanceSave;

    [ObservableProperty]
    public partial string OutputDirectory { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RecordingFileNamePrefix { get; set; } = FileNameHelper.DefaultPrefix;

    [ObservableProperty]
    public partial bool ExcludeOverlayFromCapture { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLivePreviewEnabledByDefault { get; set; }

    [ObservableProperty]
    public partial SelectOption<int>? SelectedCountdownOption { get; set; }

    [ObservableProperty]
    public partial SelectOption<AppDisplayMode>? SelectedDisplayModeOption { get; set; }

    [ObservableProperty]
    public partial SelectOption<AppColorTheme>? SelectedColorThemeOption { get; set; }

    [ObservableProperty]
    public partial string ToggleStartStopHotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TogglePauseHotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AddBookmarkHotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TakeSnapshotHotkey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

    public SettingsViewModel(
        ISettingsService settingsService,
        IThemeService themeService,
        IGlobalHotkeyService hotkeyService,
        ILocalizationService localization,
        ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _themeService = themeService;
        _hotkeyService = hotkeyService;
        _localization = localization;
        _logger = logger;

        DisplayModeOptions =
        [
            new SelectOption<AppDisplayMode>(AppDisplayMode.System, _localization.GetString("DisplayModeSystemText")),
            new SelectOption<AppDisplayMode>(AppDisplayMode.Light, _localization.GetString("DisplayModeLightText")),
            new SelectOption<AppDisplayMode>(AppDisplayMode.Dark, _localization.GetString("DisplayModeDarkText")),
        ];

        ColorThemeOptions =
        [
            new SelectOption<AppColorTheme>(AppColorTheme.EyeCareYellow, _localization.GetString("ThemeEyeCareYellowText")),
            new SelectOption<AppColorTheme>(AppColorTheme.Ocean, _localization.GetString("ThemeOceanText")),
            new SelectOption<AppColorTheme>(AppColorTheme.Forest, _localization.GetString("ThemeForestText")),
            new SelectOption<AppColorTheme>(AppColorTheme.Sunset, _localization.GetString("ThemeSunsetText")),
            new SelectOption<AppColorTheme>(AppColorTheme.Lavender, _localization.GetString("ThemeLavenderText")),
        ];

        CountdownOptions =
        [
            new SelectOption<int>(0, _localization.GetString("RecordingCountdownImmediateText")),
            new SelectOption<int>(3, _localization.Format("RecordingCountdownSecondsFormat", 3)),
            new SelectOption<int>(5, _localization.Format("RecordingCountdownSecondsFormat", 5)),
            new SelectOption<int>(10, _localization.Format("RecordingCountdownSecondsFormat", 10)),
            new SelectOption<int>(15, _localization.Format("RecordingCountdownSecondsFormat", 15)),
            new SelectOption<int>(20, _localization.Format("RecordingCountdownSecondsFormat", 20)),
            new SelectOption<int>(25, _localization.Format("RecordingCountdownSecondsFormat", 25)),
            new SelectOption<int>(30, _localization.Format("RecordingCountdownSecondsFormat", 30)),
        ];

        SyncFromSettings(_settingsService.Current);
    }

    public ObservableCollection<SelectOption<AppDisplayMode>> DisplayModeOptions { get; }

    public ObservableCollection<SelectOption<AppColorTheme>> ColorThemeOptions { get; }

    public ObservableCollection<SelectOption<int>> CountdownOptions { get; }

    partial void OnSelectedDisplayModeOptionChanged(SelectOption<AppDisplayMode>? value)
    {
        if (value is not null)
        {
            _themeService.ApplyTheme(value.Value, _themeService.CurrentColorTheme);
            PersistAppearanceIfNeeded();
        }
    }

    partial void OnSelectedColorThemeOptionChanged(SelectOption<AppColorTheme>? value)
    {
        if (value is not null)
        {
            _themeService.ApplyTheme(_themeService.CurrentDisplayMode, value.Value);
            PersistAppearanceIfNeeded();
        }
    }

    public void SyncFromSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        bool previousSuppressState = _suppressAppearanceSave;
        _suppressAppearanceSave = true;
        try
        {
            OutputDirectory = settings.OutputDirectory;
            RecordingFileNamePrefix = settings.RecordingFileNamePrefix;
            ExcludeOverlayFromCapture = settings.ExcludeOverlayFromCapture;
            IsLivePreviewEnabledByDefault = settings.IsLivePreviewEnabledByDefault;
            SelectedCountdownOption = CountdownOptions.FirstOrDefault(o => o.Value == settings.RecordingCountdownSeconds)
                ?? CountdownOptions.First(o => o.Value == 5);
            ToggleStartStopHotkey = settings.ToggleStartStopHotkey;
            TogglePauseHotkey = settings.TogglePauseHotkey;
            AddBookmarkHotkey = settings.AddBookmarkHotkey;
            TakeSnapshotHotkey = settings.TakeSnapshotHotkey;
            SelectedColorThemeOption = ColorThemeOptions.FirstOrDefault(o => o.Value == settings.ColorTheme)
                ?? ColorThemeOptions.First(o => o.Value == AppColorTheme.Ocean);
            SelectedDisplayModeOption = DisplayModeOptions.FirstOrDefault(o => o.Value == settings.DisplayMode)
                ?? DisplayModeOptions[0];
        }
        finally
        {
            _suppressAppearanceSave = previousSuppressState;
        }
    }

    private void PersistAppearanceIfNeeded()
    {
        if (_suppressAppearanceSave || SelectedDisplayModeOption is null || SelectedColorThemeOption is null)
        {
            return;
        }

        _ = PersistAppearanceAsync(SelectedDisplayModeOption.Value, SelectedColorThemeOption.Value);
    }

    private async Task PersistAppearanceAsync(AppDisplayMode displayMode, AppColorTheme colorTheme)
    {
        try
        {
            await _settingsService.SaveAppearanceAsync(displayMode, colorTheme);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "儲存外觀設定失敗");
        }
    }

    [RelayCommand]
    private void ResetHotkeysToDefaults()
    {
        var defaults = new AppSettings();
        ToggleStartStopHotkey = defaults.ToggleStartStopHotkey;
        TogglePauseHotkey = defaults.TogglePauseHotkey;
        AddBookmarkHotkey = defaults.AddBookmarkHotkey;
        TakeSnapshotHotkey = defaults.TakeSnapshotHotkey;

        StatusSeverity = InfoBarSeverity.Informational;
        StatusMessage = _localization.GetString("HotkeyResetText");
        IsStatusOpen = true;
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!TryValidateHotkeys())
        {
            return;
        }

        if (!await TryValidateOutputDirectoryAsync())
        {
            return;
        }

        try
        {
            AppSettings current = _settingsService.Current;
            var updated = new AppSettings
            {
                OutputDirectory = OutputDirectory.Trim(),
                RecordingFileNamePrefix = FileNameHelper.SanitizePrefix(RecordingFileNamePrefix),
                ExcludeOverlayFromCapture = ExcludeOverlayFromCapture,
                IsLivePreviewEnabledByDefault = IsLivePreviewEnabledByDefault,
                RecordingCountdownSeconds = SelectedCountdownOption?.Value ?? 5,
                DisplayMode = SelectedDisplayModeOption?.Value ?? AppDisplayMode.System,
                ColorTheme = SelectedColorThemeOption?.Value ?? AppColorTheme.Ocean,
                DefaultScenarioMode = current.DefaultScenarioMode,
                DefaultQualityPreset = current.DefaultQualityPreset,
                DefaultAutoStopMinutes = current.DefaultAutoStopMinutes,
                IsolateWindowAudioByDefault = current.IsolateWindowAudioByDefault,
                LastEditorVideoPath = current.LastEditorVideoPath,
                LastEditorPositionTicks = current.LastEditorPositionTicks,
                LibrarySortOrder = current.LibrarySortOrder,
                LastPage = current.LastPage,
                WindowPlacement = current.WindowPlacement,
                MiniOverlayPosition = current.MiniOverlayPosition,
                ToggleStartStopHotkey = ToggleStartStopHotkey,
                TogglePauseHotkey = TogglePauseHotkey,
                AddBookmarkHotkey = AddBookmarkHotkey,
                TakeSnapshotHotkey = TakeSnapshotHotkey,
            };

            await _settingsService.SaveAsync(updated);
            _themeService.ApplyTheme(updated.DisplayMode, updated.ColorTheme);
            SyncFromSettings(_settingsService.Current);

            IReadOnlyList<HotkeyAction> failedActions = _hotkeyService.ApplyHotkeys(_settingsService.Current);
            if (failedActions.Count > 0)
            {
                string labels = string.Join(
                    _localization.GetString("ListSeparatorText"),
                    failedActions.Select(GetHotkeyActionLabel));
                StatusSeverity = InfoBarSeverity.Warning;
                StatusMessage = _localization.Format("HotkeyConflictFormat", labels);
            }
            else
            {
                StatusSeverity = InfoBarSeverity.Success;
                StatusMessage = _localization.GetString("SettingsSavedSuccessMessage");
            }

            IsStatusOpen = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存設定失敗");
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = _localization.GetString("SettingsSaveFailedText");
            IsStatusOpen = true;
        }
    }

    [RelayCommand]
    private void OpenOutputDirectory()
    {
        try
        {
            string outputDirectory = string.IsNullOrWhiteSpace(OutputDirectory)
                ? _settingsService.Current.OutputDirectory
                : OutputDirectory;
            Directory.CreateDirectory(outputDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "開啟輸出資料夾失敗");
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = _localization.GetString("SettingsOpenOutputFailedText");
            IsStatusOpen = true;
        }
    }

    private async Task<bool> TryValidateOutputDirectoryAsync()
    {
        string directory = OutputDirectory.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            ShowStatus(InfoBarSeverity.Error, _localization.GetString("OutputDirectoryInvalidText"));
            return false;
        }

        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(directory);
                string probePath = Path.Combine(directory, $".courserec_write_test_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "驗證輸出資料夾失敗：{Directory}", directory);
            ShowStatus(InfoBarSeverity.Error, _localization.GetString("OutputDirectoryInvalidText"));
            return false;
        }

        OutputDirectory = directory;
        return true;
    }

    private void ShowStatus(InfoBarSeverity severity, string message)
    {
        StatusSeverity = severity;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    private bool TryValidateHotkeys()
    {
        (string Gesture, string LabelKey)[] entries =
        [
            (ToggleStartStopHotkey, "TxtHotkeyStartStop.Header"),
            (TogglePauseHotkey, "TxtHotkeyPause.Header"),
            (AddBookmarkHotkey, "TxtHotkeyBookmark.Header"),
            (TakeSnapshotHotkey, "TxtHotkeySnapshot.Header"),
        ];

        foreach ((string gesture, string labelKey) in entries)
        {
            if (!string.IsNullOrWhiteSpace(gesture)
                && !GlobalHotkeyService.TryParseHotkeyGesture(gesture, out _, out _))
            {
                StatusSeverity = InfoBarSeverity.Error;
                StatusMessage = _localization.Format("HotkeyInvalidFormat", _localization.GetString(labelKey));
                IsStatusOpen = true;
                return false;
            }
        }

        return true;
    }

    private string GetHotkeyActionLabel(HotkeyAction action) => action switch
    {
        HotkeyAction.ToggleStartStop => _localization.GetString("TxtHotkeyStartStop.Header"),
        HotkeyAction.TogglePauseResume => _localization.GetString("TxtHotkeyPause.Header"),
        HotkeyAction.AddBookmark => _localization.GetString("TxtHotkeyBookmark.Header"),
        HotkeyAction.TakeSnapshot => _localization.GetString("TxtHotkeySnapshot.Header"),
        _ => action.ToString(),
    };
}
