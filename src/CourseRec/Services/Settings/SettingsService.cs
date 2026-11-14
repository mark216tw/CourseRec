using System.Text.Json;
using System.Text.Json.Serialization;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Com.VibeCode.CourseRec.Services.Settings;

/// <summary>
/// 將使用者設定持久化至 <c>%AppData%\CourseRec\settings.json</c> 的服務實作。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly int[] SupportedCountdownSeconds = [0, 3, 5, 10, 15, 20, 25, 30];

    private readonly string _settingsFilePath;
    private readonly CourseRecDefaultOptions _defaults;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public SettingsService(
        IOptions<CourseRecDefaultOptions> defaultOptions,
        ILogger<SettingsService> logger)
        : this(
            GetDefaultSettingsFilePath(),
            defaultOptions?.Value ?? new CourseRecDefaultOptions(),
            logger)
    {
    }

    public SettingsService(
        string settingsFilePath,
        CourseRecDefaultOptions defaults,
        ILogger<SettingsService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        _settingsFilePath = settingsFilePath;
        _defaults = defaults ?? new CourseRecDefaultOptions();
        _logger = logger;
        Current = CreateDefaultSettings(_defaults);
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                Current = CreateDefaultSettings(_defaults);
                await WriteInternalAsync(Current, cancellationToken).ConfigureAwait(false);
                return Current;
            }

            string json = await File.ReadAllTextAsync(_settingsFilePath, cancellationToken).ConfigureAwait(false);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
            Current = Normalize(loaded ?? CreateDefaultSettings(_defaults), _defaults);
            return Current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "讀取設定檔失敗，改用預設設定：{FilePath}", _settingsFilePath);
            Current = CreateDefaultSettings(_defaults);
            return Current;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await SaveCoreAsync(settings, notifyChange: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveCoreAsync(
        AppSettings settings,
        bool notifyChange,
        CancellationToken cancellationToken)
    {
        AppSettings normalized = Normalize(settings, _defaults);
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteInternalAsync(normalized, cancellationToken).ConfigureAwait(false);
            Current = normalized;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "寫入設定檔失敗：{FilePath}", _settingsFilePath);
            throw;
        }
        finally
        {
            _ioLock.Release();
        }

        if (notifyChange)
        {
            SettingsChanged?.Invoke(this, Current);
        }
    }

    public Task SaveWindowPlacementAsync(
        WindowPlacement placement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placement);
        return MutateAndSaveAsync(updated => updated.WindowPlacement = placement, cancellationToken);
    }

    public Task SaveMiniOverlayPositionAsync(
        MiniOverlayPosition position,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        return MutateAndSaveAsync(updated => updated.MiniOverlayPosition = position, cancellationToken);
    }

    public Task SaveLibrarySortOrderAsync(
        LibrarySortOrder sortOrder,
        CancellationToken cancellationToken = default)
    {
        return MutateAndSaveAsync(updated => updated.LibrarySortOrder = sortOrder, cancellationToken);
    }

    public Task SaveLastPageAsync(
        NavigationPageKey page,
        CancellationToken cancellationToken = default)
    {
        return MutateAndSaveAsync(updated => updated.LastPage = page, cancellationToken);
    }

    public Task SaveAppearanceAsync(
        AppDisplayMode displayMode,
        AppColorTheme colorTheme,
        CancellationToken cancellationToken = default)
    {
        return MutateAndSaveAsync(
            updated =>
            {
                updated.DisplayMode = displayMode;
                updated.ColorTheme = colorTheme;
            },
            cancellationToken);
    }

    public Task SaveRecordingPreferencesAsync(
        ScenarioMode scenarioMode,
        QualityPreset qualityPreset,
        int autoStopMinutes,
        CancellationToken cancellationToken = default)
    {
        return MutateAndSaveAsync(
            updated =>
            {
                updated.DefaultScenarioMode = scenarioMode;
                updated.DefaultQualityPreset = qualityPreset;
                updated.DefaultAutoStopMinutes = autoStopMinutes;
            },
            cancellationToken);
    }

    public Task SaveLastEditorStateAsync(
        string? videoPath,
        long positionTicks,
        CancellationToken cancellationToken = default)
    {
        return MutateAndSaveAsync(
            updated =>
            {
                updated.LastEditorVideoPath = string.IsNullOrWhiteSpace(videoPath) ? null : videoPath.Trim();
                updated.LastEditorPositionTicks = Math.Max(0, positionTicks);
            },
            cancellationToken);
    }

    private async Task MutateAndSaveAsync(
        Action<AppSettings> mutate,
        CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppSettings updated = Current.Clone();
            mutate(updated);
            AppSettings normalized = Normalize(updated, _defaults);
            await WriteInternalAsync(normalized, cancellationToken).ConfigureAwait(false);
            Current = normalized;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "寫入設定檔失敗：{FilePath}", _settingsFilePath);
            throw;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task WriteInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = _settingsFilePath + ".tmp";
        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _settingsFilePath, overwrite: true);
    }

    private static string GetDefaultSettingsFilePath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "CourseRec", "settings.json");
    }

    private static AppSettings CreateDefaultSettings(CourseRecDefaultOptions defaults)
    {
        string videosFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrWhiteSpace(videosFolder))
        {
            videosFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Videos");
        }

        return new AppSettings
        {
            OutputDirectory = Path.Combine(videosFolder, "CourseRec"),
            DisplayMode = defaults.DefaultDisplayMode,
            ColorTheme = defaults.DefaultColorTheme,
            DefaultScenarioMode = defaults.DefaultScenarioMode,
            DefaultQualityPreset = defaults.DefaultQualityPreset,
            DefaultAutoStopMinutes = Math.Max(0, defaults.DefaultAutoStopMinutes),
            RecordingCountdownSeconds = NormalizeCountdownSeconds(defaults.DefaultRecordingCountdownSeconds),
            IsLivePreviewEnabledByDefault = defaults.DefaultIsLivePreviewEnabled,
            WindowPlacement = null,
            ExcludeOverlayFromCapture = defaults.DefaultExcludeOverlayFromCapture,
            IsolateWindowAudioByDefault = true,
            RecordingFileNamePrefix = FileNameHelper.DefaultPrefix,
            LastEditorVideoPath = null,
            LastEditorPositionTicks = 0,
            LibrarySortOrder = LibrarySortOrder.NewestFirst,
            LastPage = NavigationPageKey.Recorder,
            ToggleStartStopHotkey = defaults.ToggleStartStopHotkey,
            TogglePauseHotkey = defaults.TogglePauseHotkey,
            AddBookmarkHotkey = defaults.AddBookmarkHotkey,
            TakeSnapshotHotkey = defaults.TakeSnapshotHotkey,
        };
    }

    private static AppSettings Normalize(AppSettings input, CourseRecDefaultOptions defaults)
    {
        AppSettings fallback = CreateDefaultSettings(defaults);
        (AppDisplayMode displayMode, AppColorTheme colorTheme) = input.LegacyThemePreference is { } legacy
            ? MapLegacyTheme(legacy)
            : (input.DisplayMode, input.ColorTheme);

        if (colorTheme == AppColorTheme.EyeCareGreen)
        {
            colorTheme = AppColorTheme.EyeCareYellow;
        }
        else if (colorTheme == AppColorTheme.Sky)
        {
            colorTheme = AppColorTheme.Indigo;
        }

        return new AppSettings
        {
            OutputDirectory = string.IsNullOrWhiteSpace(input.OutputDirectory)
                ? fallback.OutputDirectory
                : input.OutputDirectory.Trim(),
            DisplayMode = displayMode,
            ColorTheme = colorTheme,
            DefaultScenarioMode = input.DefaultScenarioMode,
            DefaultQualityPreset = input.DefaultQualityPreset,
            DefaultAutoStopMinutes = Math.Clamp(input.DefaultAutoStopMinutes, 0, 720),
            RecordingCountdownSeconds = NormalizeCountdownSeconds(input.RecordingCountdownSeconds),
            IsLivePreviewEnabledByDefault = input.IsLivePreviewEnabledByDefault,
            WindowPlacement = NormalizeWindowPlacement(input.WindowPlacement),
            MiniOverlayPosition = NormalizeMiniOverlayPosition(input.MiniOverlayPosition),
            ExcludeOverlayFromCapture = input.ExcludeOverlayFromCapture,
            IsolateWindowAudioByDefault = input.IsolateWindowAudioByDefault,
            RecordingFileNamePrefix = FileNameHelper.SanitizePrefix(input.RecordingFileNamePrefix),
            LastEditorVideoPath = string.IsNullOrWhiteSpace(input.LastEditorVideoPath)
                ? null
                : input.LastEditorVideoPath.Trim(),
            LastEditorPositionTicks = Math.Max(0, input.LastEditorPositionTicks),
            LibrarySortOrder = Enum.IsDefined(input.LibrarySortOrder) ? input.LibrarySortOrder : default,
            LastPage = Enum.IsDefined(input.LastPage) ? input.LastPage : NavigationPageKey.Recorder,
            ToggleStartStopHotkey = string.IsNullOrWhiteSpace(input.ToggleStartStopHotkey)
                ? fallback.ToggleStartStopHotkey
                : input.ToggleStartStopHotkey.Trim(),
            TogglePauseHotkey = string.IsNullOrWhiteSpace(input.TogglePauseHotkey)
                ? fallback.TogglePauseHotkey
                : input.TogglePauseHotkey.Trim(),
            AddBookmarkHotkey = string.IsNullOrWhiteSpace(input.AddBookmarkHotkey)
                ? fallback.AddBookmarkHotkey
                : input.AddBookmarkHotkey.Trim(),
            TakeSnapshotHotkey = string.IsNullOrWhiteSpace(input.TakeSnapshotHotkey)
                ? fallback.TakeSnapshotHotkey
                : input.TakeSnapshotHotkey.Trim(),
        };
    }

    private static (AppDisplayMode DisplayMode, AppColorTheme ColorTheme) MapLegacyTheme(
        AppThemePreference legacy)
    {
        return legacy switch
        {
            AppThemePreference.Light => (AppDisplayMode.Light, AppColorTheme.Ocean),
            AppThemePreference.Dark => (AppDisplayMode.Dark, AppColorTheme.Ocean),
            AppThemePreference.OceanLight => (AppDisplayMode.Light, AppColorTheme.Ocean),
            AppThemePreference.OceanDark => (AppDisplayMode.Dark, AppColorTheme.Ocean),
            AppThemePreference.ForestLight => (AppDisplayMode.Light, AppColorTheme.Forest),
            AppThemePreference.ForestDark => (AppDisplayMode.Dark, AppColorTheme.Forest),
            AppThemePreference.SunsetLight => (AppDisplayMode.Light, AppColorTheme.Sunset),
            AppThemePreference.SunsetDark => (AppDisplayMode.Dark, AppColorTheme.Sunset),
            AppThemePreference.LavenderLight => (AppDisplayMode.Light, AppColorTheme.Lavender),
            AppThemePreference.LavenderDark => (AppDisplayMode.Dark, AppColorTheme.Lavender),
            AppThemePreference.EyeCareYellow => (AppDisplayMode.Light, AppColorTheme.EyeCareYellow),
            AppThemePreference.EyeCareGreen => (AppDisplayMode.Light, AppColorTheme.EyeCareYellow),
            _ => (AppDisplayMode.System, AppColorTheme.Ocean),
        };
    }

    private static int NormalizeCountdownSeconds(int value)
    {
        int clamped = Math.Clamp(value, SupportedCountdownSeconds[0], SupportedCountdownSeconds[^1]);
        return SupportedCountdownSeconds.MinBy(seconds => Math.Abs(seconds - clamped));
    }

    private static WindowPlacement? NormalizeWindowPlacement(WindowPlacement? placement)
    {
        if (placement is null)
        {
            return null;
        }

        return placement with
        {
            Width = Math.Clamp(placement.Width, 320, 16384),
            Height = Math.Clamp(placement.Height, 240, 16384),
        };
    }

    private static MiniOverlayPosition? NormalizeMiniOverlayPosition(MiniOverlayPosition? position)
    {
        if (position is null)
        {
            return null;
        }

        double widthDip = position.WidthDip > 0
            ? Math.Clamp(position.WidthDip, 160, 2000)
            : 0;
        double heightDip = position.HeightDip > 0
            ? Math.Clamp(position.HeightDip, 24, 300)
            : 0;

        return position with
        {
            WidthDip = widthDip,
            HeightDip = heightDip,
        };
    }
}
