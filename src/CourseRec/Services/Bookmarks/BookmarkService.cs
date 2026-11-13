using System.Text.Json;
using Com.VibeCode.CourseRec.Models;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Services.Bookmarks;

/// <summary>
/// 時間書籤 JSON 持久化與多段剪除後時間軸平移演算法實作。
/// </summary>
public sealed class BookmarkService : IBookmarkService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<BookmarkService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public BookmarkService(ILogger<BookmarkService> logger)
    {
        _logger = logger;
    }

    public string GetBookmarkFilePath(string videoFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFilePath);
        string dir = Path.GetDirectoryName(videoFilePath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(videoFilePath);
        return Path.Combine(dir, $"{stem}.bookmarks.json");
    }

    public async Task<BookmarkDocument> LoadForVideoAsync(
        string videoFilePath,
        CancellationToken cancellationToken = default)
    {
        string sidecarPath = GetBookmarkFilePath(videoFilePath);
        if (!File.Exists(sidecarPath))
        {
            return new BookmarkDocument
            {
                VideoFileName = Path.GetFileName(videoFilePath),
            };
        }

        try
        {
            string json = await File.ReadAllTextAsync(sidecarPath, cancellationToken).ConfigureAwait(false);
            BookmarkDocument? doc = JsonSerializer.Deserialize<BookmarkDocument>(json, JsonOptions);
            if (doc is null)
            {
                return new BookmarkDocument { VideoFileName = Path.GetFileName(videoFilePath) };
            }

            doc.Bookmarks = doc.Bookmarks
                .OrderBy(b => b.TimestampTicks)
                .ToList();
            return doc;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "讀取書籤檔失敗，回傳空書籤：{Path}", sidecarPath);
            return new BookmarkDocument
            {
                VideoFileName = Path.GetFileName(videoFilePath),
            };
        }
    }

    public async Task SaveForVideoAsync(
        string videoFilePath,
        TimeSpan totalDuration,
        IReadOnlyList<BookmarkItem> bookmarks,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFilePath);
        ArgumentNullException.ThrowIfNull(bookmarks);

        string sidecarPath = GetBookmarkFilePath(videoFilePath);
        string? dir = Path.GetDirectoryName(sidecarPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var doc = new BookmarkDocument
        {
            VideoFileName = Path.GetFileName(videoFilePath),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            TotalDurationTicks = Math.Max(0L, totalDuration.Ticks),
            Bookmarks = bookmarks.OrderBy(b => b.TimestampTicks).ToList(),
        };

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string tempPath = sidecarPath + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(doc, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, sidecarPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理書籤暫存檔失敗：{Path}", tempPath);
            }

            _writeLock.Release();
        }
    }

    public IReadOnlyList<TrimSegment> BuildKeptSegments(
        TimeSpan totalDuration,
        TimeSpan trimStart,
        TimeSpan trimEnd,
        IReadOnlyList<TrimSegment> cutSegments)
    {
        ArgumentNullException.ThrowIfNull(cutSegments);

        long maxTicks = Math.Max(0L, totalDuration.Ticks);
        long startTicks = Math.Clamp(trimStart.Ticks, 0L, maxTicks);
        long endTicks = trimEnd.Ticks <= 0L
            ? maxTicks
            : Math.Clamp(trimEnd.Ticks, startTicks, maxTicks);

        if (endTicks <= startTicks)
        {
            return [];
        }

        // Normalize and merge overlapping cut ranges clamped to [startTicks, endTicks]
        List<(long Start, long End)> mergedCuts = MergeCutIntervals(startTicks, endTicks, cutSegments);

        var kept = new List<TrimSegment>();
        long cursor = startTicks;
        int index = 1;

        foreach ((long cutStart, long cutEnd) in mergedCuts)
        {
            if (cutStart > cursor)
            {
                kept.Add(new TrimSegment(cursor, cutStart, $"Keep #{index++}"));
            }

            cursor = Math.Max(cursor, cutEnd);
        }

        if (cursor < endTicks)
        {
            kept.Add(new TrimSegment(cursor, endTicks, $"Keep #{index}"));
        }

        return kept;
    }

    public IReadOnlyList<BookmarkItem> RecalculateBookmarksAfterEdit(
        IReadOnlyList<BookmarkItem> originalBookmarks,
        TimeSpan totalDuration,
        TimeSpan trimStart,
        TimeSpan trimEnd,
        IReadOnlyList<TrimSegment> cutSegments)
    {
        ArgumentNullException.ThrowIfNull(originalBookmarks);
        ArgumentNullException.ThrowIfNull(cutSegments);

        if (originalBookmarks.Count == 0)
        {
            return [];
        }

        IReadOnlyList<TrimSegment> keptSegments = BuildKeptSegments(totalDuration, trimStart, trimEnd, cutSegments);
        if (keptSegments.Count == 0)
        {
            return [];
        }

        var remapped = new List<BookmarkItem>();

        foreach (BookmarkItem bm in originalBookmarks.OrderBy(b => b.TimestampTicks))
        {
            long t = bm.TimestampTicks;
            long accumulatedOutputTicks = 0L;

            for (int i = 0; i < keptSegments.Count; i++)
            {
                TrimSegment seg = keptSegments[i];
                bool isLastSegment = i == keptSegments.Count - 1;
                bool insideSegment = isLastSegment
                    ? (t >= seg.StartTicks && t <= seg.EndTicks)
                    : (t >= seg.StartTicks && t < seg.EndTicks);

                if (insideSegment)
                {
                    long shiftedTicks = accumulatedOutputTicks + (t - seg.StartTicks);
                    remapped.Add(bm with { TimestampTicks = shiftedTicks });
                    break;
                }

                accumulatedOutputTicks += seg.EndTicks - seg.StartTicks;
            }
        }

        return remapped;
    }

    private static List<(long Start, long End)> MergeCutIntervals(
        long windowStart,
        long windowEnd,
        IReadOnlyList<TrimSegment> rawCuts)
    {
        var clamped = new List<(long Start, long End)>();
        foreach (TrimSegment c in rawCuts)
        {
            long s = Math.Clamp(c.StartTicks, windowStart, windowEnd);
            long e = Math.Clamp(c.EndTicks, windowStart, windowEnd);
            if (e > s)
            {
                clamped.Add((s, e));
            }
        }

        if (clamped.Count == 0)
        {
            return clamped;
        }

        clamped.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(long Start, long End)> { clamped[0] };

        for (int i = 1; i < clamped.Count; i++)
        {
            (long prevStart, long prevEnd) = merged[^1];
            (long curStart, long curEnd) = clamped[i];

            if (curStart <= prevEnd)
            {
                merged[^1] = (prevStart, Math.Max(prevEnd, curEnd));
            }
            else
            {
                merged.Add((curStart, curEnd));
            }
        }

        return merged;
    }
}
