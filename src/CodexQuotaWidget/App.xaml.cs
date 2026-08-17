using System.Threading;
using System.Windows;

namespace CodexQuotaWidget;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceName = "Local\\CodexQuotaWidget.SingleInstance";
    private const string ShowWindowEventName = "Local\\CodexQuotaWidget.ShowWindow";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private RegisteredWaitHandle? _showWindowWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Create the signal first so a concurrently starting second instance cannot miss it.
        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        _singleInstanceMutex = new Mutex(true, SingleInstanceName, out var ownsMutex);
        if (!ownsMutex)
        {
            _showWindowEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        _showWindowWait = ThreadPool.RegisterWaitForSingleObject(
            _showWindowEvent,
            (_, timedOut) =>
            {
                if (!timedOut)
                {
                    Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromExternalLaunch());
                }
            },
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        var backgroundStart = e.Args.Any(argument =>
            string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase));
        var enableSystemIntegration = !e.Args.Any(argument =>
            string.Equals(argument, "--no-system-integration", StringComparison.OrdinalIgnoreCase));
        MainWindow = new MainWindow(backgroundStart, enableSystemIntegration);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWindowWait?.Unregister(null);
        _showWindowEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
