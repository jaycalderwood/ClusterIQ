using HVTools.Models;

namespace HVTools.Services;

public sealed class AlertInsightService
{
    private readonly AlertThresholdSettings _settings;

    public AlertInsightService(AlertThresholdSettings settings)
    {
        _settings = settings;
    }

    public IReadOnlyList<AlertInsight> Build(
        InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates,
        IReadOnlyList<PerfSummaryRow>? perf,
        IReadOnlyList<HealthCheck> findings)
    {
        var alerts = new List<AlertInsight>();

        foreach (var finding in findings.Where(f => f.Severity is HealthSeverity.Error or HealthSeverity.Warning))
        {
            var priority = InferPriority(finding);
            alerts.Add(new AlertInsight
            {
                ObjectName = finding.ObjectName,
                Category = finding.Category,
                Severity = finding.Severity,
                Priority = priority,
                Status = AlertStatus.Open,
                TriggerReason = finding.Message,
                Impact = InferImpact(finding),
                RecommendedAction = string.IsNullOrWhiteSpace(finding.Recommendation)
                    ? InferAction(finding)
                    : finding.Recommendation,
                RecommendationCategory = InferRecommendationCategory(finding),
                DetectedAt = DateTime.Now
            });
        }

        if (updates is not null)
        {
            foreach (var update in updates.Where(u => u.CriticalCount > 0 || u.RequiresReboot))
            {
                alerts.Add(new AlertInsight
                {
                    ObjectName = update.NodeName,
                    Category = "Updates",
                    Severity = update.CriticalCount > 0 ? HealthSeverity.Warning : HealthSeverity.Info,
                    Priority = update.CriticalCount > 0 ? AlertPriority.High : AlertPriority.Medium,
                    Status = AlertStatus.Open,
                    TriggerReason = update.CriticalCount > 0
                        ? $"{update.CriticalCount} critical updates pending."
                        : "Node requires reboot after updates.",
                    Impact = "Pending update remediation can affect security posture and maintenance readiness.",
                    RecommendedAction = "Schedule rolling maintenance and remediate pending updates for this node.",
                    RecommendationCategory = "Patch / Maintenance",
                    DetectedAt = DateTime.Now
                });
            }
        }

        return alerts
            .OrderByDescending(a => PriorityWeight(a.Priority))
            .ThenByDescending(a => SeverityWeight(a.Severity))
            .ThenBy(a => a.ObjectName)
            .ToList();
    }

    private AlertPriority InferPriority(HealthCheck finding)
    {
        if (finding.Category == "Storage" && finding.Severity == HealthSeverity.Error)
            return AlertPriority.Critical;
        if (finding.Category == "Imbalance" && finding.Severity == HealthSeverity.Error)
            return AlertPriority.High;
        if (finding.Severity == HealthSeverity.Error)
            return AlertPriority.High;
        if (finding.Category == "Recommendation")
            return AlertPriority.Medium;
        return AlertPriority.Medium;
    }

    private static string InferImpact(HealthCheck finding) => finding.Category switch
    {
        "Storage" => "Storage issues can affect cluster stability, CSV availability, and workload continuity.",
        "Imbalance" => "Resource imbalance can reduce failover headroom and create uneven host pressure.",
        "Score" => "Health score degradation indicates increased operational risk for the environment or object.",
        "Azure Arc" => "Arc disconnects reduce management visibility, compliance insight, and extension coverage.",
        "Updates" => "Pending remediation can delay maintenance readiness and security hardening.",
        _ => "This condition should be reviewed to prevent elevated operational risk."
    };

    private static string InferAction(HealthCheck finding) => finding.Category switch
    {
        "Storage" => "Review storage health immediately and remediate failed or degraded components before other maintenance tasks.",
        "Imbalance" => "Rebalance workloads across nodes and review host placement decisions.",
        "Score" => "Review contributing findings for this object and remediate the highest-severity items first.",
        "Azure Arc" => "Restore Arc connectivity and validate outbound management access to Azure.",
        "Updates" => "Plan update installation and reboot sequencing during the next maintenance window.",
        _ => "Review the finding and address the highest-impact contributing issue."
    };

    private static string InferRecommendationCategory(HealthCheck finding) => finding.Category switch
    {
        "Storage" => "Storage / Capacity",
        "Imbalance" => "Performance / Capacity",
        "Score" => "General Health",
        "Azure Arc" => "Connectivity / Management",
        "Updates" => "Patch / Maintenance",
        _ => "Operations"
    };

    private static int PriorityWeight(AlertPriority p) => p switch
    {
        AlertPriority.Critical => 4,
        AlertPriority.High => 3,
        AlertPriority.Medium => 2,
        _ => 1
    };

    private static int SeverityWeight(HealthSeverity s) => s switch
    {
        HealthSeverity.Error => 4,
        HealthSeverity.Warning => 3,
        HealthSeverity.Info => 2,
        _ => 1
    };
}
