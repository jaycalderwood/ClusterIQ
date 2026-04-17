using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HVTools.Models;

namespace HVTools.Services;

public sealed class NotificationService
{
    private readonly NotificationSettings _settings;
    private readonly HttpClient _httpClient = new();

    public NotificationService(NotificationSettings settings)
    {
        _settings = settings;
    }

    public async Task NotifyAsync(InventoryResult inv, IReadOnlyList<HealthCheck> checks, CancellationToken ct = default)
    {
        var openAlerts = inv.AlertInsights.Count(a => a.Status == AlertStatus.Open);
        var criticalAlerts = inv.AlertInsights.Count(a => a.Priority is AlertPriority.Critical or AlertPriority.High);
        var errors = checks.Count(c => c.Severity == HealthSeverity.Error);
        var warnings = checks.Count(c => c.Severity == HealthSeverity.Warning);

        if (_settings.EnableFileNotifications)
        {
            var folder = string.IsNullOrWhiteSpace(_settings.NotificationOutputFolder)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ", "notifications")
                : _settings.NotificationOutputFolder;

            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"{Sanitize(inv.Connection.HostOrCluster)}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

            var lines = new List<string>
            {
                "ClusterIQ Notification Summary",
                $"Environment: {inv.Connection.HostOrCluster}",
                $"Time: {DateTime.Now:g}",
                $"Open Alerts: {openAlerts}",
                $"High/Critical Alerts: {criticalAlerts}",
                $"Errors: {errors}",
                $"Warnings: {warnings}",
                $"Recommendations: {checks.Count(c => c.Category == "Recommendation")}",
                $"Compliance Findings: {checks.Count(c => c.Category == "Compliance")}",
                $"Drift Changes: {inv.DriftChanges.Count}",
                ""
            };

            foreach (var c in checks.Where(c => c.Severity is HealthSeverity.Error or HealthSeverity.Warning).Take(10))
                lines.Add($"- [{c.Category}] {c.ObjectName}: {c.Message}");

            File.WriteAllLines(file, lines);
        }

        if (_settings.EnableTeamsWebhook && !string.IsNullOrWhiteSpace(_settings.TeamsWebhookUrl))
        {
            var payload = new
            {
                text = $"ClusterIQ summary for {inv.Connection.HostOrCluster}: Open Alerts={openAlerts}, High/Critical={criticalAlerts}, Errors={errors}, Warnings={warnings}, Drift={inv.DriftChanges.Count}"
            };

            try
            {
                var json = JsonSerializer.Serialize(payload);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(_settings.TeamsWebhookUrl, content, ct);
                response.EnsureSuccessStatusCode();
            }
            catch
            {
                // intentionally swallow notification failures so collection remains stable
            }
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "environment" : name;
    }
}
