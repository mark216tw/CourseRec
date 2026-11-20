using System.Runtime;
using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 於釋放大型影音緩衝區或視窗最小化常駐時，於背景節流回收 LOH 與閒置工作集記憶體。
/// </summary>
internal static partial class MemoryTrimHelper
{
    private const long MinTrimIntervalMs = 10_000;
    private static long _lastTrimTickMs;
    private static int _trimInFlight;

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessWorkingSetSizeEx(
        nint hProcess,
        nint dwMinimumWorkingSetSize,
        nint dwMaximumWorkingSetSize,
        uint flags);

    /// <summary>
    /// 在背景執行緒排程一次記憶體整理；短時間內的重複請求會自動略過。
    /// </summary>
    public static void RequestTrim(bool trimWorkingSet = false)
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastTrimTickMs);
        if (!trimWorkingSet && previous != 0 && now - previous < MinTrimIntervalMs)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _trimInFlight, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastTrimTickMs, now);
        _ = Task.Run(() =>
        {
            try
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                if (trimWorkingSet)
                {
                    GC.WaitForPendingFinalizers();
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                    _ = SetProcessWorkingSetSizeEx(GetCurrentProcess(), -1, -1, 0);
                }
            }
            catch
            {
                // 記憶體回收屬最佳努力，不應影響主流程。
            }
            finally
            {
                Interlocked.Exchange(ref _trimInFlight, 0);
            }
        });
    }
}
