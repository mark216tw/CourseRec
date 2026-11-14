using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Bookmarks;
using Com.VibeCode.CourseRec.Services.Editing;
using Com.VibeCode.CourseRec.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Com.VibeCode.CourseRec.ViewModels;

/// <summary>
/// 快剪與複習編輯器 ViewModel（倍速播放、書籤跳轉、頭尾／分段剪除、音量放大與 MP4/M4A 雙用途匯出）。
/// </summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly IMediaEditingService _mediaEditingService;
    private readonly IBookmarkService _bookmarkService;
    private readonly ISettingsService _settingsService;
    private readonly MainViewModel _mainViewModel;
    private readonly ILocalizationService _localization;
    private readonly ILogger<EditorViewModel> _logger;

    private TimeSpan? _pendingCutStart;
    private bool _isSourceAttached;
    private CancellationTokenSource? _exportCts;

    [ObservableProperty]
    public partial bool HasLoadedVideo { get; set; }

    [ObservableProperty]
    public partial string LoadedVideoPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LoadedVideoTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial TimeSpan TotalDuration { get; set; }

    [ObservableProperty]
    public partial TimeSpan TrimStart { get; set; }

    [ObservableProperty]
    public partial TimeSpan TrimEnd { get; set; }

    [ObservableProperty]
    public partial double AudioGainPercent { get; set; } = 100.0;

    [ObservableProperty]
    public partial SelectOption<double>? SelectedSpeedOption { get; set; }

    [ObservableProperty]
    public partial string NewBookmarkNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TrimRangeSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsExporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyPropertyChangedFor(nameof(CanOpenVideo))]
    public partial bool IsSavingBookmarks { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    [NotifyPropertyChangedFor(nameof(CanOpenVideo))]
    public partial bool IsLoadingVideo { get; set; }

    [ObservableProperty]
    public partial double ExportProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial bool IsBannerOpen { get; set; }

    [ObservableProperty]
    public partial string BannerMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity BannerSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletedExportVisibility))]
    public partial string CompletedExportPath { get; set; } = string.Empty;

    public EditorViewModel(
        IMediaEditingService mediaEditingService,
        IBookmarkService bookmarkService,
        ISettingsService settingsService,
        INavigationCoordinator navigationCoordinator,
        MainViewModel mainViewModel,
        ILocalizationService localization,
        ILogger<EditorViewModel> logger)
    {
        _mediaEditingService = mediaEditingService;
        _bookmarkService = bookmarkService;
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _localization = localization;
        _logger = logger;

        PlaybackSpeedOptions =
        [
            new SelectOption<double>(0.25, "0.25x"),
            new SelectOption<double>(0.5, "0.5x"),
            new SelectOption<double>(0.75, "0.75x"),
            new SelectOption<double>(1.0, "1.0x"),
            new SelectOption<double>(1.25, "1.25x"),
            new SelectOption<double>(1.5, "1.5x"),
            new SelectOption<double>(1.75, "1.75x"),
            new SelectOption<double>(2.0, "2.0x"),
        ];

        SelectedSpeedOption = PlaybackSpeedOptions.First(option => option.Value == 1.0);

        navigationCoordinator.OpenVideoInEditorRequested += async (_, path) =>
        {
            await LoadVideoAsync(path, replacementConfirmed: true);
        };
        navigationCoordinator.ReleaseVideoFileRequested += (_, path) => ReleaseIfMatching(path);
        navigationCoordinator.VideoFileRenamed += (_, args) => HandleVideoFileRenamed(args.OldPath, args.NewPath);
        navigationCoordinator.VideoFileDeleted += (_, deletedPath) => UnloadIfMatching(deletedPath);
    }

    public event EventHandler<string>? VideoSourceLoaded;

    public event EventHandler? VideoSourceReleased;

    public event EventHandler<double>? PlaybackRateRequested;

    public event EventHandler<TimeSpan>? SeekRequested;

    public event EventHandler<string>? ToastRequested;

    public ObservableCollection<SelectOption<double>> PlaybackSpeedOptions { get; }

    public ObservableCollection<BookmarkItem> Bookmarks { get; } = [];

    public ObservableCollection<TrimSegment> CutSegments { get; } = [];

    public Func<TimeSpan>? CurrentPlaybackPositionProvider { get; set; }

    public Func<Task<bool>>? ConfirmDiscardEditsAsync { get; set; }

    public Visibility EmptyPlaceholderVisibility => HasLoadedVideo ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WorkspaceVisibility => HasLoadedVideo ? Visibility.Visible : Visibility.Collapsed;

    public Visibility BookmarksEmptyVisibility => Bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ExportProgressVisibility => IsExporting ? Visibility.Visible : Visibility.Collapsed;

    public bool CanInteract => HasLoadedVideo && CanOpenVideo;

    public bool CanOpenVideo => !IsExporting && !IsSavingBookmarks && !IsLoadingVideo;

    public string BookmarkNoteHeader => _localization.GetString("BookmarkNoteHeader");

    public string AudioGainText => _localization.Format("AudioGainValueFormat", AudioGainPercent);

    public string ExportProgressText => _localization.Format("ExportProgressFormat", ExportProgressPercent);

    public (string Title, string ConfirmText, string CancelText) GetEditBookmarkDialogTexts() => (
        _localization.GetString("EditBookmarkTitle"),
        _localization.GetString("SaveBookmarkText"),
        _localization.GetString("DialogCancel"));

    public (string Title, string Message, string ConfirmText, string CancelText) GetDeleteBookmarkDialogTexts(BookmarkItem bookmark) => (
        _localization.GetString("DeleteBookmarkTitle"),
        _localization.Format("DeleteBookmarkMessageFormat", bookmark.FormattedTimestamp, bookmark.Note),
        _localization.GetString("DialogConfirmDelete"),
        _localization.GetString("DialogCancel"));

    public bool HasPendingCutStart => _pendingCutStart.HasValue;

    public Visibility CompletedExportVisibility => string.IsNullOrEmpty(CompletedExportPath) ? Visibility.Collapsed : Visibility.Visible;
    public bool HasEditsToReset => TrimStart != TimeSpan.Zero || TrimEnd != TotalDuration
        || CutSegments.Count > 0 || _pendingCutStart.HasValue || AudioGainPercent != 100.0;

    public (string Title, string Message, string ConfirmText, string CancelText) GetResetDialogTexts() => (
        _localization.GetString("ResetEditsTitle"),
        _localization.GetString("ResetEditsMessage"),
        _localization.GetString("ResetEditsConfirm"),
        _localization.GetString("DialogCancel"));

    public void ResetEditsConfirmed()
    {
        if (!CanInteract)
        {
            return;
        }

        TrimStart = TimeSpan.Zero;
        TrimEnd = TotalDuration;
        CutSegments.Clear();
        _pendingCutStart = null;
        OnPropertyChanged(nameof(HasPendingCutStart));
        AudioGainPercent = 100.0;
        HasUnsavedChanges = false;
        UpdateTrimRangeSummary();
        ShowBanner(_localization.GetString("ResetEditsCompletedText"), InfoBarSeverity.Success);
    }

    partial void OnHasLoadedVideoChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyPlaceholderVisibility));
        OnPropertyChanged(nameof(WorkspaceVisibility));
        OnPropertyChanged(nameof(CanInteract));
    }

    partial void OnIsExportingChanged(bool value)
    {
        OnPropertyChanged(nameof(ExportProgressVisibility));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanOpenVideo));
    }

    partial void OnHasUnsavedChangesChanged(bool value)
    {
        _mainViewModel.HasUnsavedEditorChanges = value;
    }

    partial void OnAudioGainPercentChanged(double value)
    {
        OnPropertyChanged(nameof(AudioGainText));
        if (HasLoadedVideo)
        {
            HasUnsavedChanges = true;
        }
    }

    partial void OnExportProgressPercentChanged(double value)
    {
        OnPropertyChanged(nameof(ExportProgressText));
    }

    partial void OnSelectedSpeedOptionChanged(SelectOption<double>? value)
    {
        if (value is not null)
        {
            PlaybackRateRequested?.Invoke(this, value.Value);
        }
    }

    public async Task LoadVideoAsync(string videoFilePath, bool replacementConfirmed = false)
    {
        if (!CanOpenVideo)
        {
            ShowBanner(_localization.GetString("EditorBusyText"), InfoBarSeverity.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(videoFilePath) || !File.Exists(videoFilePath))
        {
            return;
        }

        bool isDifferentVideo = !string.Equals(videoFilePath, LoadedVideoPath, StringComparison.OrdinalIgnoreCase);
        if (HasUnsavedChanges
            && isDifferentVideo
            && !replacementConfirmed
            && ConfirmDiscardEditsAsync is not null
            && !await ConfirmDiscardEditsAsync())
        {
            return;
        }

        IsLoadingVideo = true;
        try
        {
            BookmarkDocument doc = await _bookmarkService.LoadForVideoAsync(videoFilePath);
            TimeSpan duration = await _mediaEditingService.GetVideoDurationAsync(videoFilePath);
            if (duration <= TimeSpan.Zero && doc.TotalDurationTicks > 0)
            {
                duration = TimeSpan.FromTicks(doc.TotalDurationTicks);
            }

            if (duration <= TimeSpan.Zero)
            {
                duration = TimeSpan.FromHours(1);
            }

            LoadedVideoPath = videoFilePath;
            LoadedVideoTitle = Path.GetFileName(videoFilePath);
            TotalDuration = duration;
            TrimStart = TimeSpan.Zero;
            TrimEnd = duration;
            _pendingCutStart = null;
            OnPropertyChanged(nameof(HasPendingCutStart));

            CutSegments.Clear();
            Bookmarks.Clear();
            foreach (BookmarkItem bm in doc.Bookmarks)
            {
                Bookmarks.Add(bm);
            }

            OnPropertyChanged(nameof(BookmarksEmptyVisibility));
            AudioGainPercent = 100.0;
            HasLoadedVideo = true;
            HasUnsavedChanges = false;
            UpdateTrimRangeSummary();

            _isSourceAttached = true;
            VideoSourceLoaded?.Invoke(this, videoFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "載入編輯器影片失敗：{FilePath}", videoFilePath);
            ShowBanner(_localization.GetString("EditorLoadVideoFailedText"), InfoBarSeverity.Error);
        }
        finally
        {
            IsLoadingVideo = false;
        }
    }

    public void UpdateResolvedDurationFromPlayer(TimeSpan naturalDuration)
    {
        if (naturalDuration <= TimeSpan.Zero)
        {
            return;
        }

        bool wasDefault = TrimEnd == TotalDuration;
        TotalDuration = naturalDuration;
        if (wasDefault || TrimEnd > naturalDuration)
        {
            TrimEnd = naturalDuration;
        }

        UpdateTrimRangeSummary();
    }

    public void SeekToBookmark(BookmarkItem bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        SeekRequested?.Invoke(this, bookmark.Timestamp);
    }

    [RelayCommand]
    private void SeekToPreviousBookmark()
    {
        if (!CanInteract)
        {
            return;
        }

        TimeSpan position = GetCurrentPosition();
        BookmarkItem? target = Bookmarks
            .Where(bookmark => bookmark.Timestamp < position - TimeSpan.FromMilliseconds(500))
            .OrderByDescending(bookmark => bookmark.Timestamp)
            .FirstOrDefault();
        if (target is not null)
        {
            SeekRequested?.Invoke(this, target.Timestamp);
        }
    }

    [RelayCommand]
    private void SeekToNextBookmark()
    {
        if (!CanInteract)
        {
            return;
        }

        TimeSpan position = GetCurrentPosition();
        BookmarkItem? target = Bookmarks
            .Where(bookmark => bookmark.Timestamp > position + TimeSpan.FromMilliseconds(500))
            .OrderBy(bookmark => bookmark.Timestamp)
            .FirstOrDefault();
        if (target is not null)
        {
            SeekRequested?.Invoke(this, target.Timestamp);
        }
    }

    [RelayCommand]
    public async Task AddBookmarkAtCurrentPositionAsync()
    {
        if (!CanInteract)
        {
            return;
        }

        TimeSpan pos = GetCurrentPosition();
        string rawNote = NewBookmarkNote.Trim();
        string note = string.IsNullOrEmpty(rawNote)
            ? _localization.Format("DefaultBookmarkNoteFormat", Bookmarks.Count + 1)
            : rawNote;

        var newItem = new BookmarkItem(Guid.NewGuid(), pos.Ticks, note, DateTimeOffset.UtcNow);
        List<BookmarkItem> sorted = Bookmarks.Append(newItem).OrderBy(b => b.TimestampTicks).ToList();

        if (await SaveBookmarksAsync(sorted, _localization.Format("BookmarkAddedStatusFormat", newItem.FormattedTimestamp, newItem.Note)))
        {
            NewBookmarkNote = string.Empty;
        }
    }

    public async Task<bool> UpdateBookmarkNoteAsync(BookmarkItem bookmark, string note)
    {
        if (!CanInteract || !Bookmarks.Any(item => item.Id == bookmark.Id))
        {
            return false;
        }

        string trimmedNote = note.Trim();
        if (string.IsNullOrEmpty(trimmedNote))
        {
            ShowBanner(_localization.GetString("BookmarkNoteRequiredText"), InfoBarSeverity.Warning);
            return false;
        }

        List<BookmarkItem> updated = Bookmarks
            .Select(item => item.Id == bookmark.Id ? item with { Note = trimmedNote } : item)
            .ToList();
        return await SaveBookmarksAsync(updated, _localization.GetString("BookmarkUpdatedText"));
    }

    public async Task<bool> DeleteBookmarkConfirmedAsync(BookmarkItem bookmark)
    {
        if (!CanInteract || !Bookmarks.Any(item => item.Id == bookmark.Id))
        {
            return false;
        }

        List<BookmarkItem> remaining = Bookmarks.Where(item => item.Id != bookmark.Id).ToList();
        return await SaveBookmarksAsync(remaining, _localization.GetString("BookmarkDeletedText"));
    }

    private async Task<bool> SaveBookmarksAsync(IReadOnlyList<BookmarkItem> bookmarks, string successMessage)
    {
        IsSavingBookmarks = true;
        try
        {
            await _bookmarkService.SaveForVideoAsync(LoadedVideoPath, TotalDuration, bookmarks);
            Bookmarks.Clear();
            foreach (BookmarkItem item in bookmarks)
            {
                Bookmarks.Add(item);
            }

            OnPropertyChanged(nameof(BookmarksEmptyVisibility));
            ShowBanner(successMessage, InfoBarSeverity.Success);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "儲存書籤修改失敗：{VideoPath}", LoadedVideoPath);
            ShowBanner(_localization.GetString("BookmarkSaveFailedText"), InfoBarSeverity.Error);
            return false;
        }
        finally
        {
            IsSavingBookmarks = false;
        }
    }

    [RelayCommand]
    private async Task CaptureCurrentFrameAsync()
    {
        if (!CanInteract)
        {
            return;
        }

        try
        {
            TimeSpan pos = GetCurrentPosition();
            string outputDir = _settingsService.Current.OutputDirectory;
            string? savedPath = await _mediaEditingService.CaptureVideoFrameToPngAsync(
                LoadedVideoPath,
                pos,
                outputDir,
                copyToClipboard: true);

            if (!string.IsNullOrEmpty(savedPath))
            {
                ToastRequested?.Invoke(this, _localization.Format("SnapshotSavedStatusFormat", Path.GetFileName(savedPath)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "擷取播放簡報畫面失敗");
            ShowBanner(_localization.GetString("EditorSnapshotFailedText"), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void OpenSnapshotFolder()
    {
        try
        {
            string snapshotsDirectory = Path.Combine(_settingsService.Current.OutputDirectory, "Snapshots");
            Directory.CreateDirectory(snapshotsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = snapshotsDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "開啟簡報 PNG 資料夾失敗");
            ShowBanner(_localization.GetString("SnapshotFolderOpenFailedText"), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    public void SetTrimStart()
    {
        if (!CanInteract)
        {
            return;
        }

        TimeSpan pos = GetCurrentPosition();
        if (pos < TrimEnd)
        {
            TrimStart = pos;
            HasUnsavedChanges = true;
            UpdateTrimRangeSummary();
        }
    }

    [RelayCommand]
    public void SetTrimEnd()
    {
        if (!CanInteract)
        {
            return;
        }

        TimeSpan pos = GetCurrentPosition();
        if (pos > TrimStart && pos <= TotalDuration)
        {
            TrimEnd = pos;
            HasUnsavedChanges = true;
            UpdateTrimRangeSummary();
        }
    }

    [RelayCommand]
    public void MarkCutStart()
    {
        if (!CanInteract)
        {
            return;
        }

        _pendingCutStart = GetCurrentPosition();
        OnPropertyChanged(nameof(HasPendingCutStart));
    }

    [RelayCommand]
    public void AddCutSegment()
    {
        if (!CanInteract || !_pendingCutStart.HasValue)
        {
            return;
        }

        TimeSpan first = _pendingCutStart.Value;
        TimeSpan second = GetCurrentPosition();
        TimeSpan start = first <= second ? first : second;
        TimeSpan end = first <= second ? second : first;

        _pendingCutStart = null;
        OnPropertyChanged(nameof(HasPendingCutStart));

        if (end - start < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        string label = _localization.Format("CutSegmentDefaultLabelFormat", CutSegments.Count + 1);
        CutSegments.Add(new TrimSegment(start.Ticks, end.Ticks, label));
        HasUnsavedChanges = true;
        UpdateTrimRangeSummary();
    }

    public void RemoveCutSegment(TrimSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (CutSegments.Remove(segment))
        {
            HasUnsavedChanges = true;
            UpdateTrimRangeSummary();
        }
    }

    [RelayCommand]
    private async Task ExportMp4Async()
    {
        await RunExportAsync(ExportFormat.Mp4Video);
    }

    [RelayCommand]
    private async Task ExportM4aAsync()
    {
        await RunExportAsync(ExportFormat.M4aAudioOnly);
    }

    [RelayCommand]
    private void CancelExport()
    {
        _exportCts?.Cancel();
    }

    private async Task RunExportAsync(ExportFormat format)
    {
        if (!CanInteract || string.IsNullOrEmpty(LoadedVideoPath))
        {
            return;
        }

        IsExporting = true;
        ExportProgressPercent = 0;
        _exportCts?.Dispose();
        _exportCts = new CancellationTokenSource();
        CancellationToken token = _exportCts.Token;

        try
        {
            IReadOnlyList<TrimSegment> keptSegments = _bookmarkService.BuildKeptSegments(
                TotalDuration,
                TrimStart,
                TrimEnd,
                CutSegments);

            IReadOnlyList<BookmarkItem> alignedBookmarks = _bookmarkService.RecalculateBookmarksAfterEdit(
                Bookmarks,
                TotalDuration,
                TrimStart,
                TrimEnd,
                CutSegments);

            TimeSpan exportedDuration = TimeSpan.FromTicks(keptSegments.Sum(s => s.Duration.Ticks));
            string outDir = _settingsService.Current.OutputDirectory;
            Directory.CreateDirectory(outDir);

            string stem = Path.GetFileNameWithoutExtension(LoadedVideoPath);
            string suffix = format == ExportFormat.M4aAudioOnly ? "ai_audio" : "edited";
            string ext = format == ExportFormat.M4aAudioOnly ? ".m4a" : ".mp4";
            string destPath = Path.Combine(outDir, $"{stem}_{suffix}_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");

            var progress = new Progress<double>(pct => ExportProgressPercent = Math.Round(pct, 1));
            double gainMultiplier = AudioGainPercent / 100.0;

            await _mediaEditingService.ExportEditedMediaAsync(
                LoadedVideoPath,
                destPath,
                format,
                keptSegments,
                gainMultiplier,
                progress,
                token);

            if (alignedBookmarks.Count > 0)
            {
                await _bookmarkService.SaveForVideoAsync(destPath, exportedDuration, alignedBookmarks);
            }

            HasUnsavedChanges = false;
            CompletedExportPath = destPath;
            ShowBanner(_localization.Format("ExportCompletedFormat", Path.GetFileName(destPath)), InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ShowBanner(_localization.GetString("ExportCancelledText"), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "匯出快剪檔案失敗（Format={Format}）", format);
            ShowBanner(_localization.GetString("ExportFailedText"), InfoBarSeverity.Error);
        }
        finally
        {
            _exportCts?.Dispose();
            _exportCts = null;
            IsExporting = false;
        }
    }

    private TimeSpan GetCurrentPosition()
    {
        TimeSpan pos = CurrentPlaybackPositionProvider?.Invoke() ?? TimeSpan.Zero;
        return TimeSpan.FromTicks(Math.Clamp(pos.Ticks, 0L, Math.Max(0L, TotalDuration.Ticks)));
    }

    private void UpdateTrimRangeSummary()
    {
        IReadOnlyList<TrimSegment> kept = _bookmarkService.BuildKeptSegments(
            TotalDuration,
            TrimStart,
            TrimEnd,
            CutSegments);
        TimeSpan estimatedOutDuration = TimeSpan.FromTicks(kept.Sum(k => k.Duration.Ticks));

        TrimRangeSummaryText = _localization.Format(
            "TrimRangeSummaryFormat",
            FormatHelper.FormatDuration(TrimStart),
            FormatHelper.FormatDuration(TrimEnd),
            CutSegments.Count,
            FormatHelper.FormatDuration(estimatedOutDuration));
    }

    [RelayCommand]
    private void OpenCompletedExport()
    {
        try
        {
            FileActionHelper.Open(CompletedExportPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "開啟匯出結果失敗");
            ShowBanner(_localization.GetString("ResultFileActionFailedText"), InfoBarSeverity.Error);
        }
    }

    [RelayCommand]
    private void OpenExportFolder()
    {
        try
        {
            string outputDir = _settingsService.Current.OutputDirectory;
            Directory.CreateDirectory(outputDir);
            Process.Start(new ProcessStartInfo
            {
                FileName = outputDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "開啟匯出資料夾失敗");
            ShowBanner(_localization.GetString("ExportFolderOpenFailedText"), InfoBarSeverity.Error);
        }
    }

    private void ShowBanner(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        BannerSeverity = severity;
        BannerMessage = message;
        IsBannerOpen = true;
    }

    private void ReleaseIfMatching(string videoFilePath)
    {
        if (HasLoadedVideo && _isSourceAttached && string.Equals(LoadedVideoPath, videoFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _isSourceAttached = false;
            VideoSourceReleased?.Invoke(this, EventArgs.Empty);
        }
    }

    private void HandleVideoFileRenamed(string oldPath, string newPath)
    {
        if (HasLoadedVideo && string.Equals(LoadedVideoPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            LoadedVideoPath = newPath;
            LoadedVideoTitle = Path.GetFileName(newPath);
            _isSourceAttached = true;
            VideoSourceLoaded?.Invoke(this, newPath);
        }
    }

    private void UnloadIfMatching(string deletedPath)
    {
        if (!HasLoadedVideo || !string.Equals(LoadedVideoPath, deletedPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_isSourceAttached)
        {
            _isSourceAttached = false;
            VideoSourceReleased?.Invoke(this, EventArgs.Empty);
        }

        HasLoadedVideo = false;
        LoadedVideoPath = string.Empty;
        LoadedVideoTitle = string.Empty;
        TotalDuration = TimeSpan.Zero;
        TrimStart = TimeSpan.Zero;
        TrimEnd = TimeSpan.Zero;
        _pendingCutStart = null;
        OnPropertyChanged(nameof(HasPendingCutStart));
        CutSegments.Clear();
        Bookmarks.Clear();
        OnPropertyChanged(nameof(BookmarksEmptyVisibility));
        AudioGainPercent = 100.0;
        HasUnsavedChanges = false;
    }
}
