using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>共用有界佇列、背景批次寫入、重複訊息彙總及檔案輪替。</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logFilePath;
    private readonly long _maxLogFileBytes;
    private readonly Channel<LogEntry> _entries;
    private readonly Task _writerTask;
    private long _droppedEntries;
    private int _isDisposed;

    public FileLoggerProvider()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CourseRec", "logs", "courserec.log"))
    {
    }

    public FileLoggerProvider(string logFilePath, long maxLogFileBytes = 5L * 1024L * 1024L, int capacity = 2048)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxLogFileBytes, 0);
        _logFilePath = Path.GetFullPath(logFilePath);
        _maxLogFileBytes = maxLogFileBytes;
        _entries = Channel.CreateBounded<LogEntry>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Enqueue(string categoryName, LogLevel level, string message)
    {
        if (Volatile.Read(ref _isDisposed) == 0
            && !_entries.Writer.TryWrite(new LogEntry(DateTimeOffset.Now, categoryName, level, message)))
        {
            Interlocked.Increment(ref _droppedEntries);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            _entries.Writer.TryComplete();
        }

        try
        {
            if (!_writerTask.Wait(TimeSpan.FromSeconds(5)))
            {
                Interlocked.Increment(ref _droppedEntries);
            }
        }
        catch (AggregateException)
        {
            // 日誌寫入失敗已在背景迴圈內記錄為遺失筆數，關閉時不再拋出。
        }
    }

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        var batch = new List<LogEntry>(256);
        try
        {
            while (await _entries.Reader.WaitToReadAsync().ConfigureAwait(false))
            {
                if (Volatile.Read(ref _isDisposed) == 0)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                }

                batch.Clear();
                while (batch.Count < 256 && _entries.Reader.TryRead(out LogEntry entry))
                {
                    batch.Add(entry);
                }

                long dropped = 0;
                try
                {
                    if (writer is not null && writer.BaseStream.Length >= _maxLogFileBytes)
                    {
                        await writer.DisposeAsync().ConfigureAwait(false);
                        writer = null;
                    }

                    if (writer is null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
                        var existing = new FileInfo(_logFilePath);
                        if (existing.Exists && existing.Length >= _maxLogFileBytes)
                        {
                            File.Move(_logFilePath, _logFilePath + ".prev", overwrite: true);
                        }

                        writer = new StreamWriter(new FileStream(_logFilePath, FileMode.Append,
                            FileAccess.Write, FileShare.Read, 4096, useAsync: true), new UTF8Encoding(false));
                    }

                    dropped = Interlocked.Exchange(ref _droppedEntries, 0);
                    if (dropped > 0)
                    {
                        await writer.WriteLineAsync($"{DateTimeOffset.Now:O} [Warning] FileLogger: {dropped} log entries dropped (queue full or I/O unavailable).")
                            .ConfigureAwait(false);
                    }

                    foreach (var group in batch.GroupBy(e => (e.Category, e.Level, e.Message)))
                    {
                        LogEntry first = group.First();
                        int count = group.Count();
                        string suffix = count > 1 ? $" (batch occurrences: {count})" : string.Empty;
                        await writer.WriteLineAsync($"{first.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{first.Level}] {first.Category}: {first.Message}{suffix}")
                            .ConfigureAwait(false);
                    }

                    await writer.FlushAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 日誌失敗不可終止錄影；下一批重試並回報遺失筆數。
                    Interlocked.Add(ref _droppedEntries, batch.Count + dropped);
                    if (writer is not null)
                    {
                        try
                        {
                            await writer.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // 原始 I/O 錯誤已透過遺失計數保留，避免遞迴記錄。
                        }

                        writer = null;
                    }
                }
            }
        }
        finally
        {
            if (writer is not null)
            {
                try
                {
                    await writer.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 關閉期間磁碟不可用時，不讓日誌錯誤中斷應用程式結束。
                }
            }
        }
    }

    private readonly record struct LogEntry(DateTimeOffset Timestamp, string Category, LogLevel Level, string Message);
}
