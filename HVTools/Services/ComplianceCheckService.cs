using HVTools.Models;

namespace HVTools.Services;

public sealed class ComplianceCheckService
{
    public IReadOnlyList<HealthCheck> Analyse(InventoryResult inv)
    {
        var checks = new List<HealthCheck>();

        foreach (var vm in inv.VMs)
        {
            if (vm.IntegrationServices is "Outdated" or "Unknown" or "")
            {
                checks.Add(new HealthCheck
                {
                    Severity = HealthSeverity.Warning,
                    Category = "Compliance",
                    ObjectName = vm.Name,
                    Message = "Integration services are outdated or unknown.",
                    Detail = $"VM {vm.Name} reports integration services state '{vm.IntegrationServices}'.",
                    Recommendation = "Review guest integration services and update them if required."
                });
            }
        }

        foreach (var snap in inv.VmSnapshots.Where(s => s.AgeDays >= 30))
        {
            checks.Add(new HealthCheck
            {
                Severity = snap.AgeDays >= 90 ? HealthSeverity.Warning : HealthSeverity.Info,
                Category = "Compliance",
                ObjectName = snap.VmName,
                Message = $"Snapshot '{snap.SnapshotName}' is older than policy threshold.",
                Detail = $"Snapshot age: {snap.AgeDays} day(s).",
                Recommendation = "Review whether the snapshot is still required and remove stale checkpoints."
            });
        }

        foreach (var disk in inv.VmDisks.Where(d => d.Path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Info,
                Category = "Compliance",
                ObjectName = disk.VmName,
                Message = "VM has an ISO mounted.",
                Detail = $"Mounted media path: {disk.Path}",
                Recommendation = "Remove mounted installation media if it is no longer required."
            });
        }

        var duplicateVmNames = inv.VMs.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                                      .Where(g => g.Count() > 1)
                                      .ToList();
        foreach (var dup in duplicateVmNames)
        {
            checks.Add(new HealthCheck
            {
                Severity = HealthSeverity.Warning,
                Category = "Compliance",
                ObjectName = dup.Key,
                Message = "Duplicate VM name detected across the collected environment.",
                Detail = $"Occurrences: {dup.Count()}",
                Recommendation = "Review naming standards and ensure duplicate VM names are intentional."
            });
        }

        return checks;
    }
}
