using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Interop;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services.Audio;
using Com.VibeCode.CourseRec.Services.Bookmarks;
using Com.VibeCode.CourseRec.Services.Capture;
using Com.VibeCode.CourseRec.Services.Encoding;
using Com.VibeCode.CourseRec.Services.Hotkeys;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;

namespace Com.VibeCode.CourseRec.ViewModels;

/// <summary>
/// 錄製工作台完整狀態機 ViewModel（整合螢幕／視窗擷取、雙軌混音、硬體編碼、暫停校正、書籤、快拍與懸浮列）。
/// </summary>
public sealed partial class RecorderViewModel : ObservableObject, IDisposable
{
    private const long MinFreeDiskBytes = 512L * 1024L * 1024L;

    private readonly ICaptureEnumerationService _enumerationService;
    private readonly IScreenCaptureService _screenCaptureService;
    private readonly IAudioCaptureService _audioCaptureService;
    private readonly IMp4EncoderService _mp4EncoderService;
    private readonly IBookmarkService _bookmarkService;
    private readonly ISettingsService _settingsService;
    private readonly IGlobalHotkeyService _hotkeyService;
    private readonly MainViewModel _mainViewModel;
    private readonly ILocalizationService _localization;
    private readonly ILogger<RecorderViewModel> _logger;
    private readonly RecordingTimelineClock _timelineClock = new();
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly ITaskbarIndicatorService _taskbarIndicator;
    private readonly ManualResetEventSlim _pumpResumeGate = new(initialState: true);

    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _recordingSessionCts;
    private Task? _encodingPumpTask;
    private Exception? _encodingFailure;
    private Task _bookmarkSaveTask = Task.CompletedTask;
    private bool _bookmarkSaveDirty;
    private bool _bookmarkSaveWarningShown;
    private long _lastBookmarkSaveTicks;
    private Timer? _housekeepingTimer;
    private bool _isInitialized;
    private bool _isDisposed;
    private bool _isApplyingDefaults;
    private bool _isKeepAwakeActive;
    private bool _refreshTargetsRequested;
    private volatile bool _isIdleCaptureSuspended;
    private long _lastDiskSummaryUpdateTicks;
    private long _lastLowDiskCheckTicks;
    private int _diskSummaryUpdateInFlight;

    [ObservableProperty]
    public partial RecordingState State { get; set; } = RecordingState.Idle;

    [ObservableProperty]
    public partial ScenarioMode CurrentScenario { get; set; } = ScenarioMode.ListenOnly;

    [ObservableProperty]
    public partial SelectOption<QualityPreset>? SelectedQualityOption { get; set; }

    [ObservableProperty]
    public partial SelectOption<CaptureTargetKind>? SelectedCaptureKindOption { get; set; }

    [ObservableProperty]
    public partial CaptureTarget? SelectedTarget { get; set; }

    [ObservableProperty]
    public partial bool IsolateWindowAudio { get; set; } = true;

    [ObservableProperty]
    public partial bool IsLivePreviewEnabled { get; set; }

    [ObservableProperty]
    public partial bool IsRefreshingTargets { get; set; }

    [ObservableProperty]
    public partial string StartRecordingButtonText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StartRecordingAutomationName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SelectOption<int>? SelectedAutoStopOption { get; set; }

    [ObservableProperty]
    public partial string StatusBadgeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ElapsedTimeText { get; set; } = "00:00:00";

    [ObservableProperty]
    public partial string AutoStopRemainingText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Visibility AutoStopRemainingVisibility { get; set; } = Visibility.Collapsed;

    [ObservableProperty]
    public partial double SystemMeterPercent { get; set; }

    [ObservableProperty]
    public partial double MicrophoneMeterPercent { get; set; }

