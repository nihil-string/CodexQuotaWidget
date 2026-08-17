using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class WidgetSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"CodexQuotaWidget.Tests.{Guid.NewGuid():N}");

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void LoadWithStatusMissingFileReturnsDefaults()
    {
        var store = new WidgetSettingsStore(SettingsPath);

        var result = store.LoadWithStatus();

        Assert.Equal(WidgetSettingsLoadStatus.Missing, result.Status);
        Assert.True(result.Settings.FollowCodex);
        Assert.Equal(0.96, result.Settings.Opacity);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public void LoadWithStatusInvalidFileReportsFailure(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, json);
        var store = new WidgetSettingsStore(SettingsPath);

        var result = store.LoadWithStatus();

        Assert.Equal(WidgetSettingsLoadStatus.Invalid, result.Status);
        Assert.True(result.Settings.FollowCodex);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void LoadWithStatusOversizedFileReportsFailureWithoutReplacingIt()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, new string(' ', 64 * 1024 + 1));
        var originalLength = new FileInfo(SettingsPath).Length;
        var store = new WidgetSettingsStore(SettingsPath);

        var result = store.LoadWithStatus();

        Assert.Equal(WidgetSettingsLoadStatus.Invalid, result.Status);
        Assert.Equal(originalLength, new FileInfo(SettingsPath).Length);
    }

    [Fact]
    public void LoadWithStatusNormalizesOutOfRangeValues()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            SettingsPath,
            """
            {
              "Left": 120,
              "Top": 80,
              "Opacity": 0.2,
              "AlwaysOnTop": false,
              "FollowCodex": false
            }
            """);
        var store = new WidgetSettingsStore(SettingsPath);

        var result = store.LoadWithStatus();

        Assert.Equal(WidgetSettingsLoadStatus.Loaded, result.Status);
        Assert.Equal(0.6, result.Settings.Opacity);
        Assert.Equal(120, result.Settings.Left);
        Assert.Equal(80, result.Settings.Top);
        Assert.False(result.Settings.AlwaysOnTop);
        Assert.False(result.Settings.FollowCodex);
    }

    [Fact]
    public void SaveReplacesSettingsWithoutLeavingTemporaryFile()
    {
        var store = new WidgetSettingsStore(SettingsPath);
        var settings = new WidgetSettings { FollowCodex = false, Opacity = 0.85 };

        store.Save(settings);
        settings.Opacity = 1.0;
        store.Save(settings);

        var loaded = store.LoadWithStatus();
        Assert.Equal(WidgetSettingsLoadStatus.Loaded, loaded.Status);
        Assert.False(loaded.Settings.FollowCodex);
        Assert.Equal(1.0, loaded.Settings.Opacity);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
