using System.Text.RegularExpressions;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class ComposerIntegrationArchitectureTests
{
    [Fact]
    public void MainWindowDoesNotCreateACrossProcessOwnerRelationship()
    {
        var source = LoadMainWindowSource();

        Assert.DoesNotContain("GwlHwndParent", source);
        Assert.DoesNotContain("OwnerHandle", source);
        Assert.DoesNotMatch(
            new Regex(@"SetWindowLongPtr\s*\([^,]+,\s*-8\s*,", RegexOptions.CultureInvariant),
            source);
        Assert.DoesNotMatch(
            new Regex(@"WindowInteropHelper\s*\([^)]*\)\s*\.Owner\s*=", RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void MainWindowRunsUiAutomationOnlyInTheIsolatedProbeProcess()
    {
        var source = LoadMainWindowSource();

        Assert.Contains("ProcessStartInfo", source);
        Assert.Contains("--composer-probe", source);
        Assert.Contains("TryTerminateProbeProcess", source);
        Assert.Contains("await process.WaitForExitAsync();", source);
        Assert.DoesNotContain("_composerLocator.TryLocate", source);
        Assert.DoesNotContain("Task.Run<CodexComposerTarget?>", source);
    }

    [Fact]
    public void ComposerProbeModeRunsBeforeSingleInstanceAndMainWindowStartup()
    {
        var source = LoadApplicationSource();
        var probeDispatch = source.IndexOf("TryRunComposerProbe(e.Args", StringComparison.Ordinal);
        var mutexCreation = source.IndexOf("new Mutex", StringComparison.Ordinal);
        var mainWindowCreation = source.IndexOf("new MainWindow", StringComparison.Ordinal);

        Assert.True(probeDispatch >= 0);
        Assert.True(mutexCreation > probeDispatch);
        Assert.True(mainWindowCreation > probeDispatch);
    }

    private static string LoadMainWindowSource()
        => LoadApplicationFile("MainWindow.xaml.cs");

    private static string LoadApplicationSource()
        => LoadApplicationFile("App.xaml.cs");

    private static string LoadApplicationFile(string fileName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var sourcePath = Path.Combine(
                directory.FullName,
                "src",
                "CodexQuotaWidget",
                fileName);
            if (File.Exists(sourcePath))
            {
                return File.ReadAllText(sourcePath);
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate src/CodexQuotaWidget/{fileName} from the test output directory.");
    }
}
