using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempDir;

    public SettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CourseRecTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task LoadAndSaveAsync_RoundTripsSettingsAndNormalizesBounds()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var defaults = new CourseRecDefaultOptions
        {
            DefaultDisplayMode = AppDisplayMode.Dark,
            DefaultColorTheme = AppColorTheme.Forest,
            DefaultQualityPreset = QualityPreset.SlidePresentation,
        };

        var service = new SettingsService(settingsPath, defaults, NullLogger<SettingsService>.Instance);
        AppSettings initial = await service.LoadAsync();

        Assert.Equal(AppDisplayMode.Dark, initial.DisplayMode);
        Assert.Equal(AppColorTheme.Forest, initial.ColorTheme);
        Assert.Equal(5, initial.RecordingCountdownSeconds);
        Assert.False(initial.IsLivePreviewEnabledByDefault);
        Assert.True(File.Exists(settingsPath));

        var updated = new AppSettings
        {
            OutputDirectory = Path.Combine(_tempDir, "Videos"),
            DisplayMode = AppDisplayMode.Light,
            ColorTheme = AppColorTheme.Lavender,
            DefaultQualityPreset = QualityPreset.StandardSmooth,
            DefaultScenarioMode = ScenarioMode.InteractiveMeeting,
            DefaultAutoStopMinutes = 9999,
            RecordingCountdownSeconds = 99,
            IsLivePreviewEnabledByDefault = true,
            WindowPlacement = new WindowPlacement(120, 80, 1400, 900, true),
            MiniOverlayPosition = new MiniOverlayPosition(320, 240),
            ExcludeOverlayFromCapture = false,
            ToggleStartStopHotkey = "  Ctrl+Alt+F9  ",
            TogglePauseHotkey = string.Empty,
        };

        await service.SaveAsync(updated);

        var reloadedService = new SettingsService(settingsPath, defaults, NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();

        Assert.Equal(Path.Combine(_tempDir, "Videos"), reloaded.OutputDirectory);
        Assert.Equal(AppDisplayMode.Light, reloaded.DisplayMode);
        Assert.Equal(AppColorTheme.Lavender, reloaded.ColorTheme);
        Assert.Equal(QualityPreset.StandardSmooth, reloaded.DefaultQualityPreset);
        Assert.Equal(ScenarioMode.InteractiveMeeting, reloaded.DefaultScenarioMode);
        Assert.Equal(720, reloaded.DefaultAutoStopMinutes);
        Assert.Equal(30, reloaded.RecordingCountdownSeconds);
        Assert.True(reloaded.IsLivePreviewEnabledByDefault);
        Assert.Equal(new WindowPlacement(120, 80, 1400, 900, true), reloaded.WindowPlacement);
        Assert.Equal(new MiniOverlayPosition(320, 240), reloaded.MiniOverlayPosition);
        Assert.False(reloaded.ExcludeOverlayFromCapture);
        Assert.Equal("Ctrl+Alt+F9", reloaded.ToggleStartStopHotkey);
        Assert.Equal("Ctrl+Shift+P", reloaded.TogglePauseHotkey);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyThemePreference()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "outputDirectory": "",
              "themePreference": "OceanDark"
            }
            """);

        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings loaded = await service.LoadAsync();

        Assert.Equal(AppDisplayMode.Dark, loaded.DisplayMode);
        Assert.Equal(AppColorTheme.Ocean, loaded.ColorTheme);
        Assert.Null(loaded.LegacyThemePreference);

        await service.SaveAsync(loaded);
        string savedJson = await File.ReadAllTextAsync(settingsPath);
        Assert.DoesNotContain("themePreference", savedJson);
    }

    [Fact]
    public async Task SaveWindowPlacementAsync_PreservesOtherSettingsAndPersistsPlacement()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();
        await service.SaveAsync(new AppSettings
        {
            OutputDirectory = Path.Combine(_tempDir, "Videos"),
            RecordingCountdownSeconds = 15,
        });

        await service.SaveWindowPlacementAsync(new WindowPlacement(200, 150, 1200, 760, false));

        var reloadedService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();

        Assert.Equal(Path.Combine(_tempDir, "Videos"), reloaded.OutputDirectory);
        Assert.Equal(15, reloaded.RecordingCountdownSeconds);
        Assert.Equal(new WindowPlacement(200, 150, 1200, 760, false), reloaded.WindowPlacement);
    }

    [Fact]
    public async Task SaveMiniOverlayPositionAsync_PreservesOtherSettingsAndPersistsPosition()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();
        await service.SaveAsync(new AppSettings
        {
            OutputDirectory = Path.Combine(_tempDir, "Videos"),
            RecordingCountdownSeconds = 10,
        });

        await service.SaveMiniOverlayPositionAsync(new MiniOverlayPosition(480, 320));

        var reloadedService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();

        Assert.Equal(Path.Combine(_tempDir, "Videos"), reloaded.OutputDirectory);
        Assert.Equal(10, reloaded.RecordingCountdownSeconds);
        Assert.Equal(new MiniOverlayPosition(480, 320), reloaded.MiniOverlayPosition);
    }

    [Fact]
    public async Task SaveMiniOverlayPositionAsync_PersistsDipSize()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();

        await service.SaveMiniOverlayPositionAsync(new MiniOverlayPosition(640, 480, 264.5, 36.25));

        var reloadedService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();

        Assert.Equal(new MiniOverlayPosition(640, 480, 264.5, 36.25), reloaded.MiniOverlayPosition);
    }

    [Fact]
    public async Task SaveLibrarySortOrderAndLastPage_PreservesOtherSettingsAndPersists()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();
        await service.SaveAsync(new AppSettings
        {
            OutputDirectory = Path.Combine(_tempDir, "Videos"),
            RecordingCountdownSeconds = 10,
        });

        await service.SaveLibrarySortOrderAsync(LibrarySortOrder.SizeDescending);
        await service.SaveLastPageAsync(NavigationPageKey.About);

        var reloadedService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();

        Assert.Equal(Path.Combine(_tempDir, "Videos"), reloaded.OutputDirectory);
        Assert.Equal(10, reloaded.RecordingCountdownSeconds);
        Assert.Equal(LibrarySortOrder.SizeDescending, reloaded.LibrarySortOrder);
        Assert.Equal(NavigationPageKey.About, reloaded.LastPage);
    }

    [Fact]
    public async Task SaveWorkflowState_PersistsAppearancePreferencesAndLastEditorState()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();
        await service.SaveAsync(new AppSettings
        {
            OutputDirectory = Path.Combine(_tempDir, "Videos"),
            RecordingCountdownSeconds = 10,
        });

        string lastVideo = Path.Combine(_tempDir, "Course_Lecture.mp4");
        await service.SaveAppearanceAsync(AppDisplayMode.Dark, AppColorTheme.Lavender);
        await service.SaveRecordingPreferencesAsync(
            ScenarioMode.InteractiveMeeting,
            QualityPreset.StandardSmooth,
            15);
        await service.SaveLastEditorStateAsync(lastVideo, TimeSpan.FromMinutes(12).Ticks);

        var reloadedService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();

        Assert.Equal(Path.Combine(_tempDir, "Videos"), reloaded.OutputDirectory);
        Assert.Equal(10, reloaded.RecordingCountdownSeconds);
        Assert.Equal(AppDisplayMode.Dark, reloaded.DisplayMode);
        Assert.Equal(AppColorTheme.Lavender, reloaded.ColorTheme);
        Assert.Equal(ScenarioMode.InteractiveMeeting, reloaded.DefaultScenarioMode);
        Assert.Equal(QualityPreset.StandardSmooth, reloaded.DefaultQualityPreset);
        Assert.Equal(15, reloaded.DefaultAutoStopMinutes);
        Assert.Equal(lastVideo, reloaded.LastEditorVideoPath);
        Assert.Equal(TimeSpan.FromMinutes(12).Ticks, reloaded.LastEditorPositionTicks);
    }

    [Theory]
    [InlineData("Course", "Course")]
    [InlineData("  統計學  ", "統計學")]
    [InlineData("a/b:c*?", "a_b_c__")]
    [InlineData("   ", "Course")]
    [InlineData("結尾點...", "結尾點")]
    public void FileNameHelper_SanitizesRecordingPrefix(string input, string expected)
    {
        Assert.Equal(expected, FileNameHelper.SanitizePrefix(input));
    }

    [Fact]
    public void LocalizationService_ResolvesTraditionalChineseStrings()
    {
        var localization = new LocalizationService();
        string title = localization.GetString("AppDisplayName");
        Assert.Contains("CourseRec", title);
    }

    [Fact]
    public async Task SaveAsync_KeepsThreeSecondRecordingCountdown()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();

        await service.SaveAsync(new AppSettings { RecordingCountdownSeconds = 3 });

        Assert.Equal(3, service.Current.RecordingCountdownSeconds);

        var reloadedService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings reloaded = await reloadedService.LoadAsync();
        Assert.Equal(3, reloaded.RecordingCountdownSeconds);
    }

    [Fact]
    public async Task SaveAsync_SnapsRecordingCountdownToSupportedOptions()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();

        await service.SaveAsync(new AppSettings { RecordingCountdownSeconds = 7 });

        Assert.Equal(5, service.Current.RecordingCountdownSeconds);
    }

    [Fact]
    public async Task LoadAsync_MigratesRemovedEyeCareGreenThemeToEyeCareYellow()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "outputDirectory": "",
              "colorTheme": "EyeCareGreen"
            }
            """);

        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings loaded = await service.LoadAsync();

        Assert.Equal(AppColorTheme.EyeCareYellow, loaded.ColorTheme);
    }

    [Fact]
    public async Task LoadAsync_MigratesRemovedSkyThemeToIndigo()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "outputDirectory": "",
              "colorTheme": "Sky"
            }
            """);

        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings loaded = await service.LoadAsync();

        Assert.Equal(AppColorTheme.Indigo, loaded.ColorTheme);
    }

    [Fact]
    public async Task LoadAsync_MigratesLegacyEyeCareGreenPreferenceToEyeCareYellow()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(
            settingsPath,
            """
            {
              "outputDirectory": "",
              "themePreference": "EyeCareGreen"
            }
            """);

        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        AppSettings loaded = await service.LoadAsync();

        Assert.Equal(AppDisplayMode.Light, loaded.DisplayMode);
        Assert.Equal(AppColorTheme.EyeCareYellow, loaded.ColorTheme);
    }

    [Fact]
    public async Task SaveAsync_ClampsNegativeRecordingCountdownToZero()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var service = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await service.LoadAsync();

        await service.SaveAsync(new AppSettings
        {
            RecordingCountdownSeconds = -5,
            IsLivePreviewEnabledByDefault = false,
        });

        Assert.Equal(0, service.Current.RecordingCountdownSeconds);
        Assert.False(service.Current.IsLivePreviewEnabledByDefault);
    }

    [Fact]
    public void RecordingProfile_Create_AlignsEvenDimensionsAndAppliesPresetCaps()
    {
        RecordingProfile qhdSlide = RecordingProfile.Create(QualityPreset.SlidePresentation, 2560, 1440);
        Assert.Equal(1920, qhdSlide.Width);
        Assert.Equal(1080, qhdSlide.Height);
        Assert.Equal(15, qhdSlide.FrameRate);

        RecordingProfile oddSlide = RecordingProfile.Create(QualityPreset.SlidePresentation, 2561, 1441);
        Assert.InRange(oddSlide.Width, 2, 1920);
        Assert.InRange(oddSlide.Height, 2, 1080);
        Assert.Equal(0, oddSlide.Width % 2);
        Assert.Equal(0, oddSlide.Height % 2);

        RecordingProfile smoothProfile = RecordingProfile.Create(QualityPreset.StandardSmooth, 1365, 767);
        Assert.Equal(1364, smoothProfile.Width);
        Assert.Equal(766, smoothProfile.Height);
        Assert.Equal(30, smoothProfile.FrameRate);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup lock in temp folder.
        }
    }
}
