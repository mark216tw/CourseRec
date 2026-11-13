using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
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

    public EditorPage()
    {
        ViewModel = App.GetService<EditorViewModel>();
        _settingsService = App.GetService<ISettingsService>();
        _logger = App.GetService<ILogger<EditorPage>>();
        InitializeComponent();
        InfoBarAutoDismiss.Attach(StatusInfoBar);
        KeyDown += OnPageKeyDown;

        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    public EditorViewModel ViewModel { get; }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
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

        if (!ViewModel.HasLoadedVideo)
        {
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
        ViewModel.CurrentPlaybackPositionProvider = null;

        CourseMediaPlayer.IsFullWindow = false;
        DetachPlayerMediaSource();
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

    private void DetachPlayerMediaSource()
    {
        if (CourseMediaPlayer.MediaPlayer is { } player)
        {
            player.PlaybackSession.NaturalDurationChanged -= OnNaturalDurationChanged;
            player.PlaybackSession.PositionChanged -= OnPlaybackPositionChanged;
            player.Pause();
        }

        CourseMediaPlayer.Source = null;
        _currentMediaSource?.Dispose();
        _currentMediaSource = null;
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
        }
    }

    private TimeSpan GetPlayerPosition()
    {
        return CourseMediaPlayer.MediaPlayer?.PlaybackSession.Position ?? TimeSpan.Zero;
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
        CourseMediaPlayer.IsFullWindow = !CourseMediaPlayer.IsFullWindow;
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
