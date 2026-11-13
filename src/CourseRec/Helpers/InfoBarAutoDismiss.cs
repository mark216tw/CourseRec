using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Com.VibeCode.CourseRec.Helpers;

internal static class InfoBarAutoDismiss
{
    public static void Attach(InfoBar infoBar)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        void RestartTimer()
        {
            timer.Stop();
            if (infoBar.IsLoaded && infoBar.IsOpen && infoBar.Severity == InfoBarSeverity.Success)
            {
                timer.Start();
            }
        }

        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (infoBar.Severity == InfoBarSeverity.Success)
            {
                infoBar.IsOpen = false;
            }
        };
        infoBar.RegisterPropertyChangedCallback(InfoBar.MessageProperty, (_, _) => RestartTimer());
        infoBar.RegisterPropertyChangedCallback(InfoBar.IsOpenProperty, (_, _) => RestartTimer());
        infoBar.RegisterPropertyChangedCallback(InfoBar.SeverityProperty, (_, _) => RestartTimer());
        infoBar.Loaded += (_, _) => RestartTimer();
        infoBar.Unloaded += (_, _) => timer.Stop();
    }
}
