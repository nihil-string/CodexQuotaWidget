using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class CodexProcessMonitorTests
{
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.707.3748.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe")]
    [InlineData(@"D:\WindowsApps\OpenAI.Codex_1.0_x64__id\app\ChatGPT.exe")]
    [InlineData(@"d:\windowsapps\openai.codex_1.0_x64__id\APP\CHATGPT.EXE")]
    [InlineData("E:/WindowsApps/OpenAI.Codex_1.0_x64__id/app/ChatGPT.exe")]
    [InlineData(@"E:\Packages/WindowsApps\OpenAI.Codex_1.0_x64__id/app\ChatGPT.exe")]
    public void RecognizesPackagedCodexDesktopExecutable(string path)
    {
        Assert.True(CodexProcessMonitor.IsCodexDesktopExecutablePath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"WindowsApps\OpenAI.Codex_x\app\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\OpenAI\ChatGPT.exe")]
    [InlineData(@"C:\WindowsApps\OpenAI.Codex_\app\ChatGPT.exe")]
    [InlineData(@"C:\WindowsApps\NotOpenAI.Codex_x\app\ChatGPT.exe")]
    [InlineData(@"C:\WindowsApps\OpenAI.Codex_x\wrong\ChatGPT.exe")]
    [InlineData(@"C:\WindowsApps\OpenAI.Codex_x\app\sub\ChatGPT.exe")]
    [InlineData(@"C:\Temp\WindowsApps\OpenAI.Codex_fake\other\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_1.0.0\app\resources\codex.exe")]
    [InlineData(@"C:\Users\user\CodexQuotaWidget.exe")]
    public void RejectsUnrelatedExecutables(string? path)
    {
        Assert.False(CodexProcessMonitor.IsCodexDesktopExecutablePath(path));
    }

    [Fact]
    public void RejectsInvalidPathWithoutThrowing()
    {
        Assert.False(CodexProcessMonitor.IsCodexDesktopExecutablePath(
            "C:\\WindowsApps\\OpenAI.Codex_x\\app\\ChatGPT.exe\0suffix"));
    }

    [Fact]
    public void StartupCommandUsesBackgroundModeAndQuotesPath()
    {
        Assert.Equal(
            "\"C:\\Apps\\Codex Widget\\CodexQuotaWidget.exe\" --background",
            StartupRegistration.BuildCommand(@"C:\Apps\Codex Widget\CodexQuotaWidget.exe"));
    }
}
