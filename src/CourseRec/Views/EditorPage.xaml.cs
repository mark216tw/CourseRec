using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using System.ComponentModel;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 快剪與複習編輯器頁面（串接 <see cref="MediaPlayerElement"/> 倍速播放、時間跳轉與剪輯區間選擇）。
/// </summary>
public sealed partial class EditorPage : Page
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<EditorPage> _logger;
    private MediaSource? _currentMediaSource;
    private string _savedVideoPath = string.Empty;
    private TimeSpan _savedPosition;
    private TimeSpan? _restorePosition;
    private long _lastPositionSaveTicks;
    private long _lastPlayheadUpdateTicks;
    private bool _isTimelineSubscribed;
    private bool _isPlayerFullScreen;
    private bool _wasWindowMaximized;

    public EditorPage()
    {
        ViewModel = App.GetService<EditorViewModel>();
        _settingsService = App.GetService<ISettingsService>();
        _logger = App.GetService<ILogger<EditorPage>>();
        if (ViewModel.ConfirmDiscardEditsAsync is null
            && Application.Current is App { MainWindowInstance: MainWindow mainWindow })
        {
            ViewModel.ConfirmDiscardEditsAsync = mainWindow.ConfirmDiscardEditorChangesAsync;
        }

        InitializeComponent();
        InfoBarAutoDismiss.Attach(StatusInfoBar);
        KeyDown += OnPageKeyDown;

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public EditorViewModel ViewModel { get; }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _isPlayerFullScreen)
        {
            ExitPlayerFullScreen();
            e.Handled = true;
            return;
        }

        if (!ViewModel.CanInteract || CourseMediaPlayer.MediaPlayer is not { } player || IsEditableElementFocused())
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Space:
                if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    player.Pause();
                }
                else
                {
                    player.Play();
                }

                e.Handled = true;
                break;
            case VirtualKey.Left:
                SeekBySeconds(player, IsControlKeyDown() ? -30 : -5);
                e.Handled = true;
                break;
            case VirtualKey.Right:
                SeekBySeconds(player, IsControlKeyDown() ? 30 : 5);
                e.Handled = true;
                break;
            case VirtualKey.Up:
                ViewModel.SeekToPreviousBookmarkCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                ViewModel.SeekToNextBookmarkCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private bool IsEditableElementFocused()
    {
        if (XamlRoot is null)
        {
            return false;
        }

        return FocusManager.GetFocusedElement(XamlRoot) is
            TextBox or PasswordBox or RichEditBox or AutoSuggestBox or ComboBox or Slider or ButtonBase or ListViewItem;
    }

    private static bool IsControlKeyDown()
    {
        CoreVirtualKeyStates state = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return (state & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
    }

    private void SeekBySeconds(MediaPlayer player, int seconds)
    {
        TimeSpan position = player.PlaybackSession.Position + TimeSpan.FromSeconds(seconds);
        TimeSpan maximum = player.PlaybackSession.NaturalDuration > TimeSpan.Zero
            ? player.PlaybackSession.NaturalDuration
            : ViewModel.TotalDuration;
        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }
        else if (maximum > TimeSpan.Zero && position > maximum)
        {
            position = maximum;
        }

        player.PlaybackSession.Position = position;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.CurrentPlaybackPositionProvider = GetPlayerPosition;
        ViewModel.VideoSourceLoaded += OnVideoSourceLoaded;
        ViewModel.VideoSourceReleased += OnVideoSourceReleased;
        ViewModel.PlaybackRateRequested += OnPlaybackRateRequested;
        ViewModel.SeekRequested += OnSeekRequested;
        ViewModel.ToastRequested += OnToastRequested;
        SubscribeTimeline();

        if (!ViewModel.HasLoadedVideo)
        {
            if (ViewModel.IsLoadingVideo)
            {
                return;
            }

            string? lastVideoPath = _settingsService.Current.LastEditorVideoPath;
            long lastPositionTicks = _settingsService.Current.LastEditorPositionTicks;
            if (!string.IsNullOrWhiteSpace(lastVideoPath) && File.Exists(lastVideoPath))
            {
                _restorePosition = TimeSpan.FromTicks(Math.Max(0, lastPositionTicks));
                await ViewModel.LoadVideoAsync(lastVideoPath);
                if (!ViewModel.HasLoadedVideo)
                {
                    _restorePosition = null;
                }
            }

            return;
        }

        if (!string.IsNullOrEmpty(ViewModel.LoadedVideoPath))
        {
            bool restorePosition = string.Equals(_savedVideoPath, ViewModel.LoadedVideoPath, StringComparison.OrdinalIgnoreCase)
                && _savedPosition > TimeSpan.Zero;
            TimeSpan targetPos = _savedPosition;
            await AttachVideoFileToPlayerAsync(ViewModel.LoadedVideoPath);
            if (restorePosition && CourseMediaPlayer.MediaPlayer is { } player)
            {
                player.PlaybackSession.Position = targetPos;
            }

            RefreshTimeline();
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _savedVideoPath = ViewModel.LoadedVideoPath;
        _savedPosition = GetPlayerPosition();
        _ = SaveEditorPositionAsync(_savedVideoPath, _savedPosition);

        ViewModel.VideoSourceLoaded -= OnVideoSourceLoaded;
        ViewModel.VideoSourceReleased -= OnVideoSourceReleased;
        ViewModel.PlaybackRateRequested -= OnPlaybackRateRequested;
        ViewModel.SeekRequested -= OnSeekRequested;
        ViewModel.ToastRequested -= OnToastRequested;
        UnsubscribeTimeline();
        ViewModel.CurrentPlaybackPositionProvider = null;

        ExitPlayerFullScreen();
        DetachPlayerMediaSource(disposePlayer: true);
        MemoryTrimHelper.RequestTrim();
    }

    private async void OnOpenVideoFileClicked(object sender, RoutedEventArgs e)
    {
        if (Application.Current is not App { MainWindowInstance: MainWindow mainWindow })
        {
            return;
        }

        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".mp4");
        InitializeWithWindow.Initialize(picker, mainWindow.WindowHandle);

        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null && !string.IsNullOrWhiteSpace(file.Path))
        {
            await ViewModel.LoadVideoAsync(file.Path);
        }
    }

    private async void OnVideoSourceLoaded(object? sender, string videoFilePath)
    {
        TimeSpan? restoreTarget = _restorePosition;
        _restorePosition = null;
        _savedVideoPath = videoFilePath;
        _savedPosition = TimeSpan.Zero;
        await AttachVideoFileToPlayerAsync(videoFilePath);

        if (restoreTarget is { } target
            && target > TimeSpan.Zero
            && CourseMediaPlayer.MediaPlayer is { } player)
        {
            TimeSpan maximum = ViewModel.TotalDuration;
            if (maximum > TimeSpan.Zero && target > maximum - TimeSpan.FromSeconds(5))
            {
                target = TimeSpan.Zero;
            }

            player.PlaybackSession.Position = target;
        }

        RefreshTimeline();
    }

    private void OnVideoSourceReleased(object? sender, EventArgs e)
    {
        _savedVideoPath = string.Empty;
        _savedPosition = TimeSpan.Zero;
        DetachPlayerMediaSource();
    }

    private async Task AttachVideoFileToPlayerAsync(string videoFilePath)
    {
        DetachPlayerMediaSource();
        if (!File.Exists(videoFilePath))
        {
            return;
        }

        StorageFile storageFile = await StorageFile.GetFileFromPathAsync(videoFilePath);
        _currentMediaSource = MediaSource.CreateFromStorageFile(storageFile);
        CourseMediaPlayer.Source = _currentMediaSource;

        MediaPlayer? player = CourseMediaPlayer.MediaPlayer;
        if (player is not null)
        {
            double speed = ViewModel.SelectedSpeedOption?.Value ?? 1.0;
            player.PlaybackSession.PlaybackRate = speed;
            player.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
            player.PlaybackSession.NaturalDurationChanged += OnNaturalDurationChanged;
            player.PlaybackSession.PositionChanged -= OnPlaybackPositionChanged;
            player.PlaybackSession.PositionChanged += OnPlaybackPositionChanged;
        }
    }

    private void DetachPlayerMediaSource(bool disposePlayer = false)
    {
        MediaPlayer? player = CourseMediaPlayer.MediaPlayer;
        if (player is not null)
        {
            player.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
            player.PlaybackSession.PositionChanged -= OnPlaybackPositionChanged;
            player.Pause();
        }

        CourseMediaPlayer.Source = null;
        _currentMediaSource?.Dispose();
        _currentMediaSource = null;

        if (disposePlayer && player is not null)
        {
            CourseMediaPlayer.SetMediaPlayer(null);
            player.Dispose();
        }
    }

    private void OnNaturalDurationChanged(MediaPlaybackSession sender, object args)
    {
        TimeSpan dur = sender.NaturalDuration;
        if (dur > TimeSpan.Zero)
        {
            DispatcherQueue.TryEnqueue(() => ViewModel.UpdateResolvedDurationFromPlayer(dur));
        }
    }

    private void OnPlaybackPositionChanged(MediaPlaybackSession sender, object args)
    {
        long now = Environment.TickCount64;
        TimeSpan position = sender.Position;
        long lastPlayhead = Interlocked.Read(ref _lastPlayheadUpdateTicks);
        if (now - lastPlayhead >= 50
            && Interlocked.CompareExchange(ref _lastPlayheadUpdateTicks, now, lastPlayhead) == lastPlayhead)
        {
            DispatcherQueue.TryEnqueue(() => EditorTimelineControl.UpdatePlayhead(position));
        }

        long lastSave = Interlocked.Read(ref _lastPositionSaveTicks);
        if (now - lastSave < 5000
            || Interlocked.CompareExchange(ref _lastPositionSaveTicks, now, lastSave) != lastSave)
        {
            return;
        }

        string videoPath = _savedVideoPath;
        if (string.IsNullOrWhiteSpace(videoPath))
        {
            return;
        }

        _ = SaveEditorPositionAsync(videoPath, sender.Position);
    }

    private async Task SaveEditorPositionAsync(string videoPath, TimeSpan position)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || position <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            await _settingsService.SaveLastEditorStateAsync(videoPath, position.Ticks);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "儲存上次播放位置失敗：{FilePath}", videoPath);
        }
    }

    private void OnPlaybackRateRequested(object? sender, double rate)
    {
        if (CourseMediaPlayer.MediaPlayer is { } player)
        {
            player.PlaybackSession.PlaybackRate = rate;
        }
    }

    private void OnSeekRequested(object? sender, TimeSpan targetPosition)
    {
        if (CourseMediaPlayer.MediaPlayer is { } player)
        {
            player.PlaybackSession.Position = targetPosition;
            EditorTimelineControl.UpdatePlayhead(targetPosition);
        }
    }

    private TimeSpan GetPlayerPosition()
    {
        return CourseMediaPlayer.MediaPlayer?.PlaybackSession.Position ?? TimeSpan.Zero;
    }

    private void SubscribeTimeline()
    {
        if (_isTimelineSubscribed)
        {
            return;
        }

        _isTimelineSubscribed = true;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.CutSegments.CollectionChanged += OnTimelineCollectionsChanged;
        ViewModel.Bookmarks.CollectionChanged += OnTimelineCollectionsChanged;
        EditorTimelineControl.SeekRequested += OnTimelineSeekRequested;

        (string seek, string trimStart, string trimEnd, string pendingCut, string bookmark, string cutSegment) =
            ViewModel.GetTimelineToolTipFormats();
        EditorTimelineControl.ApplyToolTipFormats(seek, trimStart, trimEnd, pendingCut, bookmark, cutSegment);
        RefreshTimeline();
    }

    private void UnsubscribeTimeline()
    {
        if (!_isTimelineSubscribed)
        {
            return;
        }

        _isTimelineSubscribed = false;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.CutSegments.CollectionChanged -= OnTimelineCollectionsChanged;
        ViewModel.Bookmarks.CollectionChanged -= OnTimelineCollectionsChanged;
        EditorTimelineControl.SeekRequested -= OnTimelineSeekRequested;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(EditorViewModel.TotalDuration):
            case nameof(EditorViewModel.TrimStart):
            case nameof(EditorViewModel.TrimEnd):
            case nameof(EditorViewModel.PendingCutStart):
            case nameof(EditorViewModel.HasLoadedVideo):
                RefreshTimeline();
                break;
        }
    }

    private void OnTimelineCollectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        RefreshTimeline();
    }

    private void RefreshTimeline()
    {
        EditorTimelineControl.Update(
            ViewModel.TotalDuration,
            ViewModel.TrimStart,
            ViewModel.TrimEnd,
            ViewModel.PendingCutStart,
            GetPlayerPosition(),
            ViewModel.CutSegments,
            ViewModel.Bookmarks);
    }

    private void OnTimelineSeekRequested(object? sender, TimeSpan targetPosition)
    {
        if (CourseMediaPlayer.MediaPlayer is { } player)
        {
            player.PlaybackSession.Position = targetPosition;
        }
    }

    private void OnBookmarkItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BookmarkItem bm)
        {
            ViewModel.SeekToBookmark(bm);
        }
    }

    private void OnBookmarkMoreClicked(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanInteract && sender is Button { Tag: FrameworkElement { ContextFlyout: MenuFlyout flyout } } button)
        {
            flyout.ShowAt(button);
        }
    }

    private async void OnEditBookmarkClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanInteract || sender is not MenuFlyoutItem { Tag: BookmarkItem bookmark } || XamlRoot is null)
        {
            return;
        }

        (string title, string confirm, string cancel) = ViewModel.GetEditBookmarkDialogTexts();
        var noteBox = new TextBox
        {
            Header = ViewModel.BookmarkNoteHeader,
            Text = bookmark.Note,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 280,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = noteBox,
            PrimaryButtonText = confirm,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(noteBox.Text),
        };
        noteBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(noteBox.Text);
        dialog.Opened += (_, _) =>
        {
            noteBox.Focus(FocusState.Programmatic);
            noteBox.SelectAll();
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.UpdateBookmarkNoteAsync(bookmark, noteBox.Text);
        }
    }

    private async void OnDeleteBookmarkClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanInteract || sender is not MenuFlyoutItem { Tag: BookmarkItem bookmark } || XamlRoot is null)
        {
            return;
        }

        (string title, string message, string confirm, string cancel) = ViewModel.GetDeleteBookmarkDialogTexts(bookmark);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = confirm,
            CloseButtonText = cancel,
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteBookmarkConfirmedAsync(bookmark);
        }
    }

    private void OnToggleFullScreenClicked(object sender, RoutedEventArgs e)
    {
        if (_isPlayerFullScreen)
        {
            ExitPlayerFullScreen();
        }
        else
        {
            EnterPlayerFullScreen();
        }
    }

    private void OnExitFullScreenClicked(object sender, RoutedEventArgs e)
    {
        ExitPlayerFullScreen();
    }

    private void OnExitFullScreenInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_isPlayerFullScreen)
        {
            return;
        }

        ExitPlayerFullScreen();
        args.Handled = true;
    }

    private void EnterPlayerFullScreen()
    {
        if (_isPlayerFullScreen
            || CourseMediaPlayer.MediaPlayer is null
            || Application.Current is not App { MainWindowInstance: MainWindow mainWindow })
        {
            return;
        }

        _wasWindowMaximized = mainWindow.AppWindow.Presenter is OverlappedPresenter presenter
            && presenter.State == OverlappedPresenterState.Maximized;

        PlayerHostBorder.Child = null;
        PlayerFullScreenHost.Children.Insert(0, CourseMediaPlayer);
        PlayerFullScreenHost.Visibility = Visibility.Visible;
        mainWindow.SetNavigationPaneVisible(false);
        mainWindow.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

        _isPlayerFullScreen = true;
        CourseMediaPlayer.Focus(FocusState.Programmatic);
    }

    private void ExitPlayerFullScreen()
    {
        if (!_isPlayerFullScreen)
        {
            return;
        }

        _isPlayerFullScreen = false;

        PlayerFullScreenHost.Children.Remove(CourseMediaPlayer);
        PlayerHostBorder.Child = CourseMediaPlayer;
        PlayerFullScreenHost.Visibility = Visibility.Collapsed;

        if (Application.Current is App { MainWindowInstance: MainWindow mainWindow })
        {
            mainWindow.AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
            if (_wasWindowMaximized && mainWindow.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }

            mainWindow.SetNavigationPaneVisible(true);
        }
    }

    private void OnToastRequested(object? sender, string message)
    {
        DispatcherQueue.TryEnqueue(() => ToastPresenter.Show(message));
    }

    private void OnRemoveCutSegmentClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TrimSegment segment })
        {
            ViewModel.RemoveCutSegment(segment);
        }
    }

    private async void OnResetEditsClicked(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanInteract || XamlRoot is null)
        {
            return;
        }

        if (ViewModel.HasEditsToReset)
        {
            (string title, string message, string confirm, string cancel) = ViewModel.GetResetDialogTexts();
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = message,
                PrimaryButtonText = confirm,
                CloseButtonText = cancel,
                DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        ViewModel.ResetEditsConfirmed();
    }
}
