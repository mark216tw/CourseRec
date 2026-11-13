using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 跨頁面導覽與快剪載入協調介面。
/// </summary>
public interface INavigationCoordinator
{
    event EventHandler<NavigationPageKey>? NavigationRequested;

    event EventHandler<string>? OpenVideoInEditorRequested;

    event EventHandler<string>? ReleaseVideoFileRequested;

    event EventHandler<(string OldPath, string NewPath)>? VideoFileRenamed;

    event EventHandler<string>? VideoFileDeleted;

    void NavigateTo(NavigationPageKey pageKey);

    void OpenInEditor(string videoFilePath);

    void RequestReleaseVideoFile(string videoFilePath);

    void NotifyVideoFileRenamed(string oldPath, string newPath);

    void NotifyVideoFileDeleted(string deletedPath);
}
