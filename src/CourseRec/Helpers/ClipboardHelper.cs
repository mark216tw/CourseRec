using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Com.VibeCode.CourseRec.Helpers;

/// <summary>
/// 確保 WinRT 剪貼簿 API 一律在建立它的 UI 執行緒上呼叫。
/// </summary>
public static class ClipboardHelper
{
    private static DispatcherQueue? _dispatcherQueue;

    public static void Initialize(DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _dispatcherQueue = dispatcherQueue;
    }

    public static async Task SetBitmapAsync(StorageFile file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        DispatcherQueue? dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null || dispatcherQueue.HasThreadAccess)
        {
            SetBitmapCore(file);
            return;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    SetBitmapCore(file);
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException("無法排入 UI 執行緒設定剪貼簿。"));
        }

        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void SetBitmapCore(StorageFile file)
    {
        var dataPackage = new DataPackage();
        dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromFile(file));
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();
    }
}
