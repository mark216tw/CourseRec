using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 啟動階段耗時量測：從進入點開始記錄各初始化節點，於日誌輸出以利比較優化前後差異。
/// </summary>
public static class StartupTrace
{
    private static readonly Stopwatch Clock = new();
    private static readonly object Gate = new();
    private static readonly List<(string Stage, long ElapsedMs)> Marks = [];

    public static void Start()
    {
        lock (Gate)
        {
            Marks.Clear();
            Clock.Restart();
        }
    }

    public static void Mark(string stage)
    {
        lock (Gate)
        {
            if (!Clock.IsRunning)
            {
                Clock.Start();
            }

            Marks.Add((stage, Clock.ElapsedMilliseconds));
        }
    }

    public static void Flush(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        (string Stage, long ElapsedMs)[] snapshot;
        lock (Gate)
        {
            if (Marks.Count == 0)
            {
                return;
            }

            snapshot = [.. Marks];
            Marks.Clear();
        }

        foreach ((string stage, long elapsedMs) in snapshot)
        {
            logger.LogInformation("啟動耗時：{Stage}={ElapsedMs}ms", stage, elapsedMs);
        }
    }
}
