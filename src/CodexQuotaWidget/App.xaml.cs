using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Windows;
using CodexQuotaWidget.Services;

namespace CodexQuotaWidget;

public partial class App : System.Windows.Application
{
    private const string ComposerProbeArgument = "--composer-probe";
    private const string SingleInstanceName = "Local\\CodexQuotaWidget.SingleInstance";
    private const string ShowWindowEventName = "Local\\CodexQuotaWidget.ShowWindow";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showWindowEvent;
    private RegisteredWaitHandle? _showWindowWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (TryRunComposerProbe(e.Args, out var probeExitCode))
        {
            Shutdown(probeExitCode);
            return;
        }

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

    private static bool TryRunComposerProbe(string[] arguments, out int exitCode)
    {
        exitCode = 0;
        if (arguments.Length == 0 ||
            !string.Equals(arguments[0], ComposerProbeArgument, StringComparison.Ordinal))
        {
            return false;
        }

        if (arguments.Length != 3 ||
            !double.TryParse(
                arguments[1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var width) ||
            !double.TryParse(
                arguments[2],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var height) ||
            !double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0)
        {
            exitCode = 2;
            return true;
        }

        var locator = new CodexComposerLocator();
        if (!locator.TryLocate(width, height, out var target))
        {
            exitCode = 1;
            return true;
        }

        Console.Out.WriteLine(JsonSerializer.Serialize(ComposerProbePayload.FromTarget(target)));
        Console.Out.Flush();
        return true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWindowWait?.Unregister(null);
        _showWindowEvent?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
