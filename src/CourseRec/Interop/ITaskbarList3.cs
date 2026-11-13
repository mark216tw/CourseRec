using System.Runtime.InteropServices;

namespace Com.VibeCode.CourseRec.Interop;

/// <summary>
/// Windows 工作列清單 COM 介面（僅宣告至 <c>SetOverlayIcon</c> 所需的 vtable 順序）。
/// </summary>
[ComImport]
[Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{
    [PreserveSig]
    int HrInit();

    [PreserveSig]
    int AddTab(nint hwnd);

    [PreserveSig]
    int DeleteTab(nint hwnd);

    [PreserveSig]
    int ActivateTab(nint hwnd);

    [PreserveSig]
    int SetActiveAlt(nint hwnd);

    [PreserveSig]
    int MarkFullscreenWindow(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool isFullscreen);

    [PreserveSig]
    int SetProgressValue(nint hwnd, ulong completed, ulong total);

    [PreserveSig]
    int SetProgressState(nint hwnd, int flags);

    [PreserveSig]
    int RegisterTab(nint hwndTab, nint hwndMdi);

    [PreserveSig]
    int UnregisterTab(nint hwndTab);

    [PreserveSig]
    int SetTabOrder(nint hwndTab, nint hwndInsertBefore);

    [PreserveSig]
    int SetTabActive(nint hwndTab, nint hwndInsertBefore, uint reserved);

    [PreserveSig]
    int ThumbBarAddButtons(nint hwnd, uint buttonCount, nint buttons);

    [PreserveSig]
    int ThumbBarUpdateButtons(nint hwnd, uint buttonCount, nint buttons);

    [PreserveSig]
    int ThumbBarSetImageList(nint hwnd, nint imageList);

    [PreserveSig]
    int SetOverlayIcon(nint hwnd, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);

    [PreserveSig]
    int SetThumbnailTooltip(nint hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tooltip);

    [PreserveSig]
    int SetThumbnailClip(nint hwnd, nint clipRect);
}
