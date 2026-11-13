using CommunityToolkit.Mvvm.ComponentModel;
using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.Models;
using Com.VibeCode.CourseRec.Services;

namespace Com.VibeCode.CourseRec.ViewModels;

/// <summary>
/// 主視窗導覽與關閉防呆狀態 ViewModel。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly INavigationCoordinator _navigationCoordinator;
    private readonly ILocalizationService _localization;

    [ObservableProperty]
    public partial NavigationPageKey CurrentPage { get; set; } = NavigationPageKey.Recorder;

    [ObservableProperty]
    public partial bool IsRecordingActive { get; set; }

    [ObservableProperty]
    public partial bool HasUnsavedEditorChanges { get; set; }

    public MainViewModel(
        INavigationCoordinator navigationCoordinator,
        ILocalizationService localization)
    {
        _navigationCoordinator = navigationCoordinator;
        _localization = localization;
        _navigationCoordinator.NavigationRequested += (_, pageKey) => CurrentPage = pageKey;
    }

    public string WindowTitle => _localization.GetString("AppDisplayName");
}
