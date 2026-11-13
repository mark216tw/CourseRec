using System.Collections.Concurrent;
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

namespace Com.VibeCode.CourseRec.ViewModels;

/// <summary>
/// 錄影檔案庫 ViewModel（負責掃描 MP4 與隨附書籤檔、開啟快剪複習、開啟檔案位置與刪除）。
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IBookmarkService _bookmarkService;
    private readonly IMediaEditingService _mediaEditingService;
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly ILocalizationService _localization;
    private readonly ILogger<LibraryViewModel> _logger;
    private readonly Dictionary<string, ScanCacheEntry> _scanCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsErrorOpen { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SelectOption<LibrarySortOrder>? SelectedSortOption { get; set; }

    public LibraryViewModel(
        ISettingsService settingsService,
        IBookmarkService bookmarkService,
        IMediaEditingService mediaEditingService,
        INavigationCoordinator navigationCoordinator,
        ILocalizationService localization,
        ILogger<LibraryViewModel> logger)
    {
        _settingsService = settingsService;
        _bookmarkService = bookmarkService;
        _mediaEditingService = mediaEditingService;
        _navigationCoordinator = navigationCoordinator;
        _localization = localization;
        _logger = logger;

        SortOptions =
        [
            new SelectOption<LibrarySortOrder>(LibrarySortOrder.NewestFirst, _localization.GetString("LibrarySortNewestText")),
            new SelectOption<LibrarySortOrder>(LibrarySortOrder.OldestFirst, _localization.GetString("LibrarySortOldestText")),
            new SelectOption<LibrarySortOrder>(LibrarySortOrder.NameAscending, _localization.GetString("LibrarySortNameText")),
            new SelectOption<LibrarySortOrder>(LibrarySortOrder.SizeDescending, _localization.GetString("LibrarySortSizeText")),
            new SelectOption<LibrarySortOrder>(LibrarySortOrder.DurationDescending, _localization.GetString("LibrarySortDurationText")),
        ];

        SelectedSortOption = SortOptions.FirstOrDefault(option => option.Value == _settingsService.Current.LibrarySortOrder)
            ?? SortOptions[0];
    }

    public RangeObservableCollection<RecordingLibraryItem> Recordings { get; } = [];

    public RangeObservableCollection<RecordingLibraryItem> FilteredRecordings { get; } = [];

    public ObservableCollection<SelectOption<LibrarySortOrder>> SortOptions { get; }

    public string RecordingCountText => _localization.Format("LibrarySearchCountFormat", FilteredRecordings.Count, Recordings.Count);

    public long TotalSizeBytes => Recordings.Sum(item => item.FileSizeBytes);

    public string TotalSizeText => _localization.Format(
        "LibraryTotalSizeFormat",
        FormatHelper.FormatBytes(TotalSizeBytes));

    public TimeSpan TotalDuration => TimeSpan.FromTicks(Recordings.Sum(item => item.Duration.Ticks));

    public string TotalDurationText => _localization.Format(
        "LibraryTotalDurationFormat",
        FormatHelper.FormatDuration(TotalDuration));

    public Visibility TotalSizeVisibility => !IsLoading && Recordings.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility NoSearchResultsVisibility => !IsLoading && Recordings.Count > 0 && FilteredRecordings.Count == 0
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility SearchClearVisibility => string.IsNullOrEmpty(SearchText)
        ? Visibility.Collapsed
        : Visibility.Visible;

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchClearVisibility));
        NotifyStateVisibilities();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    partial void OnSelectedSortOptionChanged(SelectOption<LibrarySortOrder>? value)
    {
        if (value is null)
        {
            return;
        }

        NotifyStateVisibilities();
        if (value.Value != _settingsService.Current.LibrarySortOrder)
        {
            _ = PersistSortOrderAsync(value.Value);
        }
    }

    private async Task PersistSortOrderAsync(LibrarySortOrder sortOrder)
    {
        try
        {
            await _settingsService.SaveLibrarySortOrderAsync(sortOrder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "儲存檔案庫排序設定失敗");
        }
    }

    public string RenameNameHeader => _localization.GetString("DialogRenameNameHeader");

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility EmptyStateVisibility => !IsLoading && Recordings.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ListVisibility => !IsLoading && FilteredRecordings.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    partial void OnIsLoadingChanged(bool value)
    {
        NotifyStateVisibilities();
    }

    [RelayCommand]
    public async Task RefreshLibraryAsync()
    {
        if (IsLoading)
        {
            return;
        }

        IsLoading = true;
        IsErrorOpen = false;
        try
        {
            string outputDir = _settingsService.Current.OutputDirectory;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                _scanCache.Clear();
                Recordings.ReplaceAll([]);
                return;
            }

            List<FileInfo> files = await Task.Run(() =>
            {
                var dirInfo = new DirectoryInfo(outputDir);
                return dirInfo.GetFiles("*.mp4", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .ToList();
            });

            var nextCache = new ConcurrentDictionary<string, ScanCacheEntry>(StringComparer.OrdinalIgnoreCase);
            using var concurrencyGate = new SemaphoreSlim(6, 6);
            Task<RecordingLibraryItem>[] scanTasks = files
                .Select(async fi =>
                {
                    await concurrencyGate.WaitAsync();
                    try
                    {
                        string bookmarkPath = _bookmarkService.GetBookmarkFilePath(fi.FullName);
                        (long bookmarkWriteTicks, long bookmarkLength) = GetFileSignature(bookmarkPath);
                        if (_scanCache.TryGetValue(fi.FullName, out ScanCacheEntry? cached)
                            && cached.FileLastWriteTicks == fi.LastWriteTimeUtc.Ticks
                            && cached.FileLength == fi.Length
                            && cached.BookmarkLastWriteTicks == bookmarkWriteTicks
                            && cached.BookmarkLength == bookmarkLength)
                        {
                            nextCache[fi.FullName] = cached;
                            return cached.Item;
                        }

                        BookmarkDocument bookmarkDoc = await _bookmarkService.LoadForVideoAsync(fi.FullName);
                        TimeSpan duration = bookmarkDoc.TotalDurationTicks > 0
                            ? TimeSpan.FromTicks(bookmarkDoc.TotalDurationTicks)
                            : await _mediaEditingService.GetVideoDurationAsync(fi.FullName);

                        int bmCount = bookmarkDoc.Bookmarks.Count;
                        string badgeText = _localization.Format("LibraryBookmarkBadgeFormat", bmCount);
                        string bookmarkSearchText = string.Join('\n', bookmarkDoc.Bookmarks.Select(bookmark => bookmark.Note));

                        var item = new RecordingLibraryItem(
                            FilePath: fi.FullName,
                            FileName: fi.Name,
                            CreatedAtLocal: new DateTimeOffset(fi.LastWriteTime),
                            Duration: duration,
                            FileSizeBytes: fi.Length,
                            BookmarkCount: bmCount,
                            BookmarkFilePath: bookmarkPath,
                            BookmarkBadgeText: badgeText,
                            BookmarkSearchText: bookmarkSearchText);
                        nextCache[fi.FullName] = new ScanCacheEntry(
                            fi.LastWriteTimeUtc.Ticks,
                            fi.Length,
                            bookmarkWriteTicks,
                            bookmarkLength,
                            item);
                        return item;
                    }
                    finally
                    {
                        concurrencyGate.Release();
                    }
                })
                .ToArray();

            RecordingLibraryItem[] scannedItems = await Task.WhenAll(scanTasks);

            _scanCache.Clear();
            foreach (KeyValuePair<string, ScanCacheEntry> entry in nextCache)
            {
                _scanCache[entry.Key] = entry.Value;
            }

            Recordings.ReplaceAll(scannedItems);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "掃描錄影檔案庫失敗");
            ErrorMessage = _localization.GetString("LibraryScanFailedText");
            IsErrorOpen = true;
        }
        finally
        {
            IsLoading = false;
            NotifyStateVisibilities();
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
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
            _logger.LogError(ex, "開啟儲存資料夾失敗");
            ErrorMessage = _localization.GetString("LibraryOpenFolderFailedText");
            IsErrorOpen = true;
        }
    }

    public void OpenInEditor(RecordingLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _navigationCoordinator.OpenInEditor(item.FilePath);
    }

    public void PlayRecording(RecordingLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            FileActionHelper.Open(item.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "以預設播放器開啟錄影失敗：{FilePath}", item.FilePath);
            ErrorMessage = _localization.GetString("ResultFileActionFailedText");
            IsErrorOpen = true;
        }
    }

    public void RevealFileInExplorer(RecordingLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!File.Exists(item.FilePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.FilePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顯示檔案位置失敗：{FilePath}", item.FilePath);
            ErrorMessage = _localization.GetString("LibraryRevealFailedText");
            IsErrorOpen = true;
        }
    }

    public async Task DeleteRecordingConfirmedAsync(RecordingLibraryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            _navigationCoordinator.RequestReleaseVideoFile(item.FilePath);

            await Task.Run(() =>
            {
                if (File.Exists(item.FilePath))
                {
                    File.Delete(item.FilePath);
                }

                if (File.Exists(item.BookmarkFilePath))
                {
                    File.Delete(item.BookmarkFilePath);
                }
            });

            _navigationCoordinator.NotifyVideoFileDeleted(item.FilePath);
            Recordings.Remove(item);
            NotifyStateVisibilities();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "刪除錄影檔失敗：{FilePath}", item.FilePath);
            ErrorMessage = _localization.GetString("LibraryDeleteFailedText");
            IsErrorOpen = true;
        }
    }

    public (string Title, string Message, string ConfirmText, string CancelText) GetDeleteDialogTexts(RecordingLibraryItem item)
    {
        return (
            _localization.GetString("DialogDeleteTitle"),
            _localization.Format("DialogDeleteMessageFormat", item.FileName),
            _localization.GetString("DialogConfirmDelete"),
            _localization.GetString("DialogCancel"));
    }

    public (string Title, string Message, string ConfirmText, string CancelText) GetRenameDialogTexts()
    {
        return (
            _localization.GetString("DialogRenameTitle"),
            _localization.GetString("DialogRenameMessage"),
            _localization.GetString("DialogRenameConfirm"),
            _localization.GetString("DialogCancel"));
    }

    public async Task<string?> RenameRecordingAsync(RecordingLibraryItem item, string requestedName)
    {
        ArgumentNullException.ThrowIfNull(item);

        string trimmedName = (requestedName ?? string.Empty).Trim();
        if (trimmedName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            trimmedName = trimmedName[..^4].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            return _localization.GetString("RenameEmptyNameMessage");
        }

        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmedName.EndsWith(' ')
            || trimmedName.EndsWith('.'))
        {
            return _localization.GetString("RenameInvalidNameMessage");
        }

        string sourcePath = item.FilePath;
        string? directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return _localization.GetString("RenameInvalidNameMessage");
        }

        string destinationPath = Path.Combine(directory, trimmedName + ".mp4");
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string sourceBookmarkPath = _bookmarkService.GetBookmarkFilePath(sourcePath);
        string destinationBookmarkPath = _bookmarkService.GetBookmarkFilePath(destinationPath);

        try
        {
            if (File.Exists(destinationPath) || File.Exists(destinationBookmarkPath))
            {
                return _localization.GetString("RenameFileExistsMessage");
            }

            _navigationCoordinator.RequestReleaseVideoFile(sourcePath);

            File.Move(sourcePath, destinationPath);
            try
            {
                if (File.Exists(sourceBookmarkPath))
                {
                    File.Move(sourceBookmarkPath, destinationBookmarkPath);
                }
            }
            catch
            {
                if (File.Exists(destinationPath) && !File.Exists(sourcePath))
                {
                    File.Move(destinationPath, sourcePath);
                }

                throw;
            }

            _navigationCoordinator.NotifyVideoFileRenamed(sourcePath, destinationPath);
            await RefreshLibraryAsync();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新命名錄影檔失敗：{FilePath}", item.FilePath);
            return _localization.GetString("LibraryRenameFailedText");
        }
    }

    public void ShowError(string message)
    {
        ErrorMessage = message;
        IsErrorOpen = true;
    }

    private void NotifyStateVisibilities()
    {
        string query = SearchText.Trim();
        IEnumerable<RecordingLibraryItem> filtered = string.IsNullOrEmpty(query)
            ? Recordings
            : Recordings.Where(item =>
                item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.BookmarkSearchText.Contains(query, StringComparison.OrdinalIgnoreCase));

        LibrarySortOrder sortOrder = SelectedSortOption?.Value ?? LibrarySortOrder.NewestFirst;
        List<RecordingLibraryItem> sorted = sortOrder switch
        {
            LibrarySortOrder.OldestFirst => filtered.OrderBy(item => item.CreatedAtLocal).ToList(),
            LibrarySortOrder.NameAscending => filtered.OrderBy(item => item.FileName, StringComparer.CurrentCultureIgnoreCase).ToList(),
            LibrarySortOrder.SizeDescending => filtered.OrderByDescending(item => item.FileSizeBytes).ToList(),
            LibrarySortOrder.DurationDescending => filtered.OrderByDescending(item => item.Duration).ToList(),
            _ => filtered.OrderByDescending(item => item.CreatedAtLocal).ToList(),
        };
        FilteredRecordings.ReplaceAll(sorted);

        OnPropertyChanged(nameof(LoadingVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
        OnPropertyChanged(nameof(ListVisibility));
        OnPropertyChanged(nameof(NoSearchResultsVisibility));
        OnPropertyChanged(nameof(RecordingCountText));
        OnPropertyChanged(nameof(TotalSizeBytes));
        OnPropertyChanged(nameof(TotalSizeText));
        OnPropertyChanged(nameof(TotalDuration));
        OnPropertyChanged(nameof(TotalDurationText));
        OnPropertyChanged(nameof(TotalSizeVisibility));
    }

    private static (long LastWriteTicks, long Length) GetFileSignature(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            return info.Exists ? (info.LastWriteTimeUtc.Ticks, info.Length) : (-1L, -1L);
        }
        catch
        {
            return (-1L, -1L);
        }
    }

    private sealed record ScanCacheEntry(
        long FileLastWriteTicks,
        long FileLength,
        long BookmarkLastWriteTicks,
        long BookmarkLength,
        RecordingLibraryItem Item);
}
