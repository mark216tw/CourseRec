using System.Reflection;
using System.Runtime.InteropServices;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Audio;
using Com.VibeCode.CourseRec.Services.Bookmarks;
using Com.VibeCode.CourseRec.Services.Capture;
using Com.VibeCode.CourseRec.Services.Encoding;
using Com.VibeCode.CourseRec.Services.Hotkeys;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class RecorderReliabilityTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "CourseRecReliability_" + Guid.NewGuid().ToString("N"));

    public RecorderReliabilityTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task EncodingFailure_AutomaticallyStopsAndNeverReportsSuccess()
    {
        using var encoder = new FaultingEncoder { FailWrites = true };
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recorder.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(recorder.State) && recorder.State == RecordingState.Idle)
            {
                stopped.TrySetResult();
            }
        };

        await recorder.StartRecordingCommand.ExecuteAsync(null);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(recorder.IsErrorBanner);
        Assert.Empty(recorder.CompletedRecordingPath);
        Assert.Equal(1, encoder.FinalizeCount);
        Assert.Contains("編碼錯誤", recorder.InfoBannerMessage);
        Assert.True(File.Exists(encoder.OutputFilePath));
    }

    [Fact]
    public async Task FinalizeFailure_PreservesBookmarksAndDoesNotOfferSuccessfulRecording()
    {
        using var encoder = new FaultingEncoder { FailFinalize = true };
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder);
        await recorder.StartRecordingCommand.ExecuteAsync(null);
        recorder.BookmarkNoteInput = "封裝失敗仍保留重點";
        recorder.AddBookmarkCommand.Execute(null);

        await recorder.StopRecordingAsync();

        Assert.Equal(RecordingState.Idle, recorder.State);
        Assert.True(recorder.IsErrorBanner);
        Assert.Empty(recorder.CompletedRecordingPath);
        var bookmarks = new BookmarkService(NullLogger<BookmarkService>.Instance);
        BookmarkDocument saved = await bookmarks.LoadForVideoAsync(encoder.OutputFilePath!);
        Assert.Equal("封裝失敗仍保留重點", Assert.Single(saved.Bookmarks).Note);
    }

    [Fact]
    public async Task BookmarkAutosave_RetriesFailureAndPersistsBeforeStopThenSavesFinalSnapshot()
    {
        using var encoder = new FaultingEncoder();
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder);
        var service = new BookmarkService(NullLogger<BookmarkService>.Instance);
        await recorder.StartRecordingCommand.ExecuteAsync(null);
        recorder.BookmarkNoteInput = "第一個重點";
        recorder.AddBookmarkCommand.Execute(null);
        string sidecar = service.GetBookmarkFilePath(encoder.OutputFilePath!);
        Directory.CreateDirectory(sidecar + ".tmp");

        await TriggerCheckpointAsync(recorder);
        Assert.False(recorder.IsErrorBanner);
        Assert.Equal(Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning, recorder.BannerSeverity);
        Assert.Equal(RecordingState.Recording, recorder.State);
        Assert.False(File.Exists(sidecar));

        Directory.Delete(sidecar + ".tmp");
        await TriggerCheckpointAsync(recorder);
        Assert.Equal("第一個重點", Assert.Single((await service.LoadForVideoAsync(encoder.OutputFilePath!)).Bookmarks).Note);
        Assert.Equal(RecordingState.Recording, recorder.State);

        recorder.BookmarkNoteInput = "最後一個重點";
        recorder.AddBookmarkCommand.Execute(null);
        await recorder.StopRecordingAsync();
        Assert.Equal(2, (await service.LoadForVideoAsync(encoder.OutputFilePath!)).Bookmarks.Count);
        Assert.Equal(encoder.OutputFilePath, recorder.CompletedRecordingPath);
        Assert.False(recorder.IsErrorBanner);
    }

    [Fact]
    public async Task StartRecording_CancelDuringSlowEncoderOpen_FinalizesEncoderOnce()
    {
        using var encoder = new FaultingEncoder { OpenDelayMs = 150 };
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder);

        Task startTask = recorder.StartRecordingCommand.ExecuteAsync(null);
        await Task.Delay(40);
        await recorder.StopRecordingAsync();
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RecordingState.Idle, recorder.State);
        Assert.Equal(1, encoder.FinalizeCount);
    }

    [Fact]
    public async Task BookmarkService_ConcurrentCheckpointsDoNotCollideAndFinalSnapshotWins()
    {
        var service = new BookmarkService(NullLogger<BookmarkService>.Instance);
        string video = Path.Combine(_tempDir, "concurrent.mp4");
        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => service.SaveForVideoAsync(video,
            TimeSpan.FromSeconds(index), [new BookmarkItem(Guid.NewGuid(), index, $"checkpoint-{index}", DateTimeOffset.UtcNow)])));
        await service.SaveForVideoAsync(video, TimeSpan.FromSeconds(60), []);
        BookmarkDocument final = await service.LoadForVideoAsync(video);
        Assert.Empty(final.Bookmarks);
        Assert.Equal(TimeSpan.FromSeconds(60).Ticks, final.TotalDurationTicks);
        Assert.False(File.Exists(service.GetBookmarkFilePath(video) + ".tmp"));
    }

    [Fact]
    public async Task RemoveBookmark_WhileRecording_SavesFinalSnapshotWithoutRemovedItem()
    {
        using var encoder = new FaultingEncoder();
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder);
        await recorder.StartRecordingCommand.ExecuteAsync(null);

        recorder.BookmarkNoteInput = "第一個重點";
        recorder.AddBookmarkCommand.Execute(null);
        recorder.BookmarkNoteInput = "保留的重點";
        recorder.AddBookmarkCommand.Execute(null);

        BookmarkItem removed = recorder.SessionBookmarks[0];
        recorder.RemoveBookmark(removed);
        Assert.Equal("保留的重點", Assert.Single(recorder.SessionBookmarks).Note);

        await recorder.StopRecordingAsync();

        var bookmarks = new BookmarkService(NullLogger<BookmarkService>.Instance);
        BookmarkDocument saved = await bookmarks.LoadForVideoAsync(encoder.OutputFilePath!);
        Assert.Equal("保留的重點", Assert.Single(saved.Bookmarks).Note);
    }

    [Fact]
    public async Task AutoStopSelection_ShowsRemainingTimeOnlyWhileRecording()
    {
        using var encoder = new FaultingEncoder();
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder);
        Assert.Contains(recorder.AutoStopOptions, option => option.Value == 5);
        Assert.Contains(recorder.AutoStopOptions, option => option.Value == 10);
        Assert.Contains(recorder.AutoStopOptions, option => option.Value == 15);
        Assert.Contains(recorder.AutoStopOptions, option => option.Value == 20);

        await recorder.StartRecordingCommand.ExecuteAsync(null);

        recorder.SelectedAutoStopOption = recorder.AutoStopOptions.First(option => option.Value == 30);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, recorder.AutoStopRemainingVisibility);
        Assert.Contains("自動停止", recorder.AutoStopRemainingText);

        recorder.SelectedAutoStopOption = recorder.AutoStopOptions.First(option => option.Value == 0);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Collapsed, recorder.AutoStopRemainingVisibility);
        Assert.Equal(string.Empty, recorder.AutoStopRemainingText);

        await recorder.StopRecordingAsync();
    }

    [Fact]
    public async Task CountdownOverlay_ShowsDescendingNumbersAndHidesBeforeRecordingStarts()
    {
        using var encoder = new FaultingEncoder();
        var overlay = new FakeCountdownOverlayService();
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder, countdownSeconds: 3, countdownOverlay: overlay);

        await recorder.StartRecordingCommand.ExecuteAsync(null);

        Assert.Equal([3, 2, 1], overlay.ShownSeconds);
        Assert.True(overlay.HideCount >= 1);
        Assert.Equal(RecordingState.Recording, recorder.State);

        await recorder.StopRecordingAsync();
    }

    [Fact]
    public async Task CountdownOverlay_ZeroCountdown_NeverShowsOverlay()
    {
        using var encoder = new FaultingEncoder();
        var overlay = new FakeCountdownOverlayService();
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder, countdownSeconds: 0, countdownOverlay: overlay);

        await recorder.StartRecordingCommand.ExecuteAsync(null);

        Assert.Empty(overlay.ShownSeconds);
        Assert.Equal(RecordingState.Recording, recorder.State);

        await recorder.StopRecordingAsync();
    }

    [Fact]
    public async Task CountdownOverlay_CancelDuringCountdown_HidesOverlayImmediately()
    {
        using var encoder = new FaultingEncoder();
        var overlay = new FakeCountdownOverlayService();
        using RecorderViewModel recorder = await CreateRecorderAsync(encoder, countdownSeconds: 5, countdownOverlay: overlay);

        Task startTask = recorder.StartRecordingCommand.ExecuteAsync(null);
        await Task.Delay(50);
        await recorder.StopRecordingAsync();
        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEmpty(overlay.ShownSeconds);
        Assert.Equal(5, overlay.ShownSeconds[0]);
        Assert.True(overlay.HideCount >= 1);
        Assert.Equal(RecordingState.Idle, recorder.State);
    }

    [Fact]
    public void CountdownOverlayBoundsHelper_CentersAndClampsWithinWorkArea()
    {
        (int left, int top) = CountdownOverlayBoundsHelper.CalculateCenteredPosition(
            targetX: 0,
            targetY: 0,
            targetWidth: 1920,
            targetHeight: 1040,
            overlayWidth: 264,
            overlayHeight: 264,
            workAreaX: 0,
            workAreaY: 0,
            workAreaWidth: 1920,
            workAreaHeight: 1040);
        Assert.Equal(828, left);
        Assert.Equal(388, top);

        // 目標視窗部分超出多螢幕左側負座標工作區時，應夾限在工作區內。
        (int clampedLeft, int clampedTop) = CountdownOverlayBoundsHelper.CalculateCenteredPosition(
            targetX: -2000,
            targetY: -100,
            targetWidth: 300,
            targetHeight: 300,
            overlayWidth: 264,
            overlayHeight: 264,
            workAreaX: -1920,
            workAreaY: 0,
            workAreaWidth: 1920,
            workAreaHeight: 1040);
        Assert.Equal(-1920, clampedLeft);
        Assert.Equal(0, clampedTop);
    }

    private async Task<RecorderViewModel> CreateRecorderAsync(
        FaultingEncoder encoder,
        int countdownSeconds = 0,
        ICountdownOverlayService? countdownOverlay = null)
    {
        var settings = new SettingsService(Path.Combine(_tempDir, "settings.json"), new CourseRecDefaultOptions(), NullLogger<SettingsService>.Instance);
        await settings.SaveAsync(new AppSettings { OutputDirectory = _tempDir, RecordingCountdownSeconds = countdownSeconds });
        var navigation = new NavigationCoordinator();
        var localization = new LocalizationService();
        return new RecorderViewModel(new CaptureEnumerationService(), new FakeScreenCapture(),
            new AudioCaptureService(NullLogger<AudioCaptureService>.Instance), encoder,
            new BookmarkService(NullLogger<BookmarkService>.Instance), settings,
            new GlobalHotkeyService(NullLogger<GlobalHotkeyService>.Instance),
            new MainViewModel(navigation, localization), localization, NullLogger<RecorderViewModel>.Instance, navigation,
            new TaskbarIndicatorService(localization, NullLogger<TaskbarIndicatorService>.Instance),
            countdownOverlay ?? new FakeCountdownOverlayService())
        {
            SelectedTarget = new CaptureTarget("test", CaptureTargetKind.Monitor, "test", "test", 0, 0, 320, 240),
        };
    }

    private static async Task TriggerCheckpointAsync(RecorderViewModel recorder)
    {
        // 直接觸發排程 tick，避免啟動 WinUI dispatcher 或等待真實週期。
        typeof(RecorderViewModel).GetMethod("OnHousekeepingTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(recorder, [null]);
        Task save = (Task)typeof(RecorderViewModel).GetField("_bookmarkSaveTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(recorder)!;
        await save.WaitAsync(TimeSpan.FromSeconds(10));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private sealed class FaultingEncoder : IMp4EncoderService
    {
        public bool FailWrites { get; init; }
        public bool FailFinalize { get; init; }
        public int OpenDelayMs { get; init; }
        public int FinalizeCount { get; private set; }
        public bool IsOpen { get; private set; }
        public RecordingProfile? ActiveProfile { get; private set; }
        public string? OutputFilePath { get; private set; }

        public void Open(string outputFilePath, RecordingProfile profile, int sourceWidth = 0, int sourceHeight = 0)
        {
            if (OpenDelayMs > 0)
            {
                Thread.Sleep(OpenDelayMs);
            }

            OutputFilePath = outputFilePath;
            ActiveProfile = profile;
            File.WriteAllBytes(outputFilePath, [0, 1, 2, 3]);
            IsOpen = true;
        }

        public void WriteVideoFrameBgra(ReadOnlySpan<byte> pixels, int width, int height, long time, long duration)
        {
            if (FailWrites)
            {
                throw new COMException("Injected device failure", unchecked((int)0x80004005));
            }
        }

        public void WriteAudioPcm16(ReadOnlySpan<short> samples, long time, long duration) { }

        public void FinalizeRecording()
        {
            FinalizeCount++;
            IsOpen = false;
            ActiveProfile = null;
            if (FailFinalize)
            {
                throw new COMException("Injected finalization failure", unchecked((int)0x80004005));
            }
        }

        public void Dispose() { }
    }

    private sealed class FakeScreenCapture : IScreenCaptureService
    {
        public CaptureTarget? ActiveTarget { get; private set; }
        public bool IsCapturing { get; private set; }
        public bool IsTargetMinimized => false;
        public long MinCaptureIntervalTicks { get; set; }
        public bool IsPreviewEnabled { get; set; }
        public int PreviewMaxDimension { get; set; } = 1280;
        public event EventHandler<CapturedFrameData>? PreviewFrameAvailable { add { } remove { } }
        public event EventHandler<bool>? TargetMinimizedChanged { add { } remove { } }
        public event EventHandler? TargetClosed { add { } remove { } }
        public bool StartCapture(CaptureTarget target) { ActiveTarget = target; IsCapturing = true; return true; }
        public void StopCapture() { ActiveTarget = null; IsCapturing = false; }
        public CapturedFrameData? GetLatestFrameSnapshot() => null;
        public Task<string?> SaveSnapshotPngAsync(string directory, bool copyToClipboard = true, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public void Dispose() { }
    }

    private sealed class FakeCountdownOverlayService : ICountdownOverlayService
    {
        public List<int> ShownSeconds { get; } = [];
        public CaptureTarget? LastTarget { get; private set; }
        public int HideCount { get; private set; }

        public void ShowOrUpdate(int remainingSeconds, CaptureTarget? target)
        {
            ShownSeconds.Add(remainingSeconds);
            LastTarget = target;
        }

        public void Hide()
        {
            HideCount++;
        }
    }
}
