using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services.Bookmarks;
using Com.VibeCode.CourseRec.Services.Editing;
using Com.VibeCode.CourseRec.Services.Encoding;
using Com.VibeCode.CourseRec.Services.Hotkeys;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class TimelineAndBookmarkTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ITestOutputHelper _output;

    public TimelineAndBookmarkTests(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), "CourseRecPhase3_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void RecordingTimelineClock_PauseAndResume_DeductsPausedIntervalsAccurately()
    {
        long fakeTicks = TimeSpan.FromSeconds(100).Ticks;
        var clock = new RecordingTimelineClock(() => fakeTicks);

        clock.Start();
        fakeTicks += TimeSpan.FromSeconds(12).Ticks;
        Assert.Equal(TimeSpan.FromSeconds(12), clock.ElapsedActiveTime);

        clock.Pause();
        fakeTicks += TimeSpan.FromSeconds(45).Ticks; // 45s break while paused
        Assert.Equal(TimeSpan.FromSeconds(12), clock.ElapsedActiveTime);

        clock.Resume();
        fakeTicks += TimeSpan.FromSeconds(8).Ticks;
        Assert.Equal(TimeSpan.FromSeconds(20), clock.ElapsedActiveTime);
    }

    [Fact]
    public void RecordingFrameTiming_AccumulatedTimestampsDoNotDrift()
    {
        const int frameRate = 30;
        long framesPerHour = frameRate * 60L * 60L;

        Assert.Equal(
            TimeSpan.FromHours(1).Ticks,
            RecordingFrameTiming.GetFrameStart100Ns(framesPerHour, frameRate));
        Assert.Equal(
            TimeSpan.FromHours(10).Ticks,
            RecordingFrameTiming.GetFrameStart100Ns(framesPerHour * 10, frameRate));
    }

    [Fact]
    public void RecordingFrameTiming_DurationsSumToNextFrameStart()
    {
        const int frameRate = 30;
        long accumulated = 0;
        for (long frameIndex = 0; frameIndex < 900; frameIndex++)
        {
            accumulated += RecordingFrameTiming.GetFrameDuration100Ns(frameIndex, frameRate);
        }

        Assert.Equal(RecordingFrameTiming.GetFrameStart100Ns(900, frameRate), accumulated);
    }

    [Fact]
    public void BgraScaler_ComputePreviewSize_PreservesAspectAndCapsLongestEdge()
    {
        Assert.Equal((1280, 720), BgraScaler.ComputePreviewSize(3840, 2160, 1280));
        Assert.Equal((720, 1280), BgraScaler.ComputePreviewSize(2160, 3840, 1280));
        Assert.Equal((800, 600), BgraScaler.ComputePreviewSize(800, 600, 1280));
        Assert.Equal((0, 0), BgraScaler.ComputePreviewSize(0, 600, 1280));
    }

    [Fact]
    public void D3D11NativeMethods_DetectsDeviceLostHresults()
    {
        Assert.True(D3D11NativeMethods.IsDeviceLostHresult(D3D11NativeMethods.DxgiErrorDeviceRemoved));
        Assert.True(D3D11NativeMethods.IsDeviceLostHresult(D3D11NativeMethods.DxgiErrorDeviceReset));
        Assert.False(D3D11NativeMethods.IsDeviceLostHresult(unchecked((int)0x80004005)));
    }

    [Fact]
    public void BookmarkService_RecalculateBookmarksAfterEdit_MergesOverlappingCutsAndShiftsTimestamps()
    {
        var service = new BookmarkService(NullLogger<BookmarkService>.Instance);
        TimeSpan totalDuration = TimeSpan.FromMinutes(60);
        TimeSpan trimStart = TimeSpan.FromMinutes(5);
        TimeSpan trimEnd = TimeSpan.FromMinutes(55);

        // Two overlapping cut intervals: [20m, 26m] and [24m, 30m] -> merged [20m, 30m] (10m removed)
        IReadOnlyList<TrimSegment> cuts =
        [
            new TrimSegment(TimeSpan.FromMinutes(24).Ticks, TimeSpan.FromMinutes(30).Ticks, "Break B"),
            new TrimSegment(TimeSpan.FromMinutes(20).Ticks, TimeSpan.FromMinutes(26).Ticks, "Break A"),
        ];

        IReadOnlyList<BookmarkItem> originals =
        [
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(2).Ticks, "Pre-class waiting", DateTimeOffset.UtcNow),
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(10).Ticks, "Chapter 1", DateTimeOffset.UtcNow),
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(25).Ticks, "Inside break", DateTimeOffset.UtcNow),
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(40).Ticks, "Chapter 2", DateTimeOffset.UtcNow),
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(58).Ticks, "Post-class Q&A", DateTimeOffset.UtcNow),
        ];

        IReadOnlyList<BookmarkItem> remapped = service.RecalculateBookmarksAfterEdit(
            originals,
            totalDuration,
            trimStart,
            trimEnd,
            cuts);

        Assert.Equal(2, remapped.Count);
        Assert.Equal("Chapter 1", remapped[0].Note);
        Assert.Equal(TimeSpan.FromMinutes(5), remapped[0].Timestamp); // 10m - 5m trimStart = 5m

        Assert.Equal("Chapter 2", remapped[1].Note);
        Assert.Equal(TimeSpan.FromMinutes(25), remapped[1].Timestamp); // 40m - 5m trimStart - 10m break = 25m
    }

    [Fact]
    public async Task BookmarkService_SaveAndLoadSidecarJson_PreservesSortedBookmarks()
    {
        var service = new BookmarkService(NullLogger<BookmarkService>.Instance);
        string videoPath = Path.Combine(_tempDir, "Course_20261104.mp4");

        IReadOnlyList<BookmarkItem> items =
        [
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(15).Ticks, "Second", DateTimeOffset.UtcNow),
            new BookmarkItem(Guid.NewGuid(), TimeSpan.FromMinutes(3).Ticks, "First", DateTimeOffset.UtcNow),
        ];

        await service.SaveForVideoAsync(videoPath, TimeSpan.FromMinutes(30), items);
        BookmarkDocument doc = await service.LoadForVideoAsync(videoPath);

        Assert.Equal(2, doc.Bookmarks.Count);
        Assert.Equal("First", doc.Bookmarks[0].Note);
        Assert.Equal("Second", doc.Bookmarks[1].Note);
    }

    [Fact]
    public void GlobalHotkeyService_TryParseHotkeyGesture_ParsesModifiersAndKeys()
    {
        bool ok = GlobalHotkeyService.TryParseHotkeyGesture("Ctrl+Shift+R", out uint mods, out uint vk);
        Assert.True(ok);
        Assert.Equal(
            HotkeyNativeMethods.ModControl | HotkeyNativeMethods.ModShift | HotkeyNativeMethods.ModNoRepeat,
            mods);
        Assert.Equal((uint)'R', vk);

        bool okFn = GlobalHotkeyService.TryParseHotkeyGesture("Alt+F10", out uint fnMods, out uint fnVk);
        Assert.True(okFn);
        Assert.Equal(HotkeyNativeMethods.ModAlt | HotkeyNativeMethods.ModNoRepeat, fnMods);
        Assert.Equal(0x79u, fnVk);

        Assert.False(GlobalHotkeyService.TryParseHotkeyGesture("R", out _, out _));
        Assert.False(GlobalHotkeyService.TryParseHotkeyGesture(string.Empty, out _, out _));
    }

    [Fact]
    public void Mp4EncoderService_ScaleBgraIntoBuffer_PreservesLetterboxBounds()
    {
        byte[] src = new byte[4 * 4 * 4];
        Array.Fill(src, (byte)255);
        byte[] dst = new byte[8 * 4 * 4]; // 8x4 target from 4x4 square source -> centered 4x4 inside 8x4

        Mp4EncoderService.ScaleBgraIntoBuffer(src, 4, 4, dst, 8, 4);

        // Left letterbox bar (x=0, y=2) is black (0), center (x=4, y=2) is white (255)
        Assert.Equal(0, dst[(2 * 8 + 0) * 4]);
        Assert.Equal(255, dst[(2 * 8 + 4) * 4]);
    }

    [Fact]
    public void Mp4EncoderService_ScaleBgraIntoBuffer_BilinearInterpolatesHorizontalGradient()
    {
        // 2x2 source: left column = 0, right column = 255
        byte[] src =
        [
            0, 0, 0, 255, 255, 255, 255, 255,
            0, 0, 0, 255, 255, 255, 255, 255,
        ];
        byte[] dst = new byte[3 * 3 * 4];

        Mp4EncoderService.ScaleBgraIntoBuffer(src, 2, 2, dst, 3, 3);

        // Center column (x=1, y=1) should be bilinearly interpolated (~128), not hard-stepped 0 or 255
        Assert.InRange(dst[((1 * 3) + 1) * 4], (byte)126, (byte)129);
    }

    [Fact]
    public async Task Mp4EncoderAndMediaEditing_EndToEndHardwarePipeline_EncodesAndFastExportsMp4AndMonoM4a()
    {
        string rawMp4 = Path.Combine(_tempDir, "NativeRecorded.mp4");
        string trimmedMp4 = Path.Combine(_tempDir, "TrimmedFast.mp4");
        string monoM4a = Path.Combine(_tempDir, "TranscriptMono.m4a");

        RecordingProfile profile = RecordingProfile.Create(QualityPreset.SlidePresentation, 320, 240);
        byte[] srcFrame640x480 = new byte[640 * 480 * 4];
        Array.Fill(srcFrame640x480, (byte)180);
        short[] audioStep = new short[(48_000 / profile.FrameRate) * 2];

        using (var encoder = new Mp4EncoderService(NullLogger<Mp4EncoderService>.Instance))
        {
            _output.WriteLine("Starting native encoder");
            encoder.Open(rawMp4, profile, sourceWidth: 640, sourceHeight: 480);
            long frameDur100Ns = TimeSpan.TicksPerSecond / profile.FrameRate;
            long audioDur100Ns = (3200L * TimeSpan.TicksPerSecond) / 48_000L;

            for (int i = 0; i < profile.FrameRate * 2; i++)
            {
                encoder.WriteVideoFrameBgra(srcFrame640x480, 640, 480, i * frameDur100Ns, frameDur100Ns);
                encoder.WriteAudioPcm16(audioStep, i * audioDur100Ns, audioDur100Ns);
            }

            encoder.FinalizeRecording();
            _output.WriteLine("Native encoding completed");
        }

        Assert.True(File.Exists(rawMp4));
        Assert.True(new FileInfo(rawMp4).Length > 0);

        var editingService = new MediaEditingService(NullLogger<MediaEditingService>.Instance);
        TimeSpan resolvedDuration = await editingService.GetVideoDurationAsync(rawMp4);
        Assert.True(resolvedDuration > TimeSpan.FromSeconds(1));

        IReadOnlyList<TrimSegment> segment = [new TrimSegment(0L, TimeSpan.FromSeconds(1).Ticks, "Keep1s")];
        await File.WriteAllBytesAsync(trimmedMp4, [10, 20, 30, 40]);
        await editingService.ExportEditedMediaAsync(rawMp4, trimmedMp4, ExportFormat.Mp4Video, segment, audioGainMultiplier: 1.0);
        _output.WriteLine("MP4 export completed");
        await editingService.ExportEditedMediaAsync(rawMp4, monoM4a, ExportFormat.M4aAudioOnly, segment, audioGainMultiplier: 1.0);
        _output.WriteLine("M4A export completed");

        Assert.True(File.Exists(trimmedMp4) && new FileInfo(trimmedMp4).Length > 0);
        Assert.True(File.Exists(monoM4a) && new FileInfo(monoM4a).Length > 0);

        // 取消發生於真正開始渲染後；既有目的檔仍必須完整保留。
        string cancelledMp4 = Path.Combine(_tempDir, "Cancelled.mp4");
        byte[] priorOutput = [10, 20, 30, 40];
        await File.WriteAllBytesAsync(cancelledMp4, priorOutput);
        using var cancellation = new CancellationTokenSource();
        int progressCalls = 0;
        bool priorOutputPreservedDuringRender = false;
        var cancelOnProgress = new InlineProgress(percent =>
        {
            if (Interlocked.Increment(ref progressCalls) == 1)
            {
                priorOutputPreservedDuringRender = File.ReadAllBytes(cancelledMp4).SequenceEqual(priorOutput);
                // WinRT 進度回呼持有原生內部鎖；取消回呼需在別的執行緒執行，避免重入 Cancel。
                _ = cancellation.CancelAsync();
            }
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => editingService.ExportEditedMediaAsync(
            rawMp4, cancelledMp4, ExportFormat.Mp4Video, segment, 1.0, cancelOnProgress, cancellation.Token));
        Assert.True(priorOutputPreservedDuringRender);
        _output.WriteLine("Cancellation completed");
        Assert.Equal(priorOutput, await File.ReadAllBytesAsync(cancelledMp4));

        // 渲染完成但提交遭到檔案鎖阻擋時，也不可破壞舊檔。
        string lockedMp4 = Path.Combine(_tempDir, "Locked.mp4");
        await File.WriteAllBytesAsync(lockedMp4, priorOutput);
        using (var locked = new FileStream(lockedMp4, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            _output.WriteLine("Starting export with locked destination");
            Exception? failure = await Record.ExceptionAsync(() => editingService.ExportEditedMediaAsync(
                rawMp4, lockedMp4, ExportFormat.Mp4Video, segment, 1.0));
            Assert.True(failure is IOException or UnauthorizedAccessException, $"Unexpected commit failure: {failure}");
        }

        Assert.Equal(priorOutput, await File.ReadAllBytesAsync(lockedMp4));
        Assert.Empty(Directory.GetFiles(_tempDir, ".*.tmp.mp4"));

        long originalLength = new FileInfo(rawMp4).Length;
        await Assert.ThrowsAsync<ArgumentException>(() => editingService.ExportEditedMediaAsync(
            rawMp4, rawMp4, ExportFormat.Mp4Video, segment, 1.0));
        Assert.Equal(originalLength, new FileInfo(rawMp4).Length);
    }

    private sealed class InlineProgress(Action<double> callback) : IProgress<double>
    {
        public void Report(double value) => callback(value);
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
}
