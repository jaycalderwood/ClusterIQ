using System.IO;
using System.Text.Json;
using HVTools.Models;

namespace HVTools.Services;

public static class SavedEnvironmentProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ");

    private static string FilePath => Path.Combine(FolderPath, "profiles.json");

    public static IReadOnlyList<SavedEnvironmentProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Array.Empty<SavedEnvironmentProfile>();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<SavedEnvironmentProfile>>(json) ?? new List<SavedEnvironmentProfile>();
        }
        catch
        {
            return Array.Empty<SavedEnvironmentProfile>();
        }
    }

    public static void Save(IReadOnlyList<SavedEnvironmentProfile> profiles)
    {
        Directory.CreateDirectory(FolderPath);
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
