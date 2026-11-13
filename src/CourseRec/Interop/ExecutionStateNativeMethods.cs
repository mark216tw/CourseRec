using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 錄影期間防止系統休眠與關閉螢幕的 Win32 P/Invoke。
/// </summary>
public static partial class ExecutionStateNativeMethods
{
    public const uint EsContinuous = 0x80000000;
    public const uint EsSystemRequired = 0x00000001;
    public const uint EsDisplayRequired = 0x00000002;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint SetThreadExecutionState(uint esFlags);
}
