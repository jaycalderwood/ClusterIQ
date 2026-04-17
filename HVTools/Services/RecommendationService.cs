using HVTools.Models;

namespace HVTools.Services;

public sealed class RecommendationService
{
    public IReadOnlyList<HealthCheck> Analyse(
        InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates,
        IReadOnlyList<PerfSummaryRow>? perf,
        IReadOnlyList<HealthCheck> findings)
    {
        var checks = new List<HealthCheck>();
        var errors = findings.Where(f => f.Severity == HealthSeverity.Error).ToList();
        var warnings = findings.Where(f => f.Severity == HealthSeverity.Warning).ToList();

        if (errors.Count == 0 && warnings.Count == 0)
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.OK,
                Category = "Recommendation",
                ObjectName = inv.Connection.HostOrCluster,
                Message = "No high-priority remediation items were identified.",
                Recommendation = "Continue periodic review and export the current state for change tracking."
            });
            return checks;
        }

        if (findings.Any(f => f.Category == "Storage" && f.Severity == HealthSeverity.Error))
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Error,
                Category = "Recommendation",
                ObjectName = inv.Connection.HostOrCluster,
                Message = "Storage remediation should be treated as the highest operational priority.",
                Detail = "Storage health issues can rapidly affect cluster stability and VM availability.",
                Recommendation = "Review S2D health, CSV capacity, and failed drive state before other maintenance tasks."
            });
        }

        if (findings.Any(f => f.Category == "Imbalance"))
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Warning,
                Category = "Recommendation",
                ObjectName = inv.Connection.HostOrCluster,
                Message = "Workload balancing action is recommended.",
                Detail = "The environment shows measurable host imbalance in VM count, CPU, or memory pressure.",
                Recommendation = "Use live migration to spread high-impact workloads across nodes more evenly."
            });
        }

        if (updates is not null && updates.Any(u => u.CriticalCount > 0 || u.RequiresReboot))
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Warning,
                Category = "Recommendation",
                ObjectName = inv.Connection.HostOrCluster,
                Message = "A maintenance window should be scheduled for update remediation.",
                Detail = string.Join(" | ", updates.Where(u => u.CriticalCount > 0 || u.RequiresReboot)
                                                   .Select(u => $"{u.NodeName}: critical={u.CriticalCount}, reboot={u.RequiresReboot}")),
                Recommendation = "Plan a rolling maintenance cycle with CAU or node drain / reboot sequencing."
            });
        }

        if (inv.ArcResources.Any(r => r.ArcStatus is "Disconnected" or "Expired"))
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Warning,
                Category = "Recommendation",
                ObjectName = "Azure Arc",
                Message = "Arc connectivity remediation is recommended.",
                Detail = string.Join(", ", inv.ArcResources.Where(r => r.ArcStatus is "Disconnected" or "Expired")
                                                           .Select(r => r.ResourceName)),
                Recommendation = "Restore Azure Arc agent connectivity and verify outbound access to Azure management endpoints."
            });
        }

        var topCategories = findings.Where(f => f.Severity is HealthSeverity.Error or HealthSeverity.Warning)
                                    .GroupBy(f => f.Category)
                                    .OrderByDescending(g => g.Count())
                                    .Take(3)
                                    .Select(g => $"{g.Key}: {g.Count()} finding(s)")
                                    .ToList();
        if (topCategories.Count > 0)
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Info,
                Category = "Recommendation",
                ObjectName = inv.Connection.HostOrCluster,
                Message = "Top focus areas identified for this collection run.",
                Detail = string.Join(" | ", topCategories),
                Recommendation = "Address higher-severity findings first, then clear repeating warning categories."
            });
        }

        return checks;
    }
}
