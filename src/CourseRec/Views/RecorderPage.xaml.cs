using System.Collections.Specialized;
using System.Runtime.InteropServices.WindowsRuntime;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Capture;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 錄製工作台頁面（負責綁定即時預覽 <see cref="WriteableBitmap"/> 與切換迷你控制列）。
/// </summary>
public sealed partial class RecorderPage : Page
{
    private readonly ILocalizationService _localization;
    private WriteableBitmap? _previewBitmap;
    private Stream? _previewStream;
    private int _isUpdatingPreview;
    private CapturedFrameData? _pendingPreview;

    public RecorderPage()
    {
        ViewModel = App.GetService<RecorderViewModel>();
        _localization = App.GetService<ILocalizationService>();
        InitializeComponent();
        InfoBarAutoDismiss.Attach(StatusInfoBar);

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public RecorderViewModel ViewModel { get; }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AttachDispatcher(DispatcherQueue);
        ViewModel.PreviewFrameArrived += OnPreviewFrameArrived;
        ViewModel.ToggleMiniOverlayRequested += OnToggleMiniOverlayRequested;
        ViewModel.SessionBookmarks.CollectionChanged += OnSessionBookmarksChanged;
        ViewModel.StartIdleServices();
        UpdatePreviewPresentation();

        // 擷取裝置與音訊監控建立較耗時，排到第一幀之後再啟動。
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ResumeIdleCaptureDeferred))
        {
            ResumeIdleCaptureDeferred();
        }
    }

    private void ResumeIdleCaptureDeferred()
    {
        if (!IsLoaded)
        {
            return;
        }

        ViewModel.ResumeIdleCapture();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PreviewFrameArrived -= OnPreviewFrameArrived;
        ViewModel.ToggleMiniOverlayRequested -= OnToggleMiniOverlayRequested;
        ViewModel.SessionBookmarks.CollectionChanged -= OnSessionBookmarksChanged;
        ViewModel.SuspendIdleCapture();
        Interlocked.Exchange(ref _pendingPreview, null)?.Dispose();
        _previewStream?.Dispose();
        _previewStream = null;
        _previewBitmap = null;
    }

    private void OnPreviewFrameArrived(object? sender, CapturedFrameData incomingFrame)
    {
        if (!ViewModel.IsLivePreviewEnabled)
        {
            return;
        }

        Interlocked.Exchange(ref _pendingPreview, incomingFrame.Retain())?.Dispose();
        QueuePreviewUpdate();
    }

    private void QueuePreviewUpdate()
    {
        if (Interlocked.CompareExchange(ref _isUpdatingPreview, 1, 0) != 0)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() =>
        {
            using CapturedFrameData? frame = Interlocked.Exchange(ref _pendingPreview, null);
            try
            {
                if (frame is null || !IsLoaded || !ViewModel.IsLivePreviewEnabled)
                {
                    return;
                }

                if (frame.Width <= 0 || frame.Height <= 0)
                {
                    return;
                }

                if (_previewBitmap is null
                    || _previewBitmap.PixelWidth != frame.Width
                    || _previewBitmap.PixelHeight != frame.Height)
                {
                    _previewStream?.Dispose();
                    _previewBitmap = new WriteableBitmap(frame.Width, frame.Height);
                    _previewStream = _previewBitmap.PixelBuffer.AsStream();
                    PreviewImage.Source = _previewBitmap;
                    PreviewPlaceholderText.Visibility = Visibility.Collapsed;
                }

                Stream pixelStream = _previewStream!;
                int copyLen = Math.Min(frame.BgraPixels.Length, frame.Width * frame.Height * 4);
                pixelStream.Seek(0, SeekOrigin.Begin);
                pixelStream.Write(frame.BgraPixels, 0, copyLen);
                _previewBitmap.Invalidate();
            }
            finally
            {
                Interlocked.Exchange(ref _isUpdatingPreview, 0);
                if (Volatile.Read(ref _pendingPreview) is not null)
                {
                    QueuePreviewUpdate();
                }
            }
        }))
        {
            Interlocked.Exchange(ref _pendingPreview, null)?.Dispose();
            Interlocked.Exchange(ref _isUpdatingPreview, 0);
        }
    }

    private void OnLivePreviewToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            ViewModel.IsLivePreviewEnabled = toggle.IsOn;
        }

        UpdatePreviewPresentation();
    }

    private void UpdatePreviewPresentation()
    {
        bool isEnabled = ViewModel.IsLivePreviewEnabled;
        PreviewImage.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        PreviewPlaceholderText.Text = isEnabled
            ? _localization.GetString("RecorderPreviewEmpty")
            : _localization.GetString("RecorderPreviewDisabledText");
        PreviewPlaceholderText.Visibility = isEnabled && _previewBitmap is not null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnToggleMiniOverlayRequested(object? sender, EventArgs e)
    {
        App.GetService<IMiniOverlayService>().Toggle();
    }

    private void OnBookmarkNoteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        if (ViewModel.AddBookmarkCommand.CanExecute(null))
        {
            ViewModel.AddBookmarkCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnSessionBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || ViewModel.SessionBookmarks.Count == 0)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            int lastIndex = ViewModel.SessionBookmarks.Count - 1;
            if (lastIndex >= 0)
            {
                SessionBookmarksList.ScrollIntoView(ViewModel.SessionBookmarks[lastIndex]);
            }
        });
    }

    private void OnRemoveSessionBookmarkClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BookmarkItem bookmark })
        {
            ViewModel.RemoveBookmark(bookmark);
        }
    }
}
