using System.Text;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;
using Com.VibeCode.CourseRec.Services.Audio;
using Com.VibeCode.CourseRec.Services.Bookmarks;
using Com.VibeCode.CourseRec.Services.Capture;
using Com.VibeCode.CourseRec.Services.Editing;
using Com.VibeCode.CourseRec.Services.Encoding;
using Com.VibeCode.CourseRec.Services.Hotkeys;
using Com.VibeCode.CourseRec.Services.Settings;
using Com.VibeCode.CourseRec.ViewModels;
using Com.VibeCode.CourseRec.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Com.VibeCode.CourseRec;

/// <summary>
/// 應用程式生命週期與 <see cref="IHost"/> 依賴注入啟動器。
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private CancellationTokenSource? _activationCts;
    private MainWindow? _mainWindow;
    private int _fatalErrorHandled;
    private int _isShuttingDown;

    public App()
    {
        StartupTrace.Mark("App 建構開始");
        InitializeComponent();
        StartupTrace.Mark("App XAML 資源載入完成");

        _host = new HostBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureAppConfiguration((_, config) =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.Services.AddSingleton<ILoggerProvider, FileLoggerProvider>();
            })
            .ConfigureServices((context, services) =>
            {
                services.Configure<CourseRecDefaultOptions>(
                    context.Configuration.GetSection(CourseRecDefaultOptions.SectionName));

                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<INavigationCoordinator, NavigationCoordinator>();
                services.AddSingleton<ICaptureEnumerationService, CaptureEnumerationService>();
                services.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
                services.AddSingleton<IAudioCaptureService, AudioCaptureService>();
                services.AddSingleton<IMp4EncoderService, Mp4EncoderService>();
                services.AddSingleton<IMediaEditingService, MediaEditingService>();
                services.AddSingleton<IBookmarkService, BookmarkService>();
                services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
                services.AddSingleton<ITaskbarIndicatorService, TaskbarIndicatorService>();
                services.AddSingleton<IMiniOverlayService, MiniOverlayService>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<RecorderViewModel>();
                services.AddSingleton<LibraryViewModel>();
                services.AddSingleton<EditorViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<AboutViewModel>();

                services.AddTransient<MainWindow>();
            })
            .Build();

        StartupTrace.Mark("主機建置完成");
        UnhandledException += OnAppUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        FlushStartupTrace();
    }

    public static T GetService<T>()
        where T : class
    {
        if (Current is not App app)
        {
            throw new InvalidOperationException("Application instance is not initialized.");
        }

        return app._host.Services.GetRequiredService<T>();
    }

    public MainWindow? MainWindowInstance => _mainWindow;

    public void StartActivationListener(EventWaitHandle activateEvent)
    {
        _activationCts = new CancellationTokenSource();
        CancellationToken token = _activationCts.Token;

        _ = Task.Run(
            () =>
            {
                WaitHandle[] handles = [activateEvent, token.WaitHandle];
                while (!token.IsCancellationRequested)
                {
                    int index = WaitHandle.WaitAny(handles);
                    if (index == 0)
                    {
                        _mainWindow?.DispatcherQueue.TryEnqueue(() => _mainWindow.BringToFront());
                    }
                    else
                    {
                        break;
                    }
                }
            },
            token);
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupTrace.Mark("OnLaunched 開始");
        await _host.StartAsync();
        StartupTrace.Mark("主機啟動完成");

        ISettingsService settingsService = GetService<ISettingsService>();
        AppSettings settings = await settingsService.LoadAsync();
        StartupTrace.Mark("設定載入完成");

        IThemeService themeService = GetService<IThemeService>();
        themeService.ApplyTheme(settings.DisplayMode, settings.ColorTheme);

        _mainWindow = GetService<MainWindow>();
        StartupTrace.Mark("主視窗建構完成");

        _mainWindow.BeforeForceStopRecordingAsync = () => GetService<RecorderViewModel>().StopRecordingAsync();
        GetService<EditorViewModel>().ConfirmDiscardEditsAsync = _mainWindow.ConfirmDiscardEditorChangesAsync;
        _mainWindow.Closed += OnMainWindowClosed;

        themeService.RegisterWindow(_mainWindow);
        StartupTrace.Mark("主題註冊完成");

        _mainWindow.Activate();
        StartupTrace.Mark("視窗 Activate");

        // 視窗顯示後才進行非必要初始化，縮短首次可見時間；頁面導覽同樣延後到下一輪。
        RecorderViewModel recorderViewModel = GetService<RecorderViewModel>();
        recorderViewModel.AttachDispatcher(_mainWindow.DispatcherQueue);
        recorderViewModel.StartIdleServices();
        if (settings.LastPage != NavigationPageKey.Recorder)
        {
            recorderViewModel.SuspendIdleCapture();
        }

        ClipboardHelper.Initialize(_mainWindow.DispatcherQueue);
        _mainWindow.NavigateToInitialPage(settings.LastPage);
        GetService<ITaskbarIndicatorService>().AttachWindow(_mainWindow.WindowHandle);

        IGlobalHotkeyService hotkeyService = GetService<IGlobalHotkeyService>();
        hotkeyService.AttachWindow(_mainWindow.WindowHandle);
        hotkeyService.ApplyHotkeys(settings);

        StartupTrace.Mark("啟動後初始化完成");
        FlushStartupTrace();
    }

    private void FlushStartupTrace()
    {
        try
        {
            StartupTrace.Flush(_host.Services.GetRequiredService<ILogger<App>>());
        }
        catch
        {
            // 日誌服務尚未就緒時不影響啟動流程。
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _activationCts?.Cancel();
        _activationCts?.Dispose();
        _activationCts = null;

        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
        {
            return;
        }

        try
        {
            GetService<IMiniOverlayService>().Close();
        }
        catch (Exception ex)
        {
            TryLogFatal(ex, "關閉迷你控制列");
        }

        try
        {
            // 在 UI 執行緒同步等待關閉，確保日誌佇列與錄影資源在處理序結束前完成排空。
            Task.Run(() => _host.StopAsync(TimeSpan.FromSeconds(3))).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            TryLogFatal(ex, "關閉主機");
        }
        finally
        {
            try
            {
                _host.Dispose();
            }
            catch (Exception ex)
            {
                TryLogFatal(ex, "釋放主機");
            }
        }
    }

    private void OnAppUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        HandleFatalException(e.Exception, "UI");
    }

    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            TryLogFatal(exception, "AppDomain");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLogFatal(e.Exception, "未觀察的工作");
        e.SetObserved();
    }

    private void HandleFatalException(Exception exception, string source)
    {
        TryLogFatal(exception, source);
        if (Interlocked.Exchange(ref _fatalErrorHandled, 1) != 0)
        {
            return;
        }

        Microsoft.UI.Dispatching.DispatcherQueue? dispatcherQueue = _mainWindow?.DispatcherQueue;
        if (dispatcherQueue is null
            || !dispatcherQueue.TryEnqueue(() => ShowFatalErrorDialog(exception, source)))
        {
            Exit();
        }
    }

    private async void ShowFatalErrorDialog(Exception exception, string source)
    {
        try
        {
            ILocalizationService? localization = null;
            try
            {
                localization = GetService<ILocalizationService>();
            }
            catch (Exception ex)
            {
                TryLogFatal(ex, "載入錯誤對話框資源");
            }

            string title = localization?.GetString("FatalErrorTitle") ?? "CourseRec 發生未預期的錯誤";
            string message = localization?.Format("FatalErrorMessageFormat", exception.HResult)
                ?? $"應用程式即將關閉。錯誤代碼：0x{exception.HResult:X8}";
            string copyText = localization?.GetString("FatalErrorCopyButton") ?? "複製診斷資訊";
            string closeText = localization?.GetString("FatalErrorCloseButton") ?? "關閉";

            if (_mainWindow?.Content is FrameworkElement root && root.XamlRoot is not null)
            {
                var dialog = new ContentDialog
                {
                    XamlRoot = root.XamlRoot,
                    Title = title,
                    Content = message,
                    PrimaryButtonText = copyText,
                    CloseButtonText = closeText,
                    DefaultButton = ContentDialogButton.Close,
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    try
                    {
                        var package = new DataPackage();
                        package.SetText(BuildDiagnosticText(exception, source));
                        Clipboard.SetContent(package);
                    }
                    catch (Exception ex)
                    {
                        TryLogFatal(ex, "複製診斷資訊");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TryLogFatal(ex, "顯示錯誤對話框");
        }
        finally
        {
            Exit();
        }
    }

    private static string BuildDiagnosticText(Exception exception, string source)
    {
        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CourseRec",
            "logs",
            "courserec.log");
        var builder = new StringBuilder();
        builder.AppendLine($"CourseRec {typeof(App).Assembly.GetName().Version}");
        builder.AppendLine($"來源：{source}");
        builder.AppendLine($"時間：{DateTimeOffset.Now:O}");
        builder.AppendLine($"錯誤代碼：0x{exception.HResult:X8}");
        builder.AppendLine($"日誌：{logPath}");
        builder.AppendLine();
        builder.Append(exception);
        return builder.ToString();
    }

    private void TryLogFatal(Exception exception, string source)
    {
        try
        {
            _host.Services.GetService<ILogger<App>>()?.LogCritical(exception, "未處理的例外（來源：{Source}）", source);
        }
        catch
        {
            // 主機已釋放或日誌服務不可用時，避免例外處理流程再次失敗。
        }
    }
}
