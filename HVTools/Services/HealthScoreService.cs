using HVTools.Models;

namespace HVTools.Services;

public sealed class HealthScoreService
{
    public int CalculateOverallScore(
        InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>? perf = null)
    {
        var hostScores = BuildHostScores(inv, updates, perf).Select(h => h.Score).ToList();
        var vmScores = BuildVmScores(inv).Select(v => v.Score).ToList();

        if (hostScores.Count == 0 && vmScores.Count == 0)
            return 100;

        return (int)Math.Round(hostScores.Concat(vmScores).Average(), MidpointRounding.AwayFromZero);
    }

    public IReadOnlyList<HealthCheck> Analyse(
        InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>? perf = null)
    {
        var checks = new List<HealthCheck>();
        var hostScores = BuildHostScores(inv, updates, perf).ToList();
        var vmScores = BuildVmScores(inv).ToList();
        var overall = CalculateOverallScore(inv, updates, perf);

        checks.Add(new HealthCheck
        {
            Severity = SeverityFromScore(overall),
            Category = "Score",
            ObjectName = inv.Connection.HostOrCluster,
            Message = $"Overall environment health score is {overall}/100.",
            Detail = $"Hosts scored: {hostScores.Count}, VMs scored: {vmScores.Count}.",
            Recommendation = RecommendationFromScore(overall)
        });

        foreach (var h in hostScores)
        {
            checks.Add(new HealthCheck
            {
                Severity = SeverityFromScore(h.Score),
                Category = "Score",
                ObjectName = h.Name,
                Message = $"Host health score for {h.Name} is {h.Score}/100.",
                Detail = h.Detail,
                Recommendation = RecommendationFromScore(h.Score)
            });
        }

        foreach (var vm in vmScores.Where(v => v.Score < 90))
        {
            checks.Add(new HealthCheck
            {
                Severity = SeverityFromScore(vm.Score),
                Category = "Score",
                ObjectName = vm.Name,
                Message = $"VM health score for {vm.Name} is {vm.Score}/100.",
                Detail = vm.Detail,
                Recommendation = RecommendationFromScore(vm.Score)
            });
        }

        checks.AddRange(BuildImbalanceChecks(inv, perf));
        return checks;
    }

    private static IEnumerable<(string Name, int Score, string Detail)> BuildHostScores(
        InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates,
        IReadOnlyList<PerfSummaryRow>? perf)
    {
        foreach (var h in inv.Hosts)
        {
            var score = 100;
            var notes = new List<string>();

            if (!string.IsNullOrWhiteSpace(h.NodeStatus) && h.NodeStatus is not ("Online" or "Up"))
            {
                score -= 40;
                notes.Add($"Node status: {h.NodeStatus}");
            }

            if (h.TotalRamGB > 0)
            {
                var memPct = (int)Math.Round((double)h.UsedRamGB / h.TotalRamGB * 100, MidpointRounding.AwayFromZero);
                notes.Add($"Memory {memPct}% used");
                if (memPct >= 95) score -= 30;
                else if (memPct >= 85) score -= 15;
            }

            var upd = updates?.FirstOrDefault(u => u.NodeName.Equals(h.NodeName, StringComparison.OrdinalIgnoreCase));
            if (upd is not null)
            {
                if (upd.CriticalCount > 0) { score -= 20; notes.Add($"{upd.CriticalCount} critical updates pending"); }
                else if (upd.PendingCount > 0) { score -= 10; notes.Add($"{upd.PendingCount} updates pending"); }
                if (upd.RequiresReboot) { score -= 10; notes.Add("reboot required"); }
            }

            var cpu = perf?.FirstOrDefault(p =>
                p.ObjectType == "Node" &&
                p.ObjectName.Equals(h.NodeName, StringComparison.OrdinalIgnoreCase) &&
                p.MetricName == "Node.CPU.Usage");

            if (cpu is not null)
            {
                notes.Add($"CPU {cpu.CurrentValue:F0}%");
                if (cpu.CurrentValue >= 90) score -= 20;
                else if (cpu.CurrentValue >= 75) score -= 10;
            }

            score = Math.Max(0, Math.Min(100, score));
            yield return (h.NodeName, score, string.Join(" | ", notes));
        }
    }

