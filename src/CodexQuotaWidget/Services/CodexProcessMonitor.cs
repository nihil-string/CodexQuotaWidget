using System.Diagnostics;
using System.IO;

namespace CodexQuotaWidget.Services;

public sealed class CodexProcessMonitor
{
    private const string CodexPackagePrefix = "OpenAI.Codex_";

    public bool IsDesktopAppRunning()
    {
        var processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (IsCodexDesktopExecutablePath(process.MainModule?.FileName))
                    {
                        return true;
                    }
                }
                catch (Exception exception) when (
                    exception is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // A process can exit or become inaccessible between enumeration and inspection.
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return false;
    }

    public static bool IsCodexDesktopExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(executablePath))
            {
                return false;
            }

            var executable = new FileInfo(Path.GetFullPath(executablePath));
            var appDirectory = executable.Directory;
            var packageDirectory = appDirectory?.Parent;
            var windowsAppsDirectory = packageDirectory?.Parent;
            var packageName = packageDirectory?.Name;

            return executable.Name.Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase) &&
                   appDirectory?.Name.Equals("app", StringComparison.OrdinalIgnoreCase) == true &&
                   packageName is not null &&
                   packageName.Length > CodexPackagePrefix.Length &&
                   packageName.StartsWith(CodexPackagePrefix, StringComparison.OrdinalIgnoreCase) &&
                   windowsAppsDirectory?.Name.Equals("WindowsApps", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
