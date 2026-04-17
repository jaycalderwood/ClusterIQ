// =============================================================================
//  HealthCheckService  (v1.1 — adds update & perf checks)
// =============================================================================

using HVTools.Models;

namespace HVTools.Services;

public sealed class HealthCheckService
{
    private const int SnapshotAgeDaysWarning   = 14;
    private const int SnapshotAgeDaysCritical  = 30;
    private const int CsvFreeSpaceWarnPercent  = 20;
    private const int CsvFreeSpaceCritPercent  = 10;
    private const int HostMemWarnPercent        = 85;
    private const int HostMemCritPercent        = 95;
    private const string LatestArcAgentVersion  = "1.38.0";

    public IReadOnlyList<HealthCheck> Analyse(InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>?   perf    = null)
    {
        var findings = new List<HealthCheck>();

        CheckSnapshots(inv.VmSnapshots, findings);
        CheckIntegrationServices(inv.VMs, findings);
        CheckVMState(inv.VMs, findings);
        CheckVMDisks(inv.VmDisks, findings);
        CheckHosts(inv.Hosts, findings);
        CheckPhysicalNics(inv.PhysicalNics, findings);
        CheckStorage(inv.Volumes, findings);
        CheckS2D(inv.S2DPools, findings);
        CheckDuplicateMacs(inv.VmNics, findings);
        CheckArc(inv.ArcResources, findings);
        CheckCluster(inv.Clusters, findings);

        // New in v1.1
        if (updates is not null) CheckUpdates(updates, findings);
        if (perf    is not null) CheckPerf(perf, findings);

        AppendOkSummaries(inv, findings);

        return findings.OrderBy(h => h.Severity switch
        {
            HealthSeverity.Error   => 0,
            HealthSeverity.Warning => 1,
            HealthSeverity.Info    => 2,
            _                      => 3
        }).ToList();
    }

