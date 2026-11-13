using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Bookmarks;
using Com.VibeCode.CourseRec.Services.Editing;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class EditorAndLibraryTests : IDisposable
{
    private readonly string _tempDir;

    public EditorAndLibraryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CourseRecPhase4_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task EditorViewModel_TrimCutAndExportM4a_ExportsKeptSegmentsAndSavesRealignedBookmarks()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var settingsService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await settingsService.SaveAsync(new AppSettings { OutputDirectory = _tempDir });

        var bookmarkService = new BookmarkService(NullLogger<BookmarkService>.Instance);
        var fakeEditingService = new FakeMediaEditingService(TimeSpan.FromMinutes(60));
        var navCoordinator = new NavigationCoordinator();
        var localization = new LocalizationService();
        var mainVm = new MainViewModel(navCoordinator, localization);

        string sourceVideo = Path.Combine(_tempDir, "Course_Lecture01.mp4");
        await File.WriteAllBytesAsync(sourceVideo, [0x00, 0x01, 0x02, 0x03]);
        await bookmarkService.SaveForVideoAsync(
            sourceVideo,
            TimeSpan.FromMinutes(60),
            [
                new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(10).Ticks, "Intro Concept", DateTimeOffset.UtcNow),
                new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(25).Ticks, "Break Chat", DateTimeOffset.UtcNow),
                new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(45).Ticks, "Exam Summary", DateTimeOffset.UtcNow),
            ]);

        var editorVm = new EditorViewModel(
            fakeEditingService,
            bookmarkService,
            settingsService,
            navCoordinator,
            mainVm,
            localization,
            NullLogger<EditorViewModel>.Instance);

        await editorVm.LoadVideoAsync(sourceVideo);
        Assert.True(editorVm.HasLoadedVideo);
        Assert.Equal(3, editorVm.Bookmarks.Count);
        Assert.False(mainVm.HasUnsavedEditorChanges);

        TimeSpan fakePosition = TimeSpan.FromMinutes(5);
        editorVm.CurrentPlaybackPositionProvider = () => fakePosition;
        editorVm.SetTrimStart();

        fakePosition = TimeSpan.FromMinutes(55);
        editorVm.SetTrimEnd();

        fakePosition = TimeSpan.FromMinutes(20);
        editorVm.MarkCutStart();
        fakePosition = TimeSpan.FromMinutes(30);
        editorVm.AddCutSegment();

        editorVm.AudioGainPercent = 150.0;
        Assert.Contains("150", editorVm.AudioGainText);
        editorVm.ExportProgressPercent = 42;
        Assert.Contains("42", editorVm.ExportProgressText);
        Assert.True(mainVm.HasUnsavedEditorChanges);

        await editorVm.ExportM4aCommand.ExecuteAsync(null);

        Assert.False(mainVm.HasUnsavedEditorChanges);
        Assert.Equal(ExportFormat.M4aAudioOnly, fakeEditingService.LastFormat);
        Assert.Equal(1.5, fakeEditingService.LastGainMultiplier, 3);
        Assert.Equal(2, fakeEditingService.LastKeptSegments.Count);
        Assert.Equal(TimeSpan.FromMinutes(40), TimeSpan.FromTicks(fakeEditingService.LastKeptSegments.Sum(s => s.Duration.Ticks)));

        Assert.NotNull(fakeEditingService.LastDestinationPath);
        BookmarkDocument exportedDoc = await bookmarkService.LoadForVideoAsync(fakeEditingService.LastDestinationPath!);
        Assert.Equal(2, exportedDoc.Bookmarks.Count);
        Assert.Equal(TimeSpan.FromMinutes(5), exportedDoc.Bookmarks[0].Timestamp);
        Assert.Equal(TimeSpan.FromMinutes(30), exportedDoc.Bookmarks[1].Timestamp);

        Assert.Equal(fakeEditingService.LastDestinationPath, editorVm.CompletedExportPath);
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success, editorVm.BannerSeverity);
        fakePosition = TimeSpan.FromMinutes(12);
        editorVm.MarkCutStart();
        Assert.True(editorVm.HasEditsToReset);
        editorVm.ResetEditsConfirmed();
        Assert.Equal(TimeSpan.Zero, editorVm.TrimStart);
        Assert.Equal(TimeSpan.FromMinutes(60), editorVm.TrimEnd);
        Assert.Empty(editorVm.CutSegments);
        Assert.False(editorVm.HasPendingCutStart);
        Assert.Equal(100.0, editorVm.AudioGainPercent);
        Assert.False(editorVm.HasEditsToReset);
        Assert.False(mainVm.HasUnsavedEditorChanges);
        Assert.Equal(3, editorVm.Bookmarks.Count);
        Assert.Equal(3, (await bookmarkService.LoadForVideoAsync(sourceVideo)).Bookmarks.Count);
        Assert.True(File.Exists(editorVm.CompletedExportPath));
    }

    [Fact]
    public async Task LibraryViewModel_RefreshAndDelete_ManagesMp4AndSidecarBookmarks()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var settingsService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await settingsService.SaveAsync(new AppSettings { OutputDirectory = _tempDir });

        var bookmarkService = new BookmarkService(NullLogger<BookmarkService>.Instance);
        var fakeEditingService = new FakeMediaEditingService(TimeSpan.FromMinutes(45));
        var navCoordinator = new NavigationCoordinator();
        var localization = new LocalizationService();

        string sampleMp4 = Path.Combine(_tempDir, "Course_20261104_090000.mp4");
        await File.WriteAllBytesAsync(sampleMp4, new byte[1024]);
        await bookmarkService.SaveForVideoAsync(
            sampleMp4,
            TimeSpan.FromMinutes(45),
            [new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(12).Ticks, "Key Slide", DateTimeOffset.UtcNow)]);

        var libraryVm = new LibraryViewModel(
            settingsService,
            bookmarkService,
            fakeEditingService,
            navCoordinator,
            localization,
            NullLogger<LibraryViewModel>.Instance);

        await libraryVm.RefreshLibraryAsync();
        Assert.Single(libraryVm.Recordings);
        Assert.Equal(1, libraryVm.Recordings[0].BookmarkCount);
        Assert.Equal(1024, libraryVm.TotalSizeBytes);
        Assert.Equal(
            localization.Format("LibraryTotalSizeFormat", FormatHelper.FormatBytes(1024)),
            libraryVm.TotalSizeText);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, libraryVm.TotalSizeVisibility);
        Assert.Equal(TimeSpan.FromMinutes(45), libraryVm.TotalDuration);
        Assert.Equal(
            localization.Format("LibraryTotalDurationFormat", FormatHelper.FormatDuration(TimeSpan.FromMinutes(45))),
            libraryVm.TotalDurationText);

        libraryVm.SearchText = "Key Slide";
        Assert.Single(libraryVm.FilteredRecordings);

        libraryVm.SearchText = "course_20261104";
        Assert.Single(libraryVm.FilteredRecordings);
        libraryVm.SearchText = "不存在的課程";
        Assert.Empty(libraryVm.FilteredRecordings);
        Assert.Single(libraryVm.Recordings);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, libraryVm.NoSearchResultsVisibility);
        await libraryVm.RefreshLibraryAsync();
        Assert.Empty(libraryVm.FilteredRecordings);
        libraryVm.SearchText = "  ";
        Assert.Single(libraryVm.FilteredRecordings);

        string? navigatedPath = null;
        navCoordinator.OpenVideoInEditorRequested += (_, p) => navigatedPath = p;
        libraryVm.OpenInEditor(libraryVm.Recordings[0]);
        Assert.Equal(sampleMp4, navigatedPath);

        await libraryVm.DeleteRecordingConfirmedAsync(libraryVm.Recordings[0]);
        Assert.Empty(libraryVm.Recordings);
        Assert.Empty(libraryVm.FilteredRecordings);
        Assert.Equal(0, libraryVm.TotalSizeBytes);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, libraryVm.TotalSizeVisibility);
        Assert.False(File.Exists(sampleMp4));
        Assert.False(File.Exists(bookmarkService.GetBookmarkFilePath(sampleMp4)));
    }

    [Fact]
    public async Task LibraryViewModel_RefreshTwice_ReusesUnchangedScanResults()
    {
        string settingsPath = Path.Combine(_tempDir, "settings_cache.json");
        var settingsService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await settingsService.SaveAsync(new AppSettings { OutputDirectory = _tempDir });

        var bookmarkService = new BookmarkService(NullLogger<BookmarkService>.Instance);
        var fakeEditingService = new FakeMediaEditingService(TimeSpan.FromMinutes(10));
        var libraryVm = new LibraryViewModel(
            settingsService,
            bookmarkService,
            fakeEditingService,
            new NavigationCoordinator(),
            new LocalizationService(),
            NullLogger<LibraryViewModel>.Instance);

        string sampleMp4 = Path.Combine(_tempDir, "CacheCourse.mp4");
        await File.WriteAllBytesAsync(sampleMp4, new byte[256]);

        await libraryVm.RefreshLibraryAsync();
        Assert.Equal(1, fakeEditingService.DurationProbeCount);
        Assert.Single(libraryVm.Recordings);

        await libraryVm.RefreshLibraryAsync();
        Assert.Equal(1, fakeEditingService.DurationProbeCount);

        await File.WriteAllBytesAsync(sampleMp4, new byte[512]);
        File.SetLastWriteTimeUtc(sampleMp4, DateTime.UtcNow.AddMinutes(1));
        await libraryVm.RefreshLibraryAsync();

        Assert.Equal(2, fakeEditingService.DurationProbeCount);
        Assert.Equal(512, Assert.Single(libraryVm.Recordings).FileSizeBytes);
    }

    [Fact]
    public async Task LibraryViewModel_Rename_RenamesMp4AndSidecarBookmarks()
    {
        string settingsPath = Path.Combine(_tempDir, "settings.json");
        var settingsService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await settingsService.SaveAsync(new AppSettings { OutputDirectory = _tempDir });

        var bookmarkService = new BookmarkService(NullLogger<BookmarkService>.Instance);
        var fakeEditingService = new FakeMediaEditingService(TimeSpan.FromMinutes(20));
        var navCoordinator = new NavigationCoordinator();
        var localization = new LocalizationService();

        string originalMp4 = Path.Combine(_tempDir, "Course_Original.mp4");
        await File.WriteAllBytesAsync(originalMp4, new byte[512]);
        await bookmarkService.SaveForVideoAsync(
            originalMp4,
            TimeSpan.FromMinutes(20),
            [new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(5).Ticks, "Important", DateTimeOffset.UtcNow)]);

        var libraryVm = new LibraryViewModel(
            settingsService,
            bookmarkService,
            fakeEditingService,
            navCoordinator,
            localization,
            NullLogger<LibraryViewModel>.Instance);

        await libraryVm.RefreshLibraryAsync();
        libraryVm.SearchText = "original";
        Assert.Single(libraryVm.FilteredRecordings);
        string? error = await libraryVm.RenameRecordingAsync(libraryVm.Recordings[0], "Course_Renamed");
        Assert.Null(error);

        string renamedMp4 = Path.Combine(_tempDir, "Course_Renamed.mp4");
        Assert.True(File.Exists(renamedMp4));
        Assert.False(File.Exists(originalMp4));
        Assert.True(File.Exists(bookmarkService.GetBookmarkFilePath(renamedMp4)));
        Assert.False(File.Exists(bookmarkService.GetBookmarkFilePath(originalMp4)));
        Assert.Single(libraryVm.Recordings);
        Assert.Equal("Course_Renamed.mp4", libraryVm.Recordings[0].FileName);
        Assert.Empty(libraryVm.FilteredRecordings);
        libraryVm.SearchText = "RENAMED";
        Assert.Single(libraryVm.FilteredRecordings);
    }

    [Fact]
    public async Task EditorViewModel_EditAndDeleteBookmark_PersistsWithoutChangingTimelineOrTrimEdits()
    {
        (EditorViewModel editor, BookmarkService service, string video, BookmarkItem original) = await CreateBookmarkEditorAsync();
        editor.AudioGainPercent = 150;

        Assert.False(await editor.UpdateBookmarkNoteAsync(original, "  "));
        Assert.Equal(original, Assert.Single(editor.Bookmarks));

        Assert.True(await editor.UpdateBookmarkNoteAsync(original, "  修正後的重點  "));
        BookmarkItem updated = Assert.Single((await service.LoadForVideoAsync(video)).Bookmarks);
        Assert.Equal(original with { Note = "修正後的重點" }, updated);
        Assert.Equal(updated, Assert.Single(editor.Bookmarks));
        Assert.True(editor.HasUnsavedChanges);
        Assert.Equal(150, editor.AudioGainPercent);

        editor.IsExporting = true;
        Assert.False(await editor.DeleteBookmarkConfirmedAsync(updated));
        Assert.False(await editor.UpdateBookmarkNoteAsync(updated, "匯出中不可修改"));
        editor.IsExporting = false;
        Assert.Single(editor.Bookmarks);

        Assert.True(await editor.DeleteBookmarkConfirmedAsync(updated));
        Assert.Empty(editor.Bookmarks);
        BookmarkDocument saved = await service.LoadForVideoAsync(video);
        Assert.Empty(saved.Bookmarks);
        Assert.Equal(TimeSpan.FromMinutes(30).Ticks, saved.TotalDurationTicks);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, editor.BookmarksEmptyVisibility);
        Assert.True(editor.HasUnsavedChanges);
        Assert.True(File.Exists(video));
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("add")]
    public async Task EditorViewModel_BookmarkSaveFailure_PreservesMemoryAndExistingSidecar(string operation)
    {
        (EditorViewModel editor, BookmarkService service, string video, BookmarkItem original) = await CreateBookmarkEditorAsync();
        // A directory at the temporary file path forces the real persistence operation to fail.
        Directory.CreateDirectory(service.GetBookmarkFilePath(video) + ".tmp");

        if (operation == "edit")
        {
            Assert.False(await editor.UpdateBookmarkNoteAsync(original, "不應儲存"));
        }
        else if (operation == "delete")
        {
            Assert.False(await editor.DeleteBookmarkConfirmedAsync(original));
        }
        else
        {
            editor.NewBookmarkNote = "新增備註";
            await editor.AddBookmarkAtCurrentPositionAsync();
            Assert.Equal("新增備註", editor.NewBookmarkNote);
        }

        Assert.Equal(original, Assert.Single(editor.Bookmarks));
        Assert.Equal(original, Assert.Single((await service.LoadForVideoAsync(video)).Bookmarks));
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error, editor.BannerSeverity);
        Assert.False(editor.IsSavingBookmarks);
        Assert.True(editor.CanInteract);
    }

    [Fact]
    public async Task LibraryViewModel_RenameAndDeleteLoadedVideo_ReleasesAndSyncsEditorState()
    {
        var settings = new SettingsService(Path.Combine(_tempDir, "settings_sync.json"), new CourseRecDefaultOptions(), NullLogger<SettingsService>.Instance);
        await settings.SaveAsync(new AppSettings { OutputDirectory = _tempDir });
        var bookmarkService = new BookmarkService(NullLogger<BookmarkService>.Instance);
        var fakeEditing = new FakeMediaEditingService(TimeSpan.FromMinutes(25));
        var nav = new NavigationCoordinator();
        var localization = new LocalizationService();
        var mainVm = new MainViewModel(nav, localization);

        string sourceMp4 = Path.Combine(_tempDir, "SyncCourse.mp4");
        await File.WriteAllBytesAsync(sourceMp4, [1, 2, 3, 4]);

        var editorVm = new EditorViewModel(fakeEditing, bookmarkService, settings, nav, mainVm, localization, NullLogger<EditorViewModel>.Instance);
        var libraryVm = new LibraryViewModel(settings, bookmarkService, fakeEditing, nav, localization, NullLogger<LibraryViewModel>.Instance);

        int releaseCount = 0;
        editorVm.VideoSourceReleased += (_, _) => releaseCount++;

        await editorVm.LoadVideoAsync(sourceMp4);
        await libraryVm.RefreshLibraryAsync();
        Assert.True(editorVm.HasLoadedVideo);

        string? err = await libraryVm.RenameRecordingAsync(libraryVm.Recordings[0], "SyncCourse_New");
        Assert.Null(err);
        Assert.Equal(1, releaseCount);
        Assert.Equal(Path.Combine(_tempDir, "SyncCourse_New.mp4"), editorVm.LoadedVideoPath);
        Assert.Equal("SyncCourse_New.mp4", editorVm.LoadedVideoTitle);

        await libraryVm.DeleteRecordingConfirmedAsync(libraryVm.Recordings[0]);
        Assert.Equal(2, releaseCount);
        Assert.False(editorVm.HasLoadedVideo);
        Assert.Equal(string.Empty, editorVm.LoadedVideoPath);
    }

    [Fact]
    public void MediaEditingService_SelectTrimmingPreferenceAndM4aProfile_UsesFastCopyAndMonoAac()
    {
        Assert.Equal(
            Windows.Media.Editing.MediaTrimmingPreference.Fast,
            MediaEditingService.SelectTrimmingPreference(ExportFormat.Mp4Video, 1.0));
        Assert.Equal(
            Windows.Media.Editing.MediaTrimmingPreference.Precise,
            MediaEditingService.SelectTrimmingPreference(ExportFormat.Mp4Video, 1.25));
        Assert.Equal(
            Windows.Media.Editing.MediaTrimmingPreference.Precise,
            MediaEditingService.SelectTrimmingPreference(ExportFormat.M4aAudioOnly, 1.0));

        Windows.Media.MediaProperties.MediaEncodingProfile m4a = MediaEditingService.CreateM4aTranscriptProfile();
        Assert.Null(m4a.Video);
        Assert.NotNull(m4a.Audio);
        Assert.Equal(1u, m4a.Audio.ChannelCount);
        Assert.Equal(48_000u, m4a.Audio.SampleRate);
        Assert.Equal(96_000u, m4a.Audio.Bitrate);
    }

    [Fact]
    public async Task LibraryViewModel_SortOptions_ReorderFilteredRecordings()
    {
        string settingsPath = Path.Combine(_tempDir, "settings_sort.json");
        var settingsService = new SettingsService(
            settingsPath,
            new CourseRecDefaultOptions(),
            NullLogger<SettingsService>.Instance);
        await settingsService.SaveAsync(new AppSettings { OutputDirectory = _tempDir });

        var libraryVm = new LibraryViewModel(
            settingsService,
            new BookmarkService(NullLogger<BookmarkService>.Instance),
            new FakeMediaEditingService(TimeSpan.FromMinutes(10)),
            new NavigationCoordinator(),
            new LocalizationService(),
            NullLogger<LibraryViewModel>.Instance);

        string alpha = Path.Combine(_tempDir, "Alpha.mp4");
        string bravo = Path.Combine(_tempDir, "Bravo.mp4");
        string charlie = Path.Combine(_tempDir, "Charlie.mp4");
        await File.WriteAllBytesAsync(alpha, new byte[100]);
        await File.WriteAllBytesAsync(bravo, new byte[300]);
        await File.WriteAllBytesAsync(charlie, new byte[200]);
        File.SetLastWriteTimeUtc(alpha, new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(bravo, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(charlie, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));

        await libraryVm.RefreshLibraryAsync();
        Assert.Equal(
            ["Alpha.mp4", "Charlie.mp4", "Bravo.mp4"],
            libraryVm.FilteredRecordings.Select(item => item.FileName));

        libraryVm.SelectedSortOption = libraryVm.SortOptions.First(option => option.Value == LibrarySortOrder.NameAscending);
        Assert.Equal(
            ["Alpha.mp4", "Bravo.mp4", "Charlie.mp4"],
            libraryVm.FilteredRecordings.Select(item => item.FileName));

        libraryVm.SelectedSortOption = libraryVm.SortOptions.First(option => option.Value == LibrarySortOrder.SizeDescending);
        Assert.Equal(
            ["Bravo.mp4", "Charlie.mp4", "Alpha.mp4"],
            libraryVm.FilteredRecordings.Select(item => item.FileName));

        libraryVm.SelectedSortOption = libraryVm.SortOptions.First(option => option.Value == LibrarySortOrder.OldestFirst);
        Assert.Equal(
            ["Bravo.mp4", "Charlie.mp4", "Alpha.mp4"],
            libraryVm.FilteredRecordings.Select(item => item.FileName));
    }

    [Fact]
    public async Task EditorViewModel_LoadAnotherVideo_AsksBeforeDiscardingUnsavedEdits()
    {
        (EditorViewModel editor, _, string originalVideo, _) = await CreateBookmarkEditorAsync();
        string secondVideo = Path.Combine(_tempDir, "SecondCourse.mp4");
        await File.WriteAllBytesAsync(secondVideo, [1, 2, 3, 4]);

        editor.AudioGainPercent = 150;
        Assert.True(editor.HasUnsavedChanges);

        int prompts = 0;
        editor.ConfirmDiscardEditsAsync = () =>
        {
            prompts++;
            return Task.FromResult(false);
        };

        await editor.LoadVideoAsync(secondVideo);
        Assert.Equal(1, prompts);
        Assert.Equal(originalVideo, editor.LoadedVideoPath);

        editor.ConfirmDiscardEditsAsync = () =>
        {
            prompts++;
            return Task.FromResult(true);
        };

        await editor.LoadVideoAsync(secondVideo);
        Assert.Equal(2, prompts);
        Assert.Equal(secondVideo, editor.LoadedVideoPath);
        Assert.False(editor.HasUnsavedChanges);
    }

    private async Task<(EditorViewModel Editor, BookmarkService Service, string Video, BookmarkItem Bookmark)> CreateBookmarkEditorAsync()
    {
        var settings = new SettingsService(Path.Combine(_tempDir, "settings.json"), new CourseRecDefaultOptions(), NullLogger<SettingsService>.Instance);
        await settings.SaveAsync(new AppSettings { OutputDirectory = _tempDir });
        var service = new BookmarkService(NullLogger<BookmarkService>.Instance);
        string video = Path.Combine(_tempDir, "BookmarkCourse.mp4");
        await File.WriteAllBytesAsync(video, [0, 1, 2, 3]);
        var bookmark = new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(5).Ticks, "原始備註", DateTimeOffset.UtcNow);
        await service.SaveForVideoAsync(video, TimeSpan.FromMinutes(30), [bookmark]);
        var navigation = new NavigationCoordinator();
        var localization = new LocalizationService();
        var editor = new EditorViewModel(
            new FakeMediaEditingService(TimeSpan.FromMinutes(30)), service, settings, navigation,
            new MainViewModel(navigation, localization), localization, NullLogger<EditorViewModel>.Instance);
        await editor.LoadVideoAsync(video);
        return (editor, service, video, bookmark);
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
            // Ignore temp cleanup lock.
        }
    }

    private sealed class FakeMediaEditingService : IMediaEditingService
    {
        private readonly TimeSpan _duration;

        public FakeMediaEditingService(TimeSpan duration)
        {
            _duration = duration;
        }

        public ExportFormat LastFormat { get; private set; }

        public int DurationProbeCount { get; private set; }

        public double LastGainMultiplier { get; private set; }

        public string? LastDestinationPath { get; private set; }

        public IReadOnlyList<TrimSegment> LastKeptSegments { get; private set; } = [];

        public Task<TimeSpan> GetVideoDurationAsync(string videoFilePath, CancellationToken cancellationToken = default)
        {
            DurationProbeCount++;
            return Task.FromResult(_duration);
        }

        public Task<string?> CaptureVideoFrameToPngAsync(
            string videoFilePath,
            TimeSpan position,
            string outputDirectory,
            bool copyToClipboard = true,
            CancellationToken cancellationToken = default)
        {
            string outFile = Path.Combine(outputDirectory, "Snapshots", "test.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
            File.WriteAllBytes(outFile, [0x89, 0x50, 0x4E, 0x47]);
            return Task.FromResult<string?>(outFile);
        }

        public async Task ExportEditedMediaAsync(
            string sourceVideoPath,
            string destinationFilePath,
            ExportFormat format,
            IReadOnlyList<TrimSegment> keptSegments,
            double audioGainMultiplier,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            LastFormat = format;
            LastGainMultiplier = MediaEditingService.ClampAudioGain(audioGainMultiplier);
            LastDestinationPath = destinationFilePath;
            LastKeptSegments = keptSegments.ToList();
            progress?.Report(50.0);
            await File.WriteAllBytesAsync(destinationFilePath, [0x00, 0x00, 0x00, 0x18], cancellationToken)
                .ConfigureAwait(false);
            progress?.Report(100.0);
        }
    }
}
