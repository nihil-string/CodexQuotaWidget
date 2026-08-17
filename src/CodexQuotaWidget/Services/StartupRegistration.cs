using Microsoft.Win32;

namespace CodexQuotaWidget.Services;

public sealed class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexQuotaWidget";

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法打开当前用户启动项。 ");

        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("无法确定当前程序路径。 ");
        }

        key.SetValue(ValueName, BuildCommand(executablePath), RegistryValueKind.String);
    }

    public static string BuildCommand(string executablePath) => $"\"{executablePath}\" --background";
}
