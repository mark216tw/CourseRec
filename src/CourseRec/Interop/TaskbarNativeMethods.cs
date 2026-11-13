using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// 工作列 COM 建立、覆蓋圖示載入與釋放 Win32 P/Invoke。
/// </summary>
internal static partial class TaskbarNativeMethods
{
    private const uint ClsCtxInProcServer = 0x00000001;
    private const uint ImageIcon = 1;
    private const uint LrLoadFromFile = 0x00000010;

    private static readonly Guid TaskbarListClassId = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid TaskbarListInterfaceId = new("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf");

    internal static ITaskbarList3? CreateTaskbarList()
    {
        Guid classId = TaskbarListClassId;
        Guid interfaceId = TaskbarListInterfaceId;
        int result = CoCreateInstance(ref classId, 0, ClsCtxInProcServer, ref interfaceId, out nint unknown);
        if (result < 0 || unknown == 0)
        {
            return null;
        }

        try
        {
            var taskbar = (ITaskbarList3)Marshal.GetObjectForIUnknown(unknown);
            if (taskbar.HrInit() < 0)
            {
                return null;
            }

            return taskbar;
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    internal static nint LoadIconFromFile(string path, int size)
    {
        return LoadImage(0, path, ImageIcon, size, size, LrLoadFromFile);
    }

    internal static void DestroyIconHandle(nint icon)
    {
        if (icon != 0)
        {
            DestroyIcon(icon);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        ref Guid classId,
        nint outerUnknown,
        uint classContext,
        ref Guid interfaceId,
        out nint instance);

    [LibraryImport("user32.dll", EntryPoint = "LoadImageW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(nint icon);
}
