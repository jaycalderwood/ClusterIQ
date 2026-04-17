namespace HVTools.Models;

public sealed class NotificationSettings
{
    public bool EnableFileNotifications { get; set; } = true;
    public string NotificationOutputFolder { get; set; } = string.Empty;
    public bool EnableTeamsWebhook { get; set; }
    public string TeamsWebhookUrl { get; set; } = string.Empty;
    public int SnapshotRetentionCount { get; set; } = 10;
}
