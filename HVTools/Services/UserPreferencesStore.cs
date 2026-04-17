using System.Text.Json;
using System.IO;

namespace HVTools.Services;

public sealed class UserPreferences
{
    public bool IsDarkMode { get; set; }
    public bool SaveConnectionInfo { get; set; }
    public string TargetHost { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool UseCurrentUser { get; set; } = true;
    public bool ConnectAzure { get; set; }
    public string AzureSubscription { get; set; } = string.Empty;
    public string AzureResourceGroup { get; set; } = string.Empty;
    public string AzureTenantId { get; set; } = string.Empty;
    public string LiveMigrationAuthMode { get; set; } = "Kerberos";
    public bool ApplyLiveMigrationAuthOnConnect { get; set; }
}

public static class UserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HVTools");

    private static string FilePath => Path.Combine(FolderPath, "userpreferences.json");

    public static UserPreferences Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new UserPreferences();

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public static void Save(UserPreferences preferences)
    {
        Directory.CreateDirectory(FolderPath);
        var json = JsonSerializer.Serialize(preferences, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