    // ── Snapshots ────────────────────────────────────────────────────────────
    private static void CheckSnapshots(IReadOnlyList<VmSnapshot> snaps, List<HealthCheck> out_)
    {
        foreach (var g in snaps.GroupBy(s => s.VmName))
        {
            var oldest = g.OrderByDescending(s => s.AgeDays).First();
            if (oldest.AgeDays >= SnapshotAgeDaysCritical)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "VM", ObjectName = g.Key,
                    Message = $"{g.Key} has {g.Count()} snapshot(s); oldest is {oldest.AgeDays} days old.",
                    Detail  = $"'{oldest.SnapshotName}' created {oldest.CreationTime:yyyy-MM-dd}",
                    Recommendation = "Delete or merge old snapshots to reduce VHDX chain depth." });
            else if (oldest.AgeDays >= SnapshotAgeDaysWarning)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "VM", ObjectName = g.Key,
                    Message = $"{g.Key} has {g.Count()} snapshot(s); oldest is {oldest.AgeDays} days old.",
                    Recommendation = "Review and remove snapshots no longer needed." });
        }
    }

    // ── Integration Services ─────────────────────────────────────────────────
    private static void CheckIntegrationServices(IReadOnlyList<VmInfo> vms, List<HealthCheck> out_)
    {
        foreach (var vm in vms.Where(v =>
            v.IntegrationServices is "Outdated" or "Unknown" or ""))
        {
            out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "VM", ObjectName = vm.Name,
                Message = $"{vm.Name} has outdated or unknown Integration Services.",
                Detail  = $"Version: {vm.IntegrationServices}",
                Recommendation = "Run Windows Update inside the VM or update via Hyper-V manager." });
        }
    }

    // ── VM State ─────────────────────────────────────────────────────────────
    private static void CheckVMState(IReadOnlyList<VmInfo> vms, List<HealthCheck> out_)
    {
        foreach (var vm in vms.Where(v => v.PowerState == "Paused"))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "VM", ObjectName = vm.Name,
                Message = $"{vm.Name} is in Paused state.", Recommendation = "Resume or shut down." });
        foreach (var vm in vms.Where(v => v.PowerState == "Saved"))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Info, Category = "VM", ObjectName = vm.Name,
                Message = $"{vm.Name} is in Saved state.", Recommendation = "Verify the VM resumes cleanly." });
    }

    // ── VM Disks ─────────────────────────────────────────────────────────────
    private static void CheckVMDisks(IReadOnlyList<VmDisk> disks, List<HealthCheck> out_)
    {
        foreach (var d in disks.Where(d => d.Path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "VM", ObjectName = d.VmName,
                Message = $"{d.VmName} has an ISO mounted: {System.IO.Path.GetFileName(d.Path)}",
                Recommendation = "Eject ISOs after installation — they block some live migrations." });
        foreach (var d in disks.Where(d => d.Controller == "IDE" && d.DiskType != "OS"))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Info, Category = "VM", ObjectName = d.VmName,
                Message = $"{d.VmName}: data disk on IDE controller — consider SCSI.",
                Recommendation = "Move data disks to SCSI for better throughput on Gen 1 VMs." });
    }

    // ── Hosts ────────────────────────────────────────────────────────────────
    private static void CheckHosts(IReadOnlyList<HvHost> hosts, List<HealthCheck> out_)
    {
        foreach (var h in hosts)
        {
            if (!string.IsNullOrWhiteSpace(h.NodeStatus) && h.NodeStatus is not ("Online" or "Up"))
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Host", ObjectName = h.NodeName,
                    Message = $"Node {h.NodeName} is not online (status: {h.NodeStatus}).",
                    Recommendation = "Check Failover Cluster Manager and event logs." });
            if (h.TotalRamGB > 0)
            {
                int pct = (int)(h.UsedRamGB * 100 / h.TotalRamGB);
                if (pct >= HostMemCritPercent)
                    out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Host", ObjectName = h.NodeName,
                        Message = $"{h.NodeName} memory critical: {pct}% ({h.UsedRamGB}/{h.TotalRamGB} GB).",
                        Recommendation = "Live-migrate VMs or add RAM." });
                else if (pct >= HostMemWarnPercent)
                    out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Host", ObjectName = h.NodeName,
                        Message = $"{h.NodeName} memory high: {pct}% ({h.UsedRamGB}/{h.TotalRamGB} GB).",
                        Recommendation = "Consider migrating VMs to less-loaded nodes." });
            }
        }
    }

    // ── Physical NICs ────────────────────────────────────────────────────────
    private static void CheckPhysicalNics(IReadOnlyList<HvPhysicalNic> nics, List<HealthCheck> out_)
    {
        foreach (var n in nics.Where(n => n.LinkStatus is "Down" or "NotPresent"))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Network", ObjectName = n.NodeName,
                Message = $"{n.NodeName}: NIC '{n.AdapterName}' is DOWN.",
                Detail  = $"{n.Description} | MAC: {n.MacAddress}",
                Recommendation = "Check cable and switch port. Verify SET/LBFO team health." });
        foreach (var n in nics.Where(n => !n.RdmaEnabled && (n.Speed.Contains("25G") || n.Speed.Contains("100G")) && !string.IsNullOrEmpty(n.TeamName)))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Info, Category = "Network", ObjectName = n.NodeName,
                Message = $"{n.NodeName}: high-speed NIC '{n.AdapterName}' ({n.Speed}) — RDMA not enabled.",
                Recommendation = "Enable RDMA (RoCE v2/iWARP) to maximise S2D throughput." });
    }

    // ── Storage ──────────────────────────────────────────────────────────────
    private static void CheckStorage(IReadOnlyList<HvStorage> volumes, List<HealthCheck> out_)
    {
        foreach (var v in volumes.Where(v => v.TotalTB > 0))
        {
            int pct = v.UsedPercent;
            if (pct >= 100 - CsvFreeSpaceCritPercent)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Storage", ObjectName = v.VolumeName,
                    Message = $"CSV '{v.VolumeName}' is critically low: {100-pct}% free ({v.FreeTB:F2} TB).",
                    Recommendation = "Extend the volume, migrate VMs, or delete files immediately." });
            else if (pct >= 100 - CsvFreeSpaceWarnPercent)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Storage", ObjectName = v.VolumeName,
                    Message = $"CSV '{v.VolumeName}' free space below {CsvFreeSpaceWarnPercent}%: {100-pct}% free.",
                    Recommendation = "Plan to expand the volume or move data." });
            if (v.Health is not ("Online" or "Healthy"))
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Storage", ObjectName = v.VolumeName,
                    Message = $"CSV '{v.VolumeName}' health is '{v.Health}'.",
                    Recommendation = "Run Get-VirtualDisk and Get-StorageJob to diagnose." });
        }
    }

    // ── S2D ──────────────────────────────────────────────────────────────────
    private static void CheckS2D(IReadOnlyList<AzureS2DPool> pools, List<HealthCheck> out_)
    {
        foreach (var p in pools)
        {
            if (p.FailedDrives > 0)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Storage", ObjectName = p.PoolName,
                    Message = $"S2D pool '{p.PoolName}': {p.FailedDrives} FAILED drive(s).",
                    Recommendation = "Replace failed drives immediately. S2D auto-repairs when replacements are added." });
            if (p.WarningDrives > 0)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Storage", ObjectName = p.PoolName,
                    Message = $"S2D pool '{p.PoolName}': {p.WarningDrives} drive(s) in warning.",
                    Recommendation = "Run Get-PhysicalDisk | FL to inspect OperationalStatus." });
            if (p.Health != "Healthy")
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Storage", ObjectName = p.PoolName,
                    Message = $"S2D pool '{p.PoolName}' health: '{p.Health}'.",
                    Recommendation = "Run Get-StorageDiagnosticInfo to collect a diagnostic bundle." });
        }
    }

    // ── Duplicate MACs ───────────────────────────────────────────────────────
    private static void CheckDuplicateMacs(IReadOnlyList<VmNic> nics, List<HealthCheck> out_)
    {
        foreach (var g in nics.Where(n => !string.IsNullOrWhiteSpace(n.MacAddress))
                               .GroupBy(n => n.MacAddress).Where(g => g.Count() > 1))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Network",
                ObjectName = g.First().VmName,
                Message = $"Duplicate MAC {g.Key} shared by {g.Count()} NICs: {string.Join(", ", g.Select(n => n.VmName))}",
                Recommendation = "Assign unique static MACs or switch to dynamic MAC assignment." });
    }

    // ── Azure Arc ────────────────────────────────────────────────────────────
    private static void CheckArc(IReadOnlyList<AzureArcResource> arc, List<HealthCheck> out_)
    {
        foreach (var r in arc)
        {
            if (r.ArcStatus is "Disconnected" or "Expired")
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Azure", ObjectName = r.ResourceName,
                    Message = $"Arc resource '{r.ResourceName}' is {r.ArcStatus}.",
                    Detail  = $"Last sync: {r.LastSyncTime?.ToString("yyyy-MM-dd HH:mm") ?? "Unknown"}",
                    Recommendation = "Check himds service and outbound connectivity to management.azure.com." });
            if (!string.IsNullOrEmpty(r.ArcAgentVersion) && CompareVersions(r.ArcAgentVersion, LatestArcAgentVersion) < 0)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Azure", ObjectName = r.ResourceName,
                    Message = $"Arc agent on '{r.ResourceName}' is outdated (v{r.ArcAgentVersion}).",
                    Recommendation = $"Run: azcmagent upgrade --version {LatestArcAgentVersion}" });
        }
    }

    // ── Cluster ──────────────────────────────────────────────────────────────
    private static void CheckCluster(IReadOnlyList<HvCluster> clusters, List<HealthCheck> out_)
    {
        foreach (var c in clusters.Where(c => c.QuorumType == "NodeMajority" && c.NodeCount % 2 == 0))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Cluster", ObjectName = c.ClusterName,
                Message = $"Cluster '{c.ClusterName}': even node count ({c.NodeCount}) with NodeMajority quorum — no witness.",
                Recommendation = "Add a Cloud Witness or file share witness to avoid split-brain." });
    }

    // ── Updates (NEW v1.1) ───────────────────────────────────────────────────
    private static void CheckUpdates(IReadOnlyList<NodeUpdateStatus> updates, List<HealthCheck> out_)
    {
        foreach (var u in updates)
        {
            if (u.Readiness == UpdateReadiness.RequiresReboot)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Update", ObjectName = u.NodeName,
                    Message = $"{u.NodeName} requires a reboot to complete pending updates.",
                    Recommendation = "Schedule a CAU run or drain and restart the node during a maintenance window." });
            if (u.CriticalCount > 0)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Update", ObjectName = u.NodeName,
                    Message = $"{u.NodeName} has {u.CriticalCount} critical pending update(s).",
                    Detail  = u.PendingKBs.Length > 80 ? u.PendingKBs[..80] + "…" : u.PendingKBs,
                    Recommendation = "Apply critical updates via CAU or Windows Update as soon as possible." });
            else if (u.PendingCount > 0)
                out_.Add(new HealthCheck { Severity = HealthSeverity.Info, Category = "Update", ObjectName = u.NodeName,
                    Message = $"{u.NodeName} has {u.PendingCount} pending update(s).",
                    Recommendation = "Schedule a CAU run to apply updates in a rolling fashion." });
            if (u.CauLastResult is "Failed" or "Cancelled")
                out_.Add(new HealthCheck { Severity = HealthSeverity.Error, Category = "Update", ObjectName = u.NodeName,
                    Message = $"Last CAU run on {u.CauLastRun} ended with status: {u.CauLastResult}.",
                    Recommendation = "Review the CAU report with Get-CauReport -ClusterName <name>." });
        }
    }

    // ── Perf (NEW v1.1) ──────────────────────────────────────────────────────
    private static void CheckPerf(IReadOnlyList<PerfSummaryRow> perf, List<HealthCheck> out_)
    {
        // High CPU nodes
        foreach (var r in perf.Where(p => p.MetricName == "Node.CPU.Usage" && p.CurrentValue > 90))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Performance", ObjectName = r.ObjectName,
                Message = $"{r.ObjectName} CPU usage is {r.CurrentValue:F0}%.",
                Recommendation = "Live-migrate CPU-heavy VMs to less-loaded nodes." });

        // High storage latency
        foreach (var r in perf.Where(p => p.MetricName == "Volume.Latency.Average" && p.CurrentValue > 50))
            out_.Add(new HealthCheck { Severity = HealthSeverity.Warning, Category = "Performance", ObjectName = r.ObjectName,
                Message = $"CSV '{r.ObjectName}' average latency is {r.CurrentValue:F1} ms.",
                Recommendation = "Check S2D drive health and network (RDMA) configuration." });
    }

    // ── OK summaries ─────────────────────────────────────────────────────────
    private static void AppendOkSummaries(InventoryResult inv, List<HealthCheck> out_)
    {
        if (inv.Hosts.All(h => string.IsNullOrWhiteSpace(h.NodeStatus) || h.NodeStatus == "Online" || h.NodeStatus == "Up") && inv.Hosts.Count > 0)
            out_.Add(new HealthCheck { Severity = HealthSeverity.OK, Category = "Host",
                ObjectName = "All Nodes", Message = $"All {inv.Hosts.Count} node(s) are online." });
        if (inv.S2DPools.All(p => p.Health == "Healthy" && p.FailedDrives == 0) && inv.S2DPools.Count > 0)
            out_.Add(new HealthCheck { Severity = HealthSeverity.OK, Category = "Storage",
                ObjectName = "S2D Pool", Message = "Storage Spaces Direct pool is healthy with 0 failed drives." });
        if (inv.ArcResources.All(r => r.ArcStatus == "Connected") && inv.ArcResources.Count > 0)
            out_.Add(new HealthCheck { Severity = HealthSeverity.OK, Category = "Azure",
                ObjectName = "Arc Resources", Message = $"All {inv.ArcResources.Count} Arc resource(s) Connected." });
    }

    private static int CompareVersions(string a, string b) =>
        Version.TryParse(a, out var va) && Version.TryParse(b, out var vb)
            ? va.CompareTo(vb)
            : string.Compare(a, b, StringComparison.Ordinal);
}
