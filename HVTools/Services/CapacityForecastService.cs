using System.IO;
using System.Text.Json;
using HVTools.Models;

namespace HVTools.Services;

public sealed class CapacityForecastService
{
    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ", "snapshots");

    public IReadOnlyList<HealthCheck> Analyse(string environmentName)
    {
        var snapshots = LoadRecent(environmentName, 5);
        var checks = new List<HealthCheck>();
        if (snapshots.Count < 3) return checks;

        var ordered = snapshots.OrderBy(s => s.CapturedAt).ToList();
        var first = ordered.First();
        var last = ordered.Last();

        var firstUsed = first.Hosts.Sum(h => h.UsedRamGB);
        var lastUsed = last.Hosts.Sum(h => h.UsedRamGB);
        var totalCapacity = last.Hosts.Sum(h => h.TotalRamGB);
        var elapsedDays = Math.Max(0.01, (last.CapturedAt - first.CapturedAt).TotalDays);
        var growthPerDay = (lastUsed - firstUsed) / elapsedDays;

        if (totalCapacity > 0 && growthPerDay > 0.01)
        {
            var target = totalCapacity * 0.95;
            var remaining = target - lastUsed;
            if (remaining > 0)
            {
                var days = remaining / growthPerDay;
                if (days <= 30)
                {
                    checks.Add(new HealthCheck
                    {
                        Severity = days <= 14 ? HealthSeverity.Warning : HealthSeverity.Info,
                        Category = "Forecast",
                        ObjectName = environmentName,
                        Message = $"Projected memory capacity threshold may be reached in ~{Math.Round(days, 0)} day(s).",
                        Detail = $"Used memory trend: {firstUsed} GB → {lastUsed} GB across {elapsedDays:F1} day(s). Capacity target: {Math.Round(target, 0)} GB.",
                        Recommendation = "Review memory growth, rebalance workloads, or plan additional host capacity."
                    });
                }
            }
        }

        var firstScore = first.OverallHealthScore;
        var lastScore = last.OverallHealthScore;
        if (lastScore < firstScore - 5)
        {
            checks.Add(new HealthCheck
            {
                Severity = lastScore <= firstScore - 15 ? HealthSeverity.Warning : HealthSeverity.Info,
                Category = "Forecast",
                ObjectName = environmentName,
                Message = $"Overall health score trend is declining ({firstScore}/100 → {lastScore}/100).",
                Detail = $"The score declined across the last {ordered.Count} snapshot(s).",
                Recommendation = "Review recurring warnings, open alerts, and drift changes to prevent further degradation."
            });
        }

        return checks;
    }

    private static IReadOnlyList<EnvironmentSnapshot> LoadRecent(string environmentName, int maxCount)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var prefix = Sanitize(environmentName) + "_";
            return new DirectoryInfo(FolderPath)
                .GetFiles(prefix + "*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(maxCount)
                .Select(f =>
                {
                    var json = File.ReadAllText(f.FullName);
                    return JsonSerializer.Deserialize<EnvironmentSnapshot>(json);
                })
                .Where(s => s is not null)
                .Cast<EnvironmentSnapshot>()
                .ToList();
        }
        catch
        {
            return Array.Empty<EnvironmentSnapshot>();
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "environment" : name;
    }
}
