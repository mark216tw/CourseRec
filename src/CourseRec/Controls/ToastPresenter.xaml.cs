using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Com.VibeCode.CourseRec.Controls;

/// <summary>
/// 頁面底部浮動 Toast 訊息，顯示後自動淡出。
/// </summary>
public sealed partial class ToastPresenter : UserControl
{
    private DispatcherTimer? _hideTimer;

    public ToastPresenter()
    {
        InitializeComponent();
    }

    public void Show(string message)
    {
        MessageText.Text = message;
        RootBorder.Visibility = Visibility.Visible;
        RootBorder.Opacity = 1;

        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5),
        };
        _hideTimer.Tick += OnHideTimerTick;
        _hideTimer.Start();
    }

    private void OnHideTimerTick(object? sender, object e)
    {
        _hideTimer?.Stop();

        var animation = new DoubleAnimation
        {
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)),
        };
        Storyboard.SetTarget(animation, RootBorder);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Completed += (_, _) => RootBorder.Visibility = Visibility.Collapsed;
        storyboard.Begin();
    }
}
