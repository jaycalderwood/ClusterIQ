using HVTools.Models;

namespace HVTools.Services;

public sealed class PlacementRecommendationService
{
    public IReadOnlyList<HealthCheck> Analyse(
        InventoryResult inv,
        IReadOnlyList<PerfSummaryRow>? perf)
    {
        var checks = new List<HealthCheck>();
        if (inv.Hosts.Count < 2) return checks;

        var nodeCpu = (perf ?? Array.Empty<PerfSummaryRow>())
            .Where(p => p.ObjectType == "Node" && p.MetricName == "Node.CPU.Usage")
            .ToDictionary(p => p.ObjectName, p => p.CurrentValue, StringComparer.OrdinalIgnoreCase);

        var source = inv.Hosts
            .OrderByDescending(h => nodeCpu.TryGetValue(h.NodeName, out var cpu) ? cpu : 0)
            .ThenByDescending(h => h.RunningVMs)
            .FirstOrDefault();

        var target = inv.Hosts
            .OrderBy(h => nodeCpu.TryGetValue(h.NodeName, out var cpu) ? cpu : 0)
            .ThenBy(h => h.RunningVMs)
            .FirstOrDefault();

        if (source is null || target is null || source.NodeName.Equals(target.NodeName, StringComparison.OrdinalIgnoreCase))
            return checks;

        var sourceCpu = nodeCpu.TryGetValue(source.NodeName, out var sCpu) ? sCpu : 0;
        var targetCpu = nodeCpu.TryGetValue(target.NodeName, out var tCpu) ? tCpu : 0;

        if (sourceCpu - targetCpu < 20 && source.RunningVMs - target.RunningVMs < 3)
            return checks;

        var vmCpu = (perf ?? Array.Empty<PerfSummaryRow>())
            .Where(p => p.ObjectType == "VM" && p.MetricName == "VM.CPU.Usage")
            .ToDictionary(p => p.ObjectName, p => p.CurrentValue, StringComparer.OrdinalIgnoreCase);

        var candidateVms = inv.VMs
            .Where(v => v.Host.Equals(source.NodeName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => vmCpu.TryGetValue(v.Name, out var cpu) ? cpu : 0)
            .ThenByDescending(v => v.MemoryGB)
            .Take(3)
            .ToList();

        if (candidateVms.Count == 0)
            return checks;

        foreach (var vm in candidateVms)
        {
            var cpu = vmCpu.TryGetValue(vm.Name, out var vCpu) ? vCpu : 0;
            checks.Add(new HealthCheck
            {
                Severity = sourceCpu - targetCpu >= 35 ? HealthSeverity.Warning : HealthSeverity.Info,
                Category = "Placement",
                ObjectName = vm.Name,
                Message = $"Recommended move: {vm.Name} from {source.NodeName} to {target.NodeName}.",
                Detail = $"Source CPU {sourceCpu:F0}% vs target CPU {targetCpu:F0}%. VM CPU {cpu:F0}% | VM Memory {vm.MemoryGB:F1} GB.",
                Recommendation = "Validate cluster constraints and live migrate this VM to improve host balance."
            });
        }

        checks.Add(new HealthCheck
        {
            Severity = sourceCpu - targetCpu >= 35 ? HealthSeverity.Warning : HealthSeverity.Info,
            Category = "Placement",
            ObjectName = inv.Connection.HostOrCluster,
            Message = $"Placement analysis suggests rebalancing from {source.NodeName} to {target.NodeName}.",
            Detail = $"Host CPU spread is {sourceCpu - targetCpu:F0} percentage points. VM spread is {source.RunningVMs - target.RunningVMs}.",
            Recommendation = "Review the recommended VM moves and rebalance workloads during the next operational window."
        });

        return checks;
    }
}
