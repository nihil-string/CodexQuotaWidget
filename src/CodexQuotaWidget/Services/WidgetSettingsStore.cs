using System.Text.Json;
using System.IO;

namespace CodexQuotaWidget.Services;

public sealed class WidgetSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Opacity { get; set; } = 0.96;
    public bool AlwaysOnTop { get; set; } = true;
    public bool ClickThrough { get; set; }
    public bool IsPositionLocked { get; set; }
}

public sealed class WidgetSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaWidget",
        "settings.json");

    public WidgetSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new WidgetSettings();
            }

            return JsonSerializer.Deserialize<WidgetSettings>(File.ReadAllText(_path), SerializerOptions)
                ?? new WidgetSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new WidgetSettings();
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
}