    private static IEnumerable<(string Name, int Score, string Detail)> BuildVmScores(InventoryResult inv)
    {
        var diskMap = inv.VmDisks.GroupBy(d => d.VmName).ToDictionary(g => g.Key, g => g.ToList());
        var snapMap = inv.VmSnapshots.GroupBy(s => s.VmName).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var vm in inv.VMs)
        {
            var score = 100;
            var notes = new List<string>();

            if (vm.PowerState == "Paused") { score -= 25; notes.Add("paused"); }
            else if (vm.PowerState == "Saved") { score -= 10; notes.Add("saved state"); }

            if (vm.IntegrationServices is "Outdated" or "Unknown" or "")
            {
                score -= 10;
                notes.Add("integration services outdated/unknown");
            }

            if (snapMap.TryGetValue(vm.Name, out var snaps) && snaps.Count > 0)
            {
                var oldest = snaps.Max(s => s.AgeDays);
                score -= Math.Min(20, 5 * snaps.Count);
                notes.Add($"{snaps.Count} snapshot(s), oldest {oldest} days");
            }

            if (diskMap.TryGetValue(vm.Name, out var disks))
            {
                if (disks.Any(d => d.Path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
                {
                    score -= 5;
                    notes.Add("ISO mounted");
                }

                if (disks.Any(d => d.Controller == "IDE" && d.DiskType != "OS"))
                {
                    score -= 5;
                    notes.Add("data disk on IDE");
                }
            }

            score = Math.Max(0, Math.Min(100, score));
            yield return (vm.Name, score, string.Join(" | ", notes));
        }
    }

    private static IEnumerable<HealthCheck> BuildImbalanceChecks(
        InventoryResult inv,
        IReadOnlyList<PerfSummaryRow>? perf)
    {
        var checks = new List<HealthCheck>();

        if (inv.Hosts.Count > 1)
        {
            var vmSpread = inv.Hosts.Max(h => h.RunningVMs) - inv.Hosts.Min(h => h.RunningVMs);
            if (vmSpread >= 3)
            {
                checks.Add(new HealthCheck
                {
                    Severity = vmSpread >= 5 ? HealthSeverity.Error : HealthSeverity.Warning,
                    Category = "Imbalance",
                    ObjectName = inv.Connection.HostOrCluster,
                    Message = $"Cluster workload imbalance detected: VM spread across nodes is {vmSpread}.",
                    Detail = string.Join(" | ", inv.Hosts.OrderByDescending(h => h.RunningVMs).Select(h => $"{h.NodeName}: {h.RunningVMs} running VMs")),
                    Recommendation = "Review VM placement and rebalance workloads across nodes."
                });
            }

            var memPcts = inv.Hosts.Where(h => h.TotalRamGB > 0)
                                  .Select(h => new { h.NodeName, Pct = (int)Math.Round((double)h.UsedRamGB / h.TotalRamGB * 100, MidpointRounding.AwayFromZero) })
                                  .ToList();
            if (memPcts.Count > 1)
            {
                var spread = memPcts.Max(h => h.Pct) - memPcts.Min(h => h.Pct);
                if (spread >= 20)
                {
                    checks.Add(new HealthCheck
                    {
                        Severity = spread >= 30 ? HealthSeverity.Warning : HealthSeverity.Info,
                        Category = "Imbalance",
                        ObjectName = inv.Connection.HostOrCluster,
                        Message = $"Host memory utilization imbalance detected: spread is {spread} percentage points.",
                        Detail = string.Join(" | ", memPcts.OrderByDescending(h => h.Pct).Select(h => $"{h.NodeName}: {h.Pct}%")),
                        Recommendation = "Consider live migration of memory-intensive VMs to level host pressure."
                    });
                }
            }

            var nodeCpu = perf?.Where(p => p.ObjectType == "Node" && p.MetricName == "Node.CPU.Usage").ToList() ?? [];
            if (nodeCpu.Count > 1)
            {
                var cpuSpread = nodeCpu.Max(p => p.CurrentValue) - nodeCpu.Min(p => p.CurrentValue);
                if (cpuSpread >= 25)
                {
                    checks.Add(new HealthCheck
                    {
                        Severity = cpuSpread >= 40 ? HealthSeverity.Warning : HealthSeverity.Info,
                        Category = "Imbalance",
                        ObjectName = inv.Connection.HostOrCluster,
                        Message = $"Host CPU imbalance detected: spread is {cpuSpread:F0} percentage points.",
                        Detail = string.Join(" | ", nodeCpu.OrderByDescending(p => p.CurrentValue).Select(p => $"{p.ObjectName}: {p.CurrentValue:F0}%")),
                        Recommendation = "Move CPU-heavy workloads to less-utilized nodes."
                    });
                }
            }
        }

        return checks;
    }

    private static HealthSeverity SeverityFromScore(int score) => score switch
    {
        >= 90 => HealthSeverity.OK,
        >= 75 => HealthSeverity.Info,
        >= 60 => HealthSeverity.Warning,
        _ => HealthSeverity.Error
    };

    private static string RecommendationFromScore(int score) => score switch
    {
        >= 90 => "No immediate action recommended.",
        >= 75 => "Monitor this object and review any informational findings.",
        >= 60 => "Investigate warnings and plan remediation during the next maintenance cycle.",
        _ => "Prioritize remediation as part of the next operational window."
    };
}
