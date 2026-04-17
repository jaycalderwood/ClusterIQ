using System.IO;
using System.Text.Json;
using HVTools.Models;

namespace HVTools.Services;

public static class AlertSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ");

    private static string FilePath => Path.Combine(FolderPath, "alertsettings.json");

    public static AlertThresholdSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AlertThresholdSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AlertThresholdSettings>(json) ?? new AlertThresholdSettings();
        }
        catch
        {
            return new AlertThresholdSettings();
        }
    }

    public static void Save(AlertThresholdSettings settings)
    {
        Directory.CreateDirectory(FolderPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
