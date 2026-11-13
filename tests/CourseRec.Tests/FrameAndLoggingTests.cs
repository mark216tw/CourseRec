using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Services.Capture;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Com.VibeCode.CourseRec.Tests;

public sealed class FrameAndLoggingTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "CourseRecFramesLogs_" + Guid.NewGuid().ToString("N"));

    public FrameAndLoggingTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public async Task FrameLeases_KeepPixelsAliveUntilAllConcurrentConsumersFinish()
    {
        int releases = 0;
        byte[] pixels = [1, 2, 3, 255];
        var original = new CapturedFrameData(pixels, 1, 1, 100, () =>
        {
            Array.Clear(pixels); // 模擬最後使用權釋放後，擷取端才覆寫槽位。
            Interlocked.Increment(ref releases);
        });
        CapturedFrameData[] readers = Enumerable.Range(0, 64).Select(_ => original.Retain()).ToArray();
        original.Dispose();
        Assert.Equal(0, releases);
        Assert.Throws<ObjectDisposedException>(() => original.Retain());

        await Task.WhenAll(readers.Select(reader => Task.Run(() =>
        {
            Assert.Equal(new byte[] { 1, 2, 3, 255 }, reader.BgraPixels);
            reader.Dispose();
            reader.Dispose();
        })));

        Assert.Equal(1, releases);
        Assert.All(pixels, value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task FileLogger_DisposeDrainsSharedQueueAndSummarizesRepeatedErrors()
    {
        string path = Path.Combine(_tempDir, "shared.log");
        var provider = new FileLoggerProvider(path);
        ILogger first = provider.CreateLogger("Encoder");
        ILogger second = provider.CreateLogger("Bookmarks");
        for (int i = 0; i < 32; i++)
        {
            first.LogError("Repeated encoder error");
        }

        second.LogInformation("Last checkpoint saved");
        provider.Dispose();
        provider.Dispose();

        string text = await File.ReadAllTextAsync(path);
        Assert.Contains("Encoder: Repeated encoder error (batch occurrences: 32)", text);
        Assert.Contains("Bookmarks: Last checkpoint saved", text);
    }

    [Fact]
    public async Task FileLogger_RotatesExistingOversizedFileBeforeWriting()
    {
        string path = Path.Combine(_tempDir, "rotate.log");
        string previous = new('x', 128);
        await File.WriteAllTextAsync(path, previous);
        using (var provider = new FileLoggerProvider(path, maxLogFileBytes: 64))
        {
            provider.CreateLogger("test").LogInformation("New session");
        }

        Assert.Equal(previous, await File.ReadAllTextAsync(path + ".prev"));
        Assert.Contains("New session", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FileLogger_BoundedQueueReportsOverloadWithoutBlockingProducer()
    {
        string path = Path.Combine(_tempDir, "bounded.log");
        using (var provider = new FileLoggerProvider(path, capacity: 1))
        {
            ILogger logger = provider.CreateLogger("test");
            for (int i = 0; i < 1000; i++)
            {
                logger.LogWarning("Burst {Index}", i);
            }
        }

        Assert.Contains("log entries dropped", await File.ReadAllTextAsync(path));
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);
}