    [ObservableProperty]
    public partial string MicrophoneStatusHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SystemAudioSourceText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DiskSpaceSummaryText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BookmarkNoteInput { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMinimizedWarningOpen { get; set; }

    [ObservableProperty]
    public partial bool IsInfoBannerOpen { get; set; }

    [ObservableProperty]
    public partial bool IsErrorBanner { get; set; }

    [ObservableProperty]
    public partial string InfoBannerMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity BannerSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompletedRecordingVisibility))]
    public partial string CompletedRecordingPath { get; set; } = string.Empty;

    public RecorderViewModel(
        ICaptureEnumerationService enumerationService,
        IScreenCaptureService screenCaptureService,
        IAudioCaptureService audioCaptureService,
        IMp4EncoderService mp4EncoderService,
        IBookmarkService bookmarkService,
        ISettingsService settingsService,
        IGlobalHotkeyService hotkeyService,
        MainViewModel mainViewModel,
        ILocalizationService localization,
        ILogger<RecorderViewModel> logger,
        INavigationCoordinator navigationCoordinator,
        ITaskbarIndicatorService taskbarIndicator)
    {
        _enumerationService = enumerationService;
        _screenCaptureService = screenCaptureService;
        _audioCaptureService = audioCaptureService;
        _mp4EncoderService = mp4EncoderService;
        _bookmarkService = bookmarkService;
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _mainViewModel = mainViewModel;
        _localization = localization;
        _logger = logger;
        _navigationCoordinator = navigationCoordinator;
        _taskbarIndicator = taskbarIndicator;

        QualityOptions =
        [
            new SelectOption<QualityPreset>(QualityPreset.SlidePresentation, _localization.GetString("QualityPresetSlideText")),
            new SelectOption<QualityPreset>(QualityPreset.StandardSmooth, _localization.GetString("QualityPresetSmoothText")),
        ];

        CaptureKindOptions =
        [
            new SelectOption<CaptureTargetKind>(CaptureTargetKind.Monitor, _localization.GetString("CaptureKindMonitorText")),
            new SelectOption<CaptureTargetKind>(CaptureTargetKind.Window, _localization.GetString("CaptureKindWindowText")),
        ];

        AutoStopOptions =
        [
            new SelectOption<int>(0, _localization.GetString("AutoStopOffText")),
            new SelectOption<int>(5, _localization.Format("AutoStopMinutesFormat", 5)),
            new SelectOption<int>(10, _localization.Format("AutoStopMinutesFormat", 10)),
            new SelectOption<int>(15, _localization.Format("AutoStopMinutesFormat", 15)),
            new SelectOption<int>(20, _localization.Format("AutoStopMinutesFormat", 20)),
            new SelectOption<int>(30, _localization.Format("AutoStopMinutesFormat", 30)),
            new SelectOption<int>(60, _localization.Format("AutoStopMinutesFormat", 60)),
            new SelectOption<int>(90, _localization.Format("AutoStopMinutesFormat", 90)),
            new SelectOption<int>(120, _localization.Format("AutoStopMinutesFormat", 120)),
            new SelectOption<int>(180, _localization.Format("AutoStopMinutesFormat", 180)),
        ];

        SelectedQualityOption = QualityOptions[0];
        SelectedCaptureKindOption = CaptureKindOptions[0];
        SelectedAutoStopOption = AutoStopOptions[0];
        StatusBadgeText = _localization.GetString("StateIdleText");
        MinimizedWarningTitle = _localization.GetString("RecorderMinimizedWarningTitle");
        MinimizedWarningMessage = _localization.GetString("RecorderMinimizedWarningMessage");
        UpdateStartRecordingButtonText(_settingsService.Current);

        _screenCaptureService.PreviewFrameAvailable += OnScreenPreviewFrameAvailable;
        _screenCaptureService.TargetMinimizedChanged += OnTargetMinimizedChanged;
        _screenCaptureService.TargetClosed += OnTargetClosed;
        _audioCaptureService.AudioLevelsUpdated += OnAudioLevelsUpdated;
        _audioCaptureService.CaptureStatusChanged += OnCaptureStatusChanged;
        _audioCaptureService.ProcessAudioFallbackTriggered += OnProcessAudioFallbackTriggered;
        _hotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        _settingsService.SettingsChanged += OnSettingsChanged;
        UpdateAudioStatus();
    }

    public event EventHandler<CapturedFrameData>? PreviewFrameArrived;

    public event EventHandler? ToggleMiniOverlayRequested;

    public event EventHandler? CloseMiniOverlayRequested;

    public ObservableCollection<SelectOption<QualityPreset>> QualityOptions { get; }

    public ObservableCollection<SelectOption<CaptureTargetKind>> CaptureKindOptions { get; }

    public ObservableCollection<SelectOption<int>> AutoStopOptions { get; }

    public RangeObservableCollection<CaptureTarget> AvailableTargets { get; } = [];

    public ObservableCollection<BookmarkItem> SessionBookmarks { get; } = [];

    public string MinimizedWarningTitle { get; }

    public string MinimizedWarningMessage { get; }

    public bool CanChangeSource => State == RecordingState.Idle;

    public Visibility IdleIndicatorVisibility => State == RecordingState.Idle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ActiveIndicatorVisibility => State == RecordingState.Recording ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PausedIndicatorVisibility => State is RecordingState.Paused or RecordingState.Countdown ? Visibility.Visible : Visibility.Collapsed;
    public bool IsFinalizing => State == RecordingState.Finalizing;
    public Visibility FinalizingVisibility => IsFinalizing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CompletedRecordingVisibility => string.IsNullOrEmpty(CompletedRecordingPath) ? Visibility.Collapsed : Visibility.Visible;
    public string PauseResumeText => _localization.GetString(State == RecordingState.Paused ? "ResumeRecordingText" : "PauseRecordingText");
    public Symbol PauseResumeSymbol => State == RecordingState.Paused ? Symbol.Play : Symbol.Pause;
    public string StopButtonText => _localization.GetString(State == RecordingState.Countdown ? "CancelCountdownText" : "StopRecordingText");

    public bool CanStart => State == RecordingState.Idle;

    public bool CanRefreshTargets => State == RecordingState.Idle && !IsRefreshingTargets;

    public bool CanPauseOrResume => State is RecordingState.Recording or RecordingState.Paused;

    public bool CanStop => State is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused;

    public bool CanBookmark => State is RecordingState.Recording or RecordingState.Paused;

    public bool IsWindowTargetSelected => SelectedCaptureKindOption?.Value == CaptureTargetKind.Window;

    public Visibility WindowTargetHintVisibility => IsWindowTargetSelected ? Visibility.Visible : Visibility.Collapsed;

    public bool IsListenOnlySelected => CurrentScenario == ScenarioMode.ListenOnly;

    public bool IsMeetingSelected => CurrentScenario == ScenarioMode.InteractiveMeeting;

    public string LivePreviewStateText => _localization.GetString(
        IsLivePreviewEnabled ? "LivePreviewOnText" : "LivePreviewOffText");

    public string LivePreviewAutomationName => _localization.GetString("LivePreviewAutomationName");

    public string HotkeySummaryText => _localization.Format(
        "RecorderHotkeySummaryFormat",
        _settingsService.Current.ToggleStartStopHotkey,
        _settingsService.Current.TogglePauseHotkey,
        _settingsService.Current.AddBookmarkHotkey,
        _settingsService.Current.TakeSnapshotHotkey);

    public void AttachDispatcher(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        ApplyDefaultsFromSettings(_settingsService.Current);
        _screenCaptureService.IsPreviewEnabled = IsLivePreviewEnabled;
        _ = RefreshTargetsAsync();
        UpdateDiskSpaceSummary();
        _housekeepingTimer = new Timer(OnHousekeepingTick, null, 1500, 1500);
    }

    /// <summary>
    /// 離開錄影頁時暫停閒置中的即時預覽與音訊監控，避免背景持續消耗 GPU／CPU 與音訊裝置。
    /// 錄影中僅關閉預覽縮圖，不中斷擷取與監控。
    /// </summary>
    public void SuspendIdleCapture()
    {
        _isIdleCaptureSuspended = true;
        _screenCaptureService.IsPreviewEnabled = false;

        if (State != RecordingState.Idle)
        {
            return;
        }

        _screenCaptureService.StopCapture();
        _ = _audioCaptureService.StopMonitoringAsync();
        SystemMeterPercent = 0;
        MicrophoneMeterPercent = 0;
    }

    /// <summary>
    /// 回到錄影頁時恢復閒置預覽與音訊監控。
    /// </summary>
    public void ResumeIdleCapture()
    {
        if (!_isInitialized)
        {
            return;
        }

        _isIdleCaptureSuspended = false;
        _screenCaptureService.IsPreviewEnabled = IsLivePreviewEnabled;

        if (State != RecordingState.Idle)
        {
            return;
        }

        RestartPreviewAndAudioMonitor();
        UpdateDiskSpaceSummary();
    }

    partial void OnStateChanged(RecordingState value)
    {
        OnPropertyChanged(nameof(IdleIndicatorVisibility));
        OnPropertyChanged(nameof(ActiveIndicatorVisibility));
        OnPropertyChanged(nameof(PausedIndicatorVisibility));
        OnPropertyChanged(nameof(IsFinalizing));
        OnPropertyChanged(nameof(FinalizingVisibility));
        OnPropertyChanged(nameof(PauseResumeText));
        OnPropertyChanged(nameof(PauseResumeSymbol));
        OnPropertyChanged(nameof(StopButtonText));
        OnPropertyChanged(nameof(CanChangeSource));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanRefreshTargets));
        RefreshTargetsCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanPauseOrResume));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanBookmark));

        _mainViewModel.IsRecordingActive = value is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused;
        _taskbarIndicator.SetRecording(value is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused);

        StatusBadgeText = value switch
        {
            RecordingState.Idle => _localization.GetString("StateIdleText"),
            RecordingState.Recording => _localization.GetString("StateRecordingText"),
            RecordingState.Paused => _localization.GetString("StatePausedText"),
            RecordingState.Finalizing => _localization.GetString("StateFinalizingText"),
            _ => StatusBadgeText,
        };

        if (value is RecordingState.Recording or RecordingState.Paused)
        {
            UpdateAutoStopRemaining(_timelineClock.ElapsedActiveTime);
        }
        else
        {
            AutoStopRemainingText = string.Empty;
            AutoStopRemainingVisibility = Visibility.Collapsed;
        }
    }

    partial void OnSelectedAutoStopOptionChanged(SelectOption<int>? value)
    {
        if (State is RecordingState.Recording or RecordingState.Paused)
        {
            UpdateAutoStopRemaining(_timelineClock.ElapsedActiveTime);
        }
        else
        {
            AutoStopRemainingText = string.Empty;
            AutoStopRemainingVisibility = Visibility.Collapsed;
        }

        PersistRecordingPreferences();
    }

    private void UpdateAutoStopRemaining(TimeSpan elapsed)
    {
        int minutes = SelectedAutoStopOption?.Value ?? 0;
        if (minutes <= 0)
        {
            AutoStopRemainingText = string.Empty;
            AutoStopRemainingVisibility = Visibility.Collapsed;
            return;
        }

        TimeSpan remaining = TimeSpan.FromMinutes(minutes) - elapsed;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        AutoStopRemainingText = _localization.Format(
            "AutoStopRemainingFormat",
            FormatHelper.FormatDuration(remaining));
        AutoStopRemainingVisibility = Visibility.Visible;
    }

    partial void OnCurrentScenarioChanged(ScenarioMode value)
    {
        OnPropertyChanged(nameof(IsListenOnlySelected));
        OnPropertyChanged(nameof(IsMeetingSelected));
        bool muteMic = value == ScenarioMode.ListenOnly;
        _audioCaptureService.MuteMicrophone = muteMic;
        UpdateAudioStatus();
        if (muteMic)
        {
            MicrophoneMeterPercent = 0;
        }

        PersistRecordingPreferences();
    }

    partial void OnIsLivePreviewEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(LivePreviewStateText));
        _screenCaptureService.IsPreviewEnabled = value;
        if (_isInitialized)
        {
            SyncScreenCaptureState();
        }
    }

    partial void OnIsRefreshingTargetsChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRefreshTargets));
        RefreshTargetsCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedCaptureKindOptionChanged(SelectOption<CaptureTargetKind>? value)
    {
        OnPropertyChanged(nameof(IsWindowTargetSelected));
        OnPropertyChanged(nameof(WindowTargetHintVisibility));
        if (_isInitialized && State == RecordingState.Idle)
        {
            _ = RefreshTargetsAsync();
        }
    }

    partial void OnSelectedTargetChanged(CaptureTarget? value)
    {
        if (!_isInitialized || State != RecordingState.Idle)
        {
            return;
        }

        RestartPreviewAndAudioMonitor();
        UpdateDiskSpaceSummary();
    }

    partial void OnIsolateWindowAudioChanged(bool value)
    {
        if (!_isInitialized)
        {
            return;
        }

        uint? pid = GetDesiredIsolatedProcessId();
        _audioCaptureService.UpdateSystemSource(pid);
    }

    partial void OnSelectedQualityOptionChanged(SelectOption<QualityPreset>? value)
    {
        if (_isInitialized)
        {
            UpdateDiskSpaceSummary();
        }

        PersistRecordingPreferences();
    }

    [RelayCommand]
    private void SelectListenOnly()
    {
        CurrentScenario = ScenarioMode.ListenOnly;
    }

    [RelayCommand]
    private void SelectInteractiveMeeting()
    {
        CurrentScenario = ScenarioMode.InteractiveMeeting;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshTargets))]
    private async Task RefreshTargetsAsync()
    {
        if (State != RecordingState.Idle)
        {
            return;
        }

        if (IsRefreshingTargets)
        {
            _refreshTargetsRequested = true;
            return;
        }

        IsRefreshingTargets = true;
        try
        {
            CaptureTargetKind kind = SelectedCaptureKindOption?.Value ?? CaptureTargetKind.Monitor;
            string? previousId = SelectedTarget?.Id;
            IReadOnlyList<CaptureTarget> targets = await Task.Run(
                () => kind == CaptureTargetKind.Monitor
                    ? _enumerationService.GetMonitors()
                    : _enumerationService.GetCapturableWindows());

            if (SelectedCaptureKindOption?.Value != kind)
            {
                _refreshTargetsRequested = true;
                return;
            }

            if (State != RecordingState.Idle)
            {
                return;
            }

            AvailableTargets.ReplaceAll(targets);

            SelectedTarget = AvailableTargets.FirstOrDefault(t => t.Id == previousId)
                ?? AvailableTargets.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重新整理擷取來源失敗");
            ShowBanner(_localization.GetString("RecorderRefreshSourcesFailedText"), isError: true);
        }
        finally
        {
            IsRefreshingTargets = false;
            if (_refreshTargetsRequested)
            {
                _refreshTargetsRequested = false;
                _ = RefreshTargetsAsync();
            }
        }
    }

    [RelayCommand]
    private async Task StartRecordingAsync()
    {
        if (State != RecordingState.Idle)
        {
            return;
        }

        if (SelectedTarget is null)
        {
            ShowBanner(_localization.GetString("ErrorNoCaptureTarget"), isError: true);
            return;
        }

        string outputDir = _settingsService.Current.OutputDirectory;
        try
        {
            Directory.CreateDirectory(outputDir);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "建立錄影輸出資料夾失敗：{Directory}", outputDir);
            ShowBanner(_localization.GetString("OutputDirectoryInvalidText"), isError: true);
            return;
        }

        if (GetAvailableFreeBytes(outputDir) < MinFreeDiskBytes)
        {
            ShowBanner(_localization.GetString("ErrorLowDiskSpace"), isError: true);
            return;
        }

        _screenCaptureService.MinCaptureIntervalTicks = TimeSpan.TicksPerMillisecond * 33;
        if ((!_screenCaptureService.IsCapturing || _screenCaptureService.ActiveTarget?.Id != SelectedTarget.Id)
            && !_screenCaptureService.StartCapture(SelectedTarget))
        {
            ShowBanner(_localization.GetString("ErrorNoCaptureTarget"), isError: true);
            return;
        }

        if (!_audioCaptureService.IsMonitoring)
        {
            _audioCaptureService.MuteMicrophone = CurrentScenario == ScenarioMode.ListenOnly;
            _audioCaptureService.StartMonitoring(GetDesiredIsolatedProcessId());
        }

        _recordingSessionCts?.Dispose();
        _recordingSessionCts = new CancellationTokenSource();
        CancellationToken token = _recordingSessionCts.Token;

        try
        {
            SetKeepAwake(true);
            State = RecordingState.Countdown;
            int countdownSeconds = Math.Clamp(_settingsService.Current.RecordingCountdownSeconds, 0, 30);
            for (int sec = countdownSeconds; sec >= 1; sec--)
            {
                StatusBadgeText = _localization.Format("StateCountdownFormat", sec);
                await Task.Delay(1000, token);
            }

            QualityPreset preset = SelectedQualityOption?.Value ?? QualityPreset.SlidePresentation;
            using CapturedFrameData? latestFrame = _screenCaptureService.GetLatestFrameSnapshot();
            int srcW = latestFrame?.Width ?? SelectedTarget.Width;
            int srcH = latestFrame?.Height ?? SelectedTarget.Height;
            RecordingProfile profile = RecordingProfile.Create(preset, srcW, srcH);
            _screenCaptureService.MinCaptureIntervalTicks = TimeSpan.TicksPerSecond / Math.Max(1, profile.FrameRate);

            string prefix = FileNameHelper.SanitizePrefix(_settingsService.Current.RecordingFileNamePrefix);
            string fileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
            string fullOutputPath = Path.Combine(outputDir, fileName);

            SessionBookmarks.Clear();
            CompletedRecordingPath = string.Empty;
            _encodingFailure = null;
            _bookmarkSaveDirty = true;
            _bookmarkSaveWarningShown = false;
            _lastBookmarkSaveTicks = 0;
            ElapsedTimeText = "00:00:00";

            token.ThrowIfCancellationRequested();
            try
            {
                await Task.Run(() => _mp4EncoderService.Open(fullOutputPath, profile, srcW, srcH));
                token.ThrowIfCancellationRequested();
            }
            catch
            {
                try
                {
                    await Task.Run(() => _mp4EncoderService.FinalizeRecording());
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(cleanupException, "啟動取消後清理編碼器失敗。");
                }

                throw;
            }

            _audioCaptureService.ResetEncodingBuffers();
            _audioCaptureService.IsBufferingForEncoding = true;
            _timelineClock.Start();
            _pumpResumeGate.Set();
            State = RecordingState.Recording;

            _encodingPumpTask = Task.Factory.StartNew(
                () => RunEncodingPumpLoop(profile, token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            _ = ObserveEncodingPumpAsync(_encodingPumpTask, _recordingSessionCts);
        }
        catch (OperationCanceledException)
        {
            SetKeepAwake(false);
            State = RecordingState.Idle;
            ElapsedTimeText = "00:00:00";
            SyncScreenCaptureState();
        }
        catch (Exception ex)
        {
            SetKeepAwake(false);
            _logger.LogError(ex, "啟動硬體編碼錄影失敗");
            _audioCaptureService.IsBufferingForEncoding = false;
            try
            {
                await Task.Run(() => _mp4EncoderService.FinalizeRecording());
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException, "啟動失敗後清理編碼器失敗。");
            }
            _timelineClock.Reset();
            State = RecordingState.Idle;
            SyncScreenCaptureState();
            ShowBanner(_localization.GetString("RecordingStartFailedText"), isError: true);
        }
    }

    [RelayCommand]
    private void TogglePauseResume()
    {
        if (State == RecordingState.Recording)
        {
            _pumpResumeGate.Reset();
            _timelineClock.Pause();
            _audioCaptureService.IsBufferingForEncoding = false;
            State = RecordingState.Paused;
            SyncScreenCaptureState();
        }
        else if (State == RecordingState.Paused)
        {
            _timelineClock.Resume();
            _audioCaptureService.IsBufferingForEncoding = true;
            _pumpResumeGate.Set();
            State = RecordingState.Recording;
            SyncScreenCaptureState();
        }
    }

    [RelayCommand]
    public async Task StopRecordingAsync()
    {
        if (State == RecordingState.Idle || State == RecordingState.Finalizing)
        {
            return;
        }

        if (State == RecordingState.Countdown)
        {
            _recordingSessionCts?.Cancel();
            SetKeepAwake(false);
            State = RecordingState.Idle;
            SyncScreenCaptureState();
            ShowBanner(_localization.GetString("CountdownCancelledText"), isError: false, InfoBarSeverity.Informational);
            return;
        }

        State = RecordingState.Finalizing;
        _audioCaptureService.IsBufferingForEncoding = false;
        _timelineClock.Pause();
        TimeSpan finalDuration = _timelineClock.ElapsedActiveTime;
        string? savedVideoPath = _mp4EncoderService.OutputFilePath;

        if (_recordingSessionCts is not null)
        {
            await _recordingSessionCts.CancelAsync();
        }

        _pumpResumeGate.Set();

        if (_encodingPumpTask is not null)
        {
            try
            {
                await _encodingPumpTask;
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _encodingFailure, ex, null);
                _logger.LogWarning(ex, "等待編碼迴圈結束時發生例外");
            }

            _encodingPumpTask = null;
        }

        try
        {
            Exception? finalizeFailure = null;
            try
            {
                await Task.Run(() => _mp4EncoderService.FinalizeRecording());
            }
            catch (Exception ex)
            {
                finalizeFailure = ex;
                _logger.LogError(ex, "MP4 封裝失敗，仍嘗試保存書籤。");
            }

            await _bookmarkSaveTask;

            if (!string.IsNullOrEmpty(savedVideoPath)
                && File.Exists(savedVideoPath)
                && (SessionBookmarks.Count > 0 || finalDuration > TimeSpan.Zero))
            {
                await _bookmarkService.SaveForVideoAsync(
                    savedVideoPath,
                    finalDuration,
                    SessionBookmarks.ToList());
            }

            if (finalizeFailure is not null)
            {
                ShowBanner(_localization.Format("RecordingFinalizeFailedText", savedVideoPath ?? string.Empty), isError: true);
            }
            else if (_encodingFailure is not null)
            {
                ShowBanner(_localization.Format("RecordingPartialFailureText", savedVideoPath ?? string.Empty), isError: true);
            }
            else if (!string.IsNullOrEmpty(savedVideoPath) && File.Exists(savedVideoPath)
                && new FileInfo(savedVideoPath).Length > 0)
            {
                CompletedRecordingPath = savedVideoPath;
                ShowBanner(_localization.Format("RecordingSavedStatusFormat", Path.GetFileName(savedVideoPath)), isError: false, InfoBarSeverity.Success);
            }
            else
            {
                ShowBanner(_localization.GetString("RecordingOutputMissingText"), isError: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "安全封裝 MP4 時發生例外");
            ShowBanner(_localization.Format("RecordingFinalizeFailedText", savedVideoPath ?? string.Empty), isError: true);
        }
        finally
        {
            SetKeepAwake(false);
            _timelineClock.Reset();
            State = RecordingState.Idle;
            SyncScreenCaptureState();
            if (_isIdleCaptureSuspended)
            {
                await _audioCaptureService.StopMonitoringAsync();
            }

            CloseMiniOverlayRequested?.Invoke(this, EventArgs.Empty);
            UpdateDiskSpaceSummary();
        }
    }

    [RelayCommand]
    private void OpenCompletedRecording()
    {
        if (File.Exists(CompletedRecordingPath))
        {
            _navigationCoordinator.OpenInEditor(CompletedRecordingPath);
        }
        else
        {
            ShowBanner(_localization.GetString("ResultFileMissingText"), isError: true);
        }
    }

    [RelayCommand]
    private void RevealCompletedRecording()
    {
        try
        {
            FileActionHelper.Reveal(CompletedRecordingPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "顯示完成錄影失敗");
            ShowBanner(_localization.GetString("ResultFileActionFailedText"), isError: true);
        }
    }

    [RelayCommand]
    private void AddBookmark()
    {
        if (!CanBookmark)
        {
            return;
        }

        long ticks = _timelineClock.GetElapsedActiveTicks();
        string rawNote = BookmarkNoteInput.Trim();
        string note = string.IsNullOrEmpty(rawNote)
            ? _localization.Format("DefaultBookmarkNoteFormat", SessionBookmarks.Count + 1)
            : rawNote;

        var item = new BookmarkItem(Guid.NewGuid(), ticks, note, DateTimeOffset.UtcNow);
        SessionBookmarks.Add(item);
        _bookmarkSaveDirty = true;
        BookmarkNoteInput = string.Empty;

        ShowBanner(_localization.Format("BookmarkAddedStatusFormat", item.FormattedTimestamp, item.Note), isError: false, InfoBarSeverity.Success);
    }

    public void RemoveBookmark(BookmarkItem bookmark)
    {
        ArgumentNullException.ThrowIfNull(bookmark);
        if (!CanBookmark)
        {
            ShowBanner(_localization.GetString("RecordingBookmarkRemoveNotActiveText"), isError: false, InfoBarSeverity.Informational);
            return;
        }

        if (!SessionBookmarks.Remove(bookmark))
        {
            return;
        }

        _bookmarkSaveDirty = true;
        ShowBanner(
            _localization.Format("RecordingBookmarkRemovedText", bookmark.FormattedTimestamp),
            isError: false,
            InfoBarSeverity.Informational);
    }

    [RelayCommand]
    private async Task TakeSnapshotAsync()
    {
        bool startedTemporarily = false;
        try
        {
            if (!_screenCaptureService.IsCapturing && SelectedTarget is not null)
            {
                _screenCaptureService.MinCaptureIntervalTicks = TimeSpan.TicksPerMillisecond * 16;
                startedTemporarily = _screenCaptureService.StartCapture(SelectedTarget);
                if (startedTemporarily)
                {
                    for (int attempt = 0; attempt < 16; attempt++)
                    {
                        using CapturedFrameData? available = _screenCaptureService.GetLatestFrameSnapshot();
                        if (available is not null)
                        {
                            break;
                        }

                        await Task.Delay(16);
                    }
                }
            }

            string outputDir = _settingsService.Current.OutputDirectory;
            string? savedPath = await _screenCaptureService.SaveSnapshotPngAsync(outputDir, copyToClipboard: true);
            if (!string.IsNullOrEmpty(savedPath))
            {
                ShowBanner(_localization.Format("SnapshotSavedStatusFormat", Path.GetFileName(savedPath)), isError: false, InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "投影片快拍失敗");
            ShowBanner(_localization.GetString("SnapshotFailedText"), isError: true);
        }
        finally
        {
            if (startedTemporarily)
            {
                SyncScreenCaptureState();
            }
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
            ShowBanner(_localization.GetString("SnapshotFolderOpenFailedText"), isError: true);
        }
    }

    [RelayCommand]
    private void ToggleMiniOverlay()
    {
        ToggleMiniOverlayRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        SetKeepAwake(false);
        _housekeepingTimer?.Dispose();
        _pumpResumeGate.Set();
        _recordingSessionCts?.Cancel();
        _recordingSessionCts?.Dispose();
        _screenCaptureService.PreviewFrameAvailable -= OnScreenPreviewFrameAvailable;
        _screenCaptureService.TargetMinimizedChanged -= OnTargetMinimizedChanged;
        _screenCaptureService.TargetClosed -= OnTargetClosed;
        _audioCaptureService.AudioLevelsUpdated -= OnAudioLevelsUpdated;
        _audioCaptureService.CaptureStatusChanged -= OnCaptureStatusChanged;
        _audioCaptureService.ProcessAudioFallbackTriggered -= OnProcessAudioFallbackTriggered;
        _hotkeyService.HotkeyPressed -= OnGlobalHotkeyPressed;
        _settingsService.SettingsChanged -= OnSettingsChanged;
    }

    private void RunEncodingPumpLoop(RecordingProfile profile, CancellationToken cancellationToken)
    {
        long nextFrameIndex = 0;
        byte[] fallbackBlackBgra = new byte[profile.Width * profile.Height * 4];
        int maxAudioFramesPerStep = (48_000 / Math.Max(1, profile.FrameRate)) + 64;
        short[] audioStepPcm = new short[maxAudioFramesPerStep * 2];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _pumpResumeGate.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            long activeTicks = _timelineClock.GetElapsedActiveTicks();
            long nextVideoTime100Ns = RecordingFrameTiming.GetFrameStart100Ns(nextFrameIndex, profile.FrameRate);
            long frameDuration100Ns = RecordingFrameTiming.GetFrameDuration100Ns(nextFrameIndex, profile.FrameRate);

            if (activeTicks < nextVideoTime100Ns)
            {
                int sleepMs = (int)Math.Clamp((nextVideoTime100Ns - activeTicks) / TimeSpan.TicksPerMillisecond, 1L, 15L);
                cancellationToken.WaitHandle.WaitOne(sleepMs);
                continue;
            }

            using CapturedFrameData? frame = _screenCaptureService.GetLatestFrameSnapshot();
            if (frame is not null)
            {
                _mp4EncoderService.WriteVideoFrameBgra(
                    frame.BgraPixels,
                    frame.Width,
                    frame.Height,
                    nextVideoTime100Ns,
                    frameDuration100Ns);
            }
            else
            {
                _mp4EncoderService.WriteVideoFrameBgra(
                    fallbackBlackBgra,
                    profile.Width,
                    profile.Height,
                    nextVideoTime100Ns,
                    frameDuration100Ns);
            }

            long audioFramesBefore = (nextFrameIndex * 48_000L) / profile.FrameRate;
            long audioFramesAfter = ((nextFrameIndex + 1) * 48_000L) / profile.FrameRate;
            int audioFramesCount = (int)(audioFramesAfter - audioFramesBefore);
            long audioTime100Ns = (audioFramesBefore * TimeSpan.TicksPerSecond) / 48_000L;
            long audioDur100Ns = (audioFramesCount * TimeSpan.TicksPerSecond) / 48_000L;

            Span<short> stepSpan = audioStepPcm.AsSpan(0, audioFramesCount * 2);
            _audioCaptureService.MixNextStereoFramesToPcm16(audioFramesCount, stepSpan);
            _mp4EncoderService.WriteAudioPcm16(stepSpan, audioTime100Ns, audioDur100Ns);

            nextFrameIndex++;

            if (nextFrameIndex % profile.FrameRate == 0)
            {
                string formatted = FormatHelper.FormatDuration(TimeSpan.FromTicks(activeTicks));
                EnqueueUi(() => ElapsedTimeText = formatted);
            }
        }
    }

    private void RestartPreviewAndAudioMonitor()
    {
        SyncScreenCaptureState();

        _audioCaptureService.MuteMicrophone = CurrentScenario == ScenarioMode.ListenOnly;
        _audioCaptureService.StartMonitoring(GetDesiredIsolatedProcessId());
    }

    private void SyncScreenCaptureState()
    {
        if (State == RecordingState.Recording)
        {
            int fps = Math.Max(1, _mp4EncoderService.ActiveProfile?.FrameRate ?? 30);
            _screenCaptureService.MinCaptureIntervalTicks = TimeSpan.TicksPerSecond / fps;
            if (SelectedTarget is not null
                && (!_screenCaptureService.IsCapturing || _screenCaptureService.ActiveTarget?.Id != SelectedTarget.Id))
            {
                _screenCaptureService.StartCapture(SelectedTarget);
            }

            return;
        }

        if (State == RecordingState.Countdown)
        {
            _screenCaptureService.MinCaptureIntervalTicks = TimeSpan.TicksPerMillisecond * 33;
            return;
        }

        if (State == RecordingState.Paused)
        {
            _screenCaptureService.MinCaptureIntervalTicks = IsLivePreviewEnabled
                ? TimeSpan.TicksPerMillisecond * 100
                : TimeSpan.TicksPerMillisecond * 250;
            return;
        }

        if (SelectedTarget is not null && IsLivePreviewEnabled && !_isIdleCaptureSuspended)
        {
            _screenCaptureService.MinCaptureIntervalTicks = TimeSpan.TicksPerMillisecond * 100;
            if (!_screenCaptureService.IsCapturing || _screenCaptureService.ActiveTarget?.Id != SelectedTarget.Id)
            {
                _screenCaptureService.StartCapture(SelectedTarget);
            }
        }
        else
        {
            _screenCaptureService.StopCapture();
        }
    }

    private uint? GetDesiredIsolatedProcessId()
    {
        if (SelectedTarget is { Kind: CaptureTargetKind.Window, ProcessId: > 0 } && IsolateWindowAudio)
        {
            return SelectedTarget.ProcessId;
        }

        return null;
    }

    private void ApplyDefaultsFromSettings(AppSettings settings)
    {
        _isApplyingDefaults = true;
        try
        {
            CurrentScenario = settings.DefaultScenarioMode;
            IsolateWindowAudio = settings.IsolateWindowAudioByDefault;
            IsLivePreviewEnabled = settings.IsLivePreviewEnabledByDefault;
            SelectedQualityOption = QualityOptions.FirstOrDefault(o => o.Value == settings.DefaultQualityPreset) ?? QualityOptions[0];
            SelectedAutoStopOption = AutoStopOptions.FirstOrDefault(o => o.Value == settings.DefaultAutoStopMinutes) ?? AutoStopOptions[0];
            UpdateStartRecordingButtonText(settings);
        }
        finally
        {
            _isApplyingDefaults = false;
        }
    }

    private void UpdateStartRecordingButtonText(AppSettings settings)
    {
        int countdownSeconds = Math.Clamp(settings.RecordingCountdownSeconds, 0, 30);
        StartRecordingButtonText = countdownSeconds > 0
            ? _localization.Format("BtnStartRecordingFormat", countdownSeconds)
            : _localization.GetString("BtnStartRecordingImmediate");
        StartRecordingAutomationName = countdownSeconds > 0
            ? _localization.Format("BtnStartRecordingAutomationFormat", countdownSeconds)
            : _localization.GetString("BtnStartRecordingImmediateAutomation");
    }

    private void PersistRecordingPreferences()
    {
        if (!_isInitialized || _isApplyingDefaults || _isDisposed)
        {
            return;
        }

        _ = PersistRecordingPreferencesAsync();
    }

    private async Task PersistRecordingPreferencesAsync()
    {
        try
        {
            await _settingsService.SaveRecordingPreferencesAsync(
                CurrentScenario,
                SelectedQualityOption?.Value ?? QualityPreset.SlidePresentation,
                SelectedAutoStopOption?.Value ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "儲存錄影偏好設定失敗");
        }
    }

    private void SetKeepAwake(bool active)
    {
        if (_isKeepAwakeActive == active)
        {
            return;
        }

        _isKeepAwakeActive = active;
        uint flags = active
            ? ExecutionStateNativeMethods.EsContinuous
                | ExecutionStateNativeMethods.EsSystemRequired
                | ExecutionStateNativeMethods.EsDisplayRequired
            : ExecutionStateNativeMethods.EsContinuous;
        ExecutionStateNativeMethods.SetThreadExecutionState(flags);
    }

    private void OnHousekeepingTick(object? state)
    {
        EnqueueUi(() =>
        {
            if (State is RecordingState.Recording or RecordingState.Paused)
            {
                TimeSpan elapsed = _timelineClock.ElapsedActiveTime;
                ElapsedTimeText = FormatHelper.FormatDuration(elapsed);
                UpdateAutoStopRemaining(elapsed);
                bool periodicSyncDue = SessionBookmarks.Count > 0
                    && elapsed.Ticks - _lastBookmarkSaveTicks >= TimeSpan.TicksPerMinute;
                if (_bookmarkSaveTask.IsCompleted && (_bookmarkSaveDirty || periodicSyncDue))
                {
                    string? path = _mp4EncoderService.OutputFilePath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        _bookmarkSaveDirty = false;
                        _lastBookmarkSaveTicks = elapsed.Ticks;
                        _bookmarkSaveTask = SaveRecordingBookmarksAsync(path, elapsed, SessionBookmarks.ToList());
                    }
                }

                int autoStopMinutes = SelectedAutoStopOption?.Value ?? 0;
                if (autoStopMinutes > 0 && elapsed.TotalMinutes >= autoStopMinutes)
                {
                    _ = StopRecordingAsync();
                    return;
                }

                long recordNowMs = Environment.TickCount64;
                if (recordNowMs - Volatile.Read(ref _lastLowDiskCheckTicks) >= 15000)
                {
                    Interlocked.Exchange(ref _lastLowDiskCheckTicks, recordNowMs);
                    long freeBytes = GetAvailableFreeBytes(_settingsService.Current.OutputDirectory);
                    if (freeBytes < MinFreeDiskBytes)
                    {
                        ShowBanner(_localization.GetString("ErrorLowDiskSpace"), isError: true);
                        _ = StopRecordingAsync();
                        return;
                    }
                }
            }

            long nowTicks = Environment.TickCount64;
            if (!_isIdleCaptureSuspended
                && nowTicks - Volatile.Read(ref _lastDiskSummaryUpdateTicks) >= 5000)
            {
                UpdateDiskSpaceSummary();
            }
        });
    }

    private void UpdateDiskSpaceSummary()
    {
        if (Interlocked.Exchange(ref _diskSummaryUpdateInFlight, 1) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _lastDiskSummaryUpdateTicks, Environment.TickCount64);
        string outputDir = _settingsService.Current.OutputDirectory;
        QualityPreset preset = SelectedQualityOption?.Value ?? QualityPreset.SlidePresentation;
        int w = SelectedTarget?.Width ?? 1920;
        int h = SelectedTarget?.Height ?? 1080;

        _ = Task.Run(() =>
        {
            long freeBytes = GetAvailableFreeBytes(outputDir);
            EnqueueUi(() =>
            {
                try
                {
                    RecordingProfile profile = RecordingProfile.Create(preset, w, h);
                    TimeSpan remaining = profile.EstimateRemainingDuration(freeBytes);
                    DiskSpaceSummaryText = _localization.Format(
                        "DiskSpaceSummaryFormat",
                        FormatHelper.FormatBytes(freeBytes),
                        FormatHelper.FormatDuration(remaining));
                }
                finally
                {
                    Interlocked.Exchange(ref _diskSummaryUpdateInFlight, 0);
                }
            });
        });
    }

    private async Task SaveRecordingBookmarksAsync(string videoPath, TimeSpan elapsed, IReadOnlyList<BookmarkItem> bookmarks)
    {
        try
        {
            await Task.Run(() => _bookmarkService.SaveForVideoAsync(videoPath, elapsed, bookmarks)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "錄影書籤自動保存失敗：{Path}", videoPath);
            EnqueueUi(() =>
            {
                if (_mp4EncoderService.OutputFilePath != videoPath || State == RecordingState.Idle)
                {
                    return;
                }

                _bookmarkSaveDirty = true;
                if (!_bookmarkSaveWarningShown)
                {
                    _bookmarkSaveWarningShown = true;
                    ShowBanner(_localization.GetString("RecordingBookmarkAutosaveFailedText"), isError: false, InfoBarSeverity.Warning);
                }
            });
        }
    }

    private static long GetAvailableFreeBytes(string directoryPath)
    {
        try
        {
            string fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(directoryPath) ? AppContext.BaseDirectory : directoryPath);
            string? root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    return drive.AvailableFreeSpace;
                }
            }
        }
        catch
        {
            // Ignore transient drive probe error.
        }

        return 10L * 1024L * 1024L * 1024L;
    }

    private void OnScreenPreviewFrameAvailable(object? sender, CapturedFrameData frame)
    {
        PreviewFrameArrived?.Invoke(this, frame);
    }

    private async Task ObserveEncodingPumpAsync(Task pump, CancellationTokenSource session)
    {
        try
        {
            await pump.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_recordingSessionCts, session))
            {
                return;
            }

            Interlocked.CompareExchange(ref _encodingFailure, ex, null);
            _logger.LogError(ex, "錄影編碼失敗，停止錄影並嘗試保留部分檔案。");
            EnqueueUi(() =>
            {
                if (ReferenceEquals(_recordingSessionCts, session))
                {
                    _ = StopRecordingAsync();
                }
            });
        }
    }

    private void OnTargetMinimizedChanged(object? sender, bool minimized)
    {
        EnqueueUi(() => IsMinimizedWarningOpen = minimized);
    }

    private void OnTargetClosed(object? sender, EventArgs e)
    {
        EnqueueUi(() =>
        {
            if (State is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused)
            {
                _ = StopRecordingAsync();
            }
            else
            {
                _ = RefreshTargetsAsync();
            }
        });
    }

    private void OnAudioLevelsUpdated(object? sender, AudioLevelReading reading)
    {
        if (_isIdleCaptureSuspended)
        {
            return;
        }

        EnqueueUi(() =>
        {
            SystemMeterPercent = Math.Round(reading.SystemPeak * 100.0, 1);
            MicrophoneMeterPercent = CurrentScenario == ScenarioMode.ListenOnly
                ? 0.0
                : Math.Round(reading.MicrophonePeak * 100.0, 1);
        });
    }

    private void OnCaptureStatusChanged(object? sender, AudioCaptureStatus status)
    {
        EnqueueUi(UpdateAudioStatus);
    }

    private void UpdateAudioStatus()
    {
        AudioCaptureStatus status = _audioCaptureService.CurrentStatus;
        SystemAudioSourceText = status.SystemSource switch
        {
            SystemAudioSource.TargetProcess => _localization.Format("AudioSourceProcessFormat", status.ProcessId ?? 0),
            SystemAudioSource.SystemWide => _localization.GetString(status.IsSystemAudioFallback ? "AudioSourceSystemFallbackText" : "AudioSourceSystemText"),
            SystemAudioSource.Connecting => _localization.GetString("AudioSourceConnectingText"),
            SystemAudioSource.Unavailable => _localization.GetString("AudioSourceUnavailableText"),
            _ => _localization.GetString("AudioSourceInactiveText"),
        };
        MicrophoneStatusHint = _localization.GetString(
            _audioCaptureService.MuteMicrophone ? "MicMutedByListenModeText"
            : status.IsMicrophoneAvailable ? "MicrophoneActiveText" : "MicrophoneUnavailableText");
    }

    private void OnProcessAudioFallbackTriggered(object? sender, EventArgs e)
    {
        EnqueueUi(() => ShowBanner(_localization.GetString("ProcessAudioFallbackNotice"), isError: false, InfoBarSeverity.Warning));
    }

    private void OnGlobalHotkeyPressed(object? sender, HotkeyAction action)
    {
        EnqueueUi(() =>
        {
            switch (action)
            {
                case HotkeyAction.ToggleStartStop:
                    if (CanStart)
                    {
                        _ = StartRecordingAsync();
                    }
                    else if (CanStop)
                    {
                        _ = StopRecordingAsync();
                    }

                    break;
                case HotkeyAction.TogglePauseResume:
                    if (CanPauseOrResume)
                    {
                        TogglePauseResume();
                    }

                    break;
                case HotkeyAction.AddBookmark:
                    if (CanBookmark)
                    {
                        AddBookmark();
                    }

                    break;
                case HotkeyAction.TakeSnapshot:
                    _ = TakeSnapshotAsync();
                    break;
            }
        });
    }

    private void OnSettingsChanged(object? sender, AppSettings updated)
    {
        EnqueueUi(() =>
        {
            _hotkeyService.ApplyHotkeys(updated);
            UpdateStartRecordingButtonText(updated);
            OnPropertyChanged(nameof(HotkeySummaryText));
            UpdateDiskSpaceSummary();
        });
    }

    private void ShowBanner(string message, bool isError, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        IsErrorBanner = isError;
        BannerSeverity = isError ? InfoBarSeverity.Error : severity;
        InfoBannerMessage = message;
        IsInfoBannerOpen = true;
    }

    private void EnqueueUi(DispatcherQueueHandler callback)
    {
        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(callback);
        }
        else
        {
            callback();
        }
    }
}
