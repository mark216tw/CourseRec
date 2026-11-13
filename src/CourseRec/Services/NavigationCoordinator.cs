using Com.VibeCode.CourseRec.Models;

namespace Com.VibeCode.CourseRec.Services;

/// <summary>
/// 跨頁面導覽協調實作。
/// </summary>
public sealed class NavigationCoordinator : INavigationCoordinator
{
    public event EventHandler<NavigationPageKey>? NavigationRequested;

    public event EventHandler<string>? OpenVideoInEditorRequested;

    public event EventHandler<string>? ReleaseVideoFileRequested;

    public event EventHandler<(string OldPath, string NewPath)>? VideoFileRenamed;

    public event EventHandler<string>? VideoFileDeleted;

    public void NavigateTo(NavigationPageKey pageKey)
    {
        NavigationRequested?.Invoke(this, pageKey);
    }

    public void OpenInEditor(string videoFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoFilePath);
        NavigationRequested?.Invoke(this, NavigationPageKey.Editor);
        OpenVideoInEditorRequested?.Invoke(this, videoFilePath);
    }

    public void RequestReleaseVideoFile(string videoFilePath)
    {
        if (!string.IsNullOrWhiteSpace(videoFilePath))
        {
            ReleaseVideoFileRequested?.Invoke(this, videoFilePath);
        }
    }

    public void NotifyVideoFileRenamed(string oldPath, string newPath)
    {
        if (!string.IsNullOrWhiteSpace(oldPath) && !string.IsNullOrWhiteSpace(newPath))
        {
            VideoFileRenamed?.Invoke(this, (oldPath, newPath));
        }
    }

    public void NotifyVideoFileDeleted(string deletedPath)
    {
        if (!string.IsNullOrWhiteSpace(deletedPath))
        {
            VideoFileDeleted?.Invoke(this, deletedPath);
        }
    }
}
