using Com.VibeCode.CourseRec.Helpers;
using Com.VibeCode.CourseRec.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Com.VibeCode.CourseRec.Views;

/// <summary>
/// 關於頁面。
/// </summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        ViewModel = App.GetService<AboutViewModel>();
        InitializeComponent();
        InfoBarAutoDismiss.Attach(StatusInfoBar);
    }

    public AboutViewModel ViewModel { get; }
}
