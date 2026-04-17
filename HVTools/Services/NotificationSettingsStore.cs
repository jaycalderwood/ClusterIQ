using System.IO;
using System.Text.Json;
using HVTools.Models;

namespace HVTools.Services;

public static class NotificationSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ");

    private static string FilePath => Path.Combine(FolderPath, "notificationsettings.json");

    public static NotificationSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new NotificationSettings();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<NotificationSettings>(json) ?? new NotificationSettings();
        }
        catch
        {
            return new NotificationSettings();
        }
    }

    public static void Save(NotificationSettings settings)
    {
        Directory.CreateDirectory(FolderPath);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
