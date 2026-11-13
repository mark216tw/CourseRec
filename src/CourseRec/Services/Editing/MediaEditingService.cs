using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Com.VibeCode.CourseRec.Services.Editing;

/// <summary>
/// 純 Windows 原生 <see cref="MediaComposition"/> 頭尾修剪、多段合併、音量增強與 MP4 / M4A 匯出實作。
/// </summary>
public sealed class MediaEditingService : IMediaEditingService
{
    private readonly ILogger<MediaEditingService> _logger;

    public MediaEditingService(ILogger<MediaEditingService> logger)
    {
        _logger = logger;
    }

    public async Task<TimeSpan> GetVideoDurationAsync(
        string videoFilePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return TimeSpan.Zero;
        }

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(videoFilePath)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            VideoProperties props = await file.Properties.GetVideoPropertiesAsync()
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (props.Duration > TimeSpan.Zero)
            {
                return props.Duration;
            }

            MediaClip clip = await MediaClip.CreateFromFileAsync(file)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            return clip.OriginalDuration;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "讀取影片時長失敗：{FilePath}", videoFilePath);
            return TimeSpan.Zero;
        }
    }

    public async Task<string?> CaptureVideoFrameToPngAsync(
        string videoFilePath,
        TimeSpan position,
        string outputDirectory,
        bool copyToClipboard = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return null;
        }

        StorageFile srcFile = await StorageFile.GetFileFromPathAsync(videoFilePath)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        MediaClip clip = await MediaClip.CreateFromFileAsync(srcFile)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var composition = new MediaComposition();
        composition.Clips.Add(clip);

        TimeSpan safePos = TimeSpan.FromTicks(Math.Clamp(position.Ticks, 0L, Math.Max(0L, composition.Duration.Ticks - 1)));
        using ImageStream thumbnailStream = await composition
            .GetThumbnailAsync(safePos, 0, 0, VideoFramePrecision.NearestFrame)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(thumbnailStream)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);
        SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        using (softwareBitmap)
        {
            string snapshotsDir = Path.Combine(outputDirectory, "Snapshots");
            Directory.CreateDirectory(snapshotsDir);
            string fullPath = Path.Combine(snapshotsDir, $"Slide_Review_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

            using (var outFileStream = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read))
            {
                using IRandomAccessStream winrtOut = outFileStream.AsRandomAccessStream();
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, winrtOut)
                    .AsTask(cancellationToken)
                    .ConfigureAwait(false);
                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
            }

            if (copyToClipboard)
            {
                try
                {
                    StorageFile savedStorageFile = await StorageFile.GetFileFromPathAsync(fullPath)
                        .AsTask(cancellationToken)
                        .ConfigureAwait(false);
                    await ClipboardHelper.SetBitmapAsync(savedStorageFile, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "編輯器快拍已存檔，但複製至剪貼簿失敗。");
                }
            }

            return fullPath;
        }
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
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceVideoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentNullException.ThrowIfNull(keptSegments);
        sourceVideoPath = Path.GetFullPath(sourceVideoPath);
        destinationFilePath = Path.GetFullPath(destinationFilePath);
        if (string.Equals(sourceVideoPath, destinationFilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and destination must be different files.", nameof(destinationFilePath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (keptSegments.Count == 0)
        {
            throw new InvalidOperationException("剪輯後沒有任何保留區間可供匯出。");
        }

        StorageFile srcFile = await StorageFile.GetFileFromPathAsync(sourceVideoPath)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        var composition = new MediaComposition();
        double clampedGain = ClampAudioGain(audioGainMultiplier);

        MediaClip templateClip = await MediaClip.CreateFromFileAsync(srcFile)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        foreach (TrimSegment segment in keptSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MediaClip clip = templateClip.Clone();

            TimeSpan maxDur = clip.OriginalDuration;
            TimeSpan start = TimeSpan.FromTicks(Math.Clamp(segment.StartTicks, 0L, maxDur.Ticks));
            TimeSpan end = TimeSpan.FromTicks(Math.Clamp(segment.EndTicks, start.Ticks, maxDur.Ticks));
            if (end <= start)
            {
                continue;
            }

            clip.TrimTimeFromStart = start;
            clip.TrimTimeFromEnd = maxDur - end;
            clip.Volume = clampedGain;
            composition.Clips.Add(clip);
        }

        if (composition.Clips.Count == 0)
        {
            throw new InvalidOperationException("剪輯區間長度為零，無法匯出。");
        }

        string? outDir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        string tempPath = Path.Combine(outDir!, $".{Path.GetFileNameWithoutExtension(destinationFilePath)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(destinationFilePath)}");
        try
        {
            using (new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }

            StorageFile dstFile = await StorageFile.GetFileFromPathAsync(tempPath)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);

            MediaEncodingProfile encodingProfile = await CreateExportProfileAsync(srcFile, format, cancellationToken)
                .ConfigureAwait(false);

            MediaTrimmingPreference trimmingPreference = SelectTrimmingPreference(format, clampedGain);
            var renderOp = composition.RenderToFileAsync(dstFile, trimmingPreference, encodingProfile);
            renderOp.Progress = (_, pct) => progress?.Report(pct);

            TranscodeFailureReason failure = await renderOp.AsTask(cancellationToken).ConfigureAwait(false);
            if (failure != TranscodeFailureReason.None)
            {
                throw new InvalidOperationException($"匯出媒體轉碼失敗：{failure}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (new FileInfo(tempPath).Length == 0)
            {
                throw new InvalidOperationException("Media export produced an empty file.");
            }

            File.Move(tempPath, destinationFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理匯出暫存檔失敗：{Path}", tempPath);
            }
        }
    }

    public static MediaTrimmingPreference SelectTrimmingPreference(ExportFormat format, double clampedGain)
    {
        return format == ExportFormat.Mp4Video && Math.Abs(clampedGain - 1.0) < 0.001
            ? MediaTrimmingPreference.Fast
            : MediaTrimmingPreference.Precise;
    }

    public static MediaEncodingProfile CreateM4aTranscriptProfile()
    {
        MediaEncodingProfile m4aProfile = MediaEncodingProfile.CreateM4a(AudioEncodingQuality.Low);
        m4aProfile.Video = null;
        m4aProfile.Audio = AudioEncodingProperties.CreateAac(48_000, 1, 96_000);
        return m4aProfile;
    }

    public static double ClampAudioGain(double multiplier)
    {
        if (double.IsNaN(multiplier) || double.IsInfinity(multiplier))
        {
            return 1.0;
        }

        return Math.Clamp(multiplier, 0.1, 2.0);
    }

    private static async Task<MediaEncodingProfile> CreateExportProfileAsync(
        StorageFile sourceFile,
        ExportFormat format,
        CancellationToken cancellationToken)
    {
        if (format == ExportFormat.M4aAudioOnly)
        {
            return CreateM4aTranscriptProfile();
        }

        try
        {
            MediaEncodingProfile sourceProfile = await MediaEncodingProfile.CreateFromFileAsync(sourceFile)
                .AsTask(cancellationToken)
                .ConfigureAwait(false);
            if (sourceProfile.Video is not null)
            {
                return sourceProfile;
            }
        }
        catch
        {
            // Fall back to standard HD1080p MP4 profile.
        }

        return MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
    }
}
