using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT;

namespace Com.VibeCode.CourseRec;

/// <summary>
/// 單一實例守護與 WinUI 3 啟動進入點。
/// </summary>
public static class Program
{
    private const string MutexName = @"Local\Com.VibeCode.CourseRec.SingleInstanceMutex";
    private const string ActivateEventName = @"Local\Com.VibeCode.CourseRec.ActivateEvent";

    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        using var activateEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ActivateEventName);

        if (!createdNew)
        {
            activateEvent.Set();
            return;
        }

        ComWrappersSupport.InitializeComWrappers();

        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            var app = new App();
            app.StartActivationListener(activateEvent);
        });
    }
}
