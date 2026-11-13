using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Services.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Com.VibeCode.CourseRec.ViewModels;

/// <summary>
/// 關於頁面 ViewModel。
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly ILocalizationService _localization;
    private readonly ILogger<AboutViewModel> _logger;

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Success;

    public AboutViewModel(
        ISettingsService settingsService,
        ILocalizationService localization,
        ILogger<AboutViewModel> logger)
    {
        _settingsService = settingsService;
        _localization = localization;
        _logger = logger;
        VersionDisplayText = BuildVersionDisplayText();
    }

    public string VersionDisplayText { get; }

    [RelayCommand]
    private void OpenOutputFolder() => OpenFolder(_settingsService.Current.OutputDirectory);

    [RelayCommand]
    private void OpenLogFolder()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        OpenFolder(Path.Combine(appData, "CourseRec", "logs"));
    }

    [RelayCommand]
    private void CopyVersionInfo()
    {
        try
        {
            var package = new DataPackage();
            package.SetText(VersionDisplayText);
            Clipboard.SetContent(package);
            StatusSeverity = InfoBarSeverity.Success;
            StatusMessage = _localization.GetString("AboutVersionCopiedText");
            IsStatusOpen = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "複製版本資訊失敗");
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = _localization.GetString("AboutCopyFailedText");
            IsStatusOpen = true;
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "開啟資料夾失敗：{Path}", path);
            StatusSeverity = InfoBarSeverity.Error;
            StatusMessage = _localization.GetString("AboutOpenFolderFailedText");
            IsStatusOpen = true;
        }
    }

    private string BuildVersionDisplayText()
    {
        Assembly assembly = typeof(AboutViewModel).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?
            .Split('+')[0]
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.1.0";

        string fileVersion = assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()?
            .Version
            ?? FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion
            ?? "0.1.0.0";

        return _localization.Format("AboutVersionFormat", version, fileVersion);
    }
}
