using System.IO;
using System.Security;
using System.Text.Json;

namespace CodexQuotaWidget.Services;

public sealed class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Opacity { get; set; } = 0.96;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool IsPositionLocked { get; set; }
    public bool FollowCodex { get; set; } = true;
}

public sealed class WidgetSettingsStore
{
    private const int MaximumSettingsBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;

    public WidgetSettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaWidget",
            "settings.json"))
    {
    }

    internal WidgetSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public WidgetSettings Load() => LoadWithStatus().Settings;

    internal WidgetSettingsLoadResult LoadWithStatus()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new WidgetSettingsLoadResult(
                    new WidgetSettings(),
                    WidgetSettingsLoadStatus.Missing);
            }

            using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumSettingsBytes)
            {
                return new WidgetSettingsLoadResult(
                    new WidgetSettings(),
                    WidgetSettingsLoadStatus.Invalid);
            }

            var settings = JsonSerializer.Deserialize<WidgetSettings>(stream, SerializerOptions);
            if (settings is null)
            {
                return new WidgetSettingsLoadResult(
                    new WidgetSettings(),
                    WidgetSettingsLoadStatus.Invalid);
            }

            Normalize(settings);
            return new WidgetSettingsLoadResult(settings, WidgetSettingsLoadStatus.Loaded);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return new WidgetSettingsLoadResult(
                new WidgetSettings(),
                WidgetSettingsLoadStatus.Invalid);
        }
    }

    public void Save(WidgetSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, SerializerOptions));
        File.Move(temporaryPath, _path, true);
    }

    private static void Normalize(WidgetSettings settings)
    {
        settings.Opacity = double.IsFinite(settings.Opacity)
            ? Math.Clamp(settings.Opacity, 0.6, 1.0)
            : 0.96;
        if (settings.Left is not null && !double.IsFinite(settings.Left.Value))
        {
            settings.Left = null;
        }
        if (settings.Top is not null && !double.IsFinite(settings.Top.Value))
        {
            settings.Top = null;
        }
    }
}

internal enum WidgetSettingsLoadStatus
{
    Missing,
    Loaded,
    Invalid
}

internal readonly record struct WidgetSettingsLoadResult(
    WidgetSettings Settings,
    WidgetSettingsLoadStatus Status);
