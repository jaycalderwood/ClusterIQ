using System.IO;
// =============================================================================
//  ExportService  (v1.1 — adds CSV export + update/perf sheets)
// =============================================================================

using System.Text;
using ClosedXML.Excel;
using HVTools.Models;

namespace HVTools.Services;

public sealed class ExportService
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Export All (XLSX)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task ExportAllAsync(InventoryResult inv, string filePath,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>?   perf    = null)
    {
        await Task.Run(() =>
        {
            using var wb = new XLWorkbook();
            wb.Style.Font.FontName = "Segoe UI";
            wb.Style.Font.FontSize = 10;

            WriteSheet(wb, "vmInfo",     VmInfoHeaders,     VmInfoRows(inv.VMs));
            WriteSheet(wb, "vmDisk",     VmDiskHeaders,     VmDiskRows(inv.VmDisks));
            WriteSheet(wb, "vmNIC",      VmNicHeaders,      VmNicRows(inv.VmNics));
            WriteSheet(wb, "vmSnapshot", VmSnapHeaders,     VmSnapRows(inv.VmSnapshots));
            WriteSheet(wb, "hvHost",     HvHostHeaders,     HvHostRows(inv.Hosts));
            WriteSheet(wb, "hvCluster",  HvClusterHeaders,  HvClusterRows(inv.Clusters));
            WriteSheet(wb, "hvStorage",  HvStorageHeaders,  HvStorageRows(inv.Volumes));
            WriteSheet(wb, "hvSwitch",   HvSwitchHeaders,   HvSwitchRows(inv.Switches));
            WriteSheet(wb, "hvNIC",      HvNicHeaders,      HvNicRows(inv.PhysicalNics));
            WriteSheet(wb, "hvHealth",   HvHealthHeaders,   HvHealthRows(inv.HealthChecks));
            WriteSheet(wb, "azArc",      AzArcHeaders,      AzArcRows(inv.ArcResources));
            WriteSheet(wb, "azS2D",      AzS2DHeaders,      AzS2DRows(inv.S2DPools));

            if (updates is not null && updates.Count > 0)
                WriteSheet(wb, "hvUpdate", UpdateHeaders, UpdateRows(updates));
            if (perf is not null && perf.Count > 0)
                WriteSheet(wb, "hvPerf", PerfHeaders, PerfRows(perf));

            WriteSummarySheet(wb, inv, updates);
            wb.SaveAs(filePath);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Export Current Tab (XLSX or CSV)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task ExportCurrentTabAsync(InventoryResult inv, string tabName,
        string filePath, bool asCsv = false,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>?   perf    = null)
    {
        if (asCsv)
        {
            await ExportTabAsCsvAsync(inv, tabName, filePath, updates, perf);
            return;
        }

        await Task.Run(() =>
        {
            using var wb = new XLWorkbook();
            (string[] headers, IEnumerable<object?[]> rows) = tabName switch
            {
                "vmInfo"     => (VmInfoHeaders,    VmInfoRows(inv.VMs)),
                "vmDisk"     => (VmDiskHeaders,    VmDiskRows(inv.VmDisks)),
                "vmNIC"      => (VmNicHeaders,     VmNicRows(inv.VmNics)),
                "vmSnapshot" => (VmSnapHeaders,    VmSnapRows(inv.VmSnapshots)),
                "hvHost"     => (HvHostHeaders,    HvHostRows(inv.Hosts)),
                "hvCluster"  => (HvClusterHeaders, HvClusterRows(inv.Clusters)),
                "hvStorage"  => (HvStorageHeaders, HvStorageRows(inv.Volumes)),
                "hvSwitch"   => (HvSwitchHeaders,  HvSwitchRows(inv.Switches)),
                "hvNIC"      => (HvNicHeaders,      HvNicRows(inv.PhysicalNics)),
                "hvHealth"   => (HvHealthHeaders,  HvHealthRows(inv.HealthChecks)),
                "azArc"      => (AzArcHeaders,     AzArcRows(inv.ArcResources)),
                "azS2D"      => (AzS2DHeaders,     AzS2DRows(inv.S2DPools)),
                "hvUpdate"   => (UpdateHeaders,    UpdateRows(updates ?? [])),
                "hvPerf"     => (PerfHeaders,      PerfRows(perf ?? [])),
                _            => (VmInfoHeaders,    VmInfoRows(inv.VMs)),
            };
            WriteSheet(wb, tabName, headers, rows);
            wb.SaveAs(filePath);
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CSV Export
    // ─────────────────────────────────────────────────────────────────────────
    public async Task ExportTabAsCsvAsync(InventoryResult inv, string tabName,
        string filePath,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>?   perf    = null)
    {
        (string[] headers, IEnumerable<object?[]> rows) = tabName switch
        {
            "vmInfo"     => (VmInfoHeaders,    VmInfoRows(inv.VMs)),
            "vmDisk"     => (VmDiskHeaders,    VmDiskRows(inv.VmDisks)),
            "vmNIC"      => (VmNicHeaders,     VmNicRows(inv.VmNics)),
            "vmSnapshot" => (VmSnapHeaders,    VmSnapRows(inv.VmSnapshots)),
            "hvHost"     => (HvHostHeaders,    HvHostRows(inv.Hosts)),
            "hvCluster"  => (HvClusterHeaders, HvClusterRows(inv.Clusters)),
            "hvStorage"  => (HvStorageHeaders, HvStorageRows(inv.Volumes)),
            "hvSwitch"   => (HvSwitchHeaders,  HvSwitchRows(inv.Switches)),
            "hvNIC"      => (HvNicHeaders,      HvNicRows(inv.PhysicalNics)),
            "hvHealth"   => (HvHealthHeaders,  HvHealthRows(inv.HealthChecks)),
            "azArc"      => (AzArcHeaders,     AzArcRows(inv.ArcResources)),
            "azS2D"      => (AzS2DHeaders,     AzS2DRows(inv.S2DPools)),
            "hvUpdate"   => (UpdateHeaders,    UpdateRows(updates ?? [])),
            "hvPerf"     => (PerfHeaders,      PerfRows(perf ?? [])),
            _            => (VmInfoHeaders,    VmInfoRows(inv.VMs)),
        };

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", headers.Select(CsvEscape)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(v => CsvEscape(v?.ToString() ?? ""))));

        await File.WriteAllTextAsync(filePath, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>Export all tabs as individual CSV files in a folder.</summary>
    public async Task ExportAllAsCsvAsync(InventoryResult inv, string folderPath,
        IReadOnlyList<NodeUpdateStatus>? updates = null,
        IReadOnlyList<PerfSummaryRow>?   perf    = null)
    {
        Directory.CreateDirectory(folderPath);
        var tabs = new[] { "vmInfo","vmDisk","vmNIC","vmSnapshot","hvHost","hvCluster",
                           "hvStorage","hvSwitch","hvNIC","hvHealth","azArc","azS2D" };
        foreach (var tab in tabs)
            await ExportTabAsCsvAsync(inv, tab, Path.Combine(folderPath, $"{tab}.csv"), updates, perf);
        if (updates?.Count > 0)
            await ExportTabAsCsvAsync(inv, "hvUpdate", Path.Combine(folderPath, "hvUpdate.csv"), updates);
        if (perf?.Count > 0)
            await ExportTabAsCsvAsync(inv, "hvPerf", Path.Combine(folderPath, "hvPerf.csv"), perf: perf);
    }

    private static string CsvEscape(string? value)
    {
        if (value is null) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Sheet writer
    // ─────────────────────────────────────────────────────────────────────────
    private static void WriteSheet(XLWorkbook wb, string name,
        string[] headers, IEnumerable<object?[]> rows)
    {
        var ws = wb.Worksheets.Add(name);
        var headerRow = ws.Row(1);
        headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0, 70, 127);
        headerRow.Style.Font.FontColor = XLColor.White;
        headerRow.Style.Font.Bold = true;
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];

        int rowIdx = 2;
        bool shade = false;
        foreach (var row in rows)
        {
            if (shade) ws.Row(rowIdx).Style.Fill.BackgroundColor = XLColor.FromArgb(242, 246, 251);
            for (int c = 0; c < row.Length && c < headers.Length; c++)
            {
                var cell = ws.Cell(rowIdx, c + 1);
                cell.Value = row[c] switch
                {
                    null       => XLCellValue.FromObject(""),
                    bool b     => b ? "Yes" : "No",
                    DateTime d => XLCellValue.FromObject(d.ToString("yyyy-MM-dd HH:mm")),
                    _          => XLCellValue.FromObject(row[c]!.ToString() ?? ""),
                };
            }
            if (name == "hvHealth" && row.Length > 0)
            {
                var sev = row[0]?.ToString() ?? "";
                ws.Row(rowIdx).Style.Fill.BackgroundColor = sev switch
                {
                    "Error"   => XLColor.FromArgb(255, 235, 238),
                    "Warning" => XLColor.FromArgb(255, 248, 225),
                    "OK"      => XLColor.FromArgb(232, 245, 233),
                    _         => XLColor.FromArgb(227, 242, 253),
                };
            }
            if (name == "hvUpdate" && row.Length > 1)
            {
                var readiness = row[1]?.ToString() ?? "";
                if (readiness == "RequiresReboot")
                    ws.Row(rowIdx).Style.Fill.BackgroundColor = XLColor.FromArgb(255, 248, 225);
                else if (readiness == "PendingUpdates")
                    ws.Row(rowIdx).Style.Fill.BackgroundColor = XLColor.FromArgb(227, 242, 253);
            }
            shade = !shade;
            rowIdx++;
        }
        ws.Columns().AdjustToContents(8, 60);
        ws.SheetView.FreezeRows(1);
        if (rowIdx > 2) ws.RangeUsed()?.SetAutoFilter();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Summary sheet
    // ─────────────────────────────────────────────────────────────────────────
    private static void WriteSummarySheet(XLWorkbook wb, InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus>? updates)
    {
        var ws = wb.Worksheets.Add("Summary");
        ws.Column(1).Width = 30; ws.Column(2).Width = 20;

        void Header(string t, int r) {
            ws.Cell(r, 1).Value = t;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 1).Style.Fill.BackgroundColor = XLColor.FromArgb(0, 70, 127);
            ws.Cell(r, 1).Style.Font.FontColor = XLColor.White;
            ws.Range(r, 1, r, 2).Merge();
        }
        void Row(string l, object v, int r) {
            ws.Cell(r, 1).Value = l;
            ws.Cell(r, 2).Value = XLCellValue.FromObject(v.ToString() ?? "");
        }

        ws.Cell(1,1).Value = "ClusterIQ v1.0 — Executive Summary";
        ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 14;
        ws.Cell(2,1).Value = $"Collected: {inv.CollectedAt:yyyy-MM-dd HH:mm} UTC";
        ws.Cell(3,1).Value = $"Target: {inv.Connection.HostOrCluster}";
        ws.Cell(4,1).Value = $"Duration: {inv.CollectionDuration.TotalSeconds:F1}s";

        Header("Virtual Machines", 6);
        Row("Total VMs",     inv.VMs.Count, 7);
        Row("Running",       inv.VMs.Count(v => v.PowerState == "Running"), 8);
        Row("Off",           inv.VMs.Count(v => v.PowerState == "Off"), 9);
        Row("Total vCPUs",   inv.VMs.Sum(v => v.vCPU), 10);
        Row("Total RAM (GB)",inv.VMs.Sum(v => v.MemoryGB), 11);
        Row("Snapshots",     inv.VmSnapshots.Count, 12);

        Header("Hosts", 14);
        Row("Nodes",         inv.Hosts.Count, 15);
        Row("Online",        inv.Hosts.Count(h => h.NodeStatus == "Online"), 16);
        Row("Total RAM (GB)",inv.Hosts.Sum(h => h.TotalRamGB), 17);

        Header("Storage", 19);
        Row("CSV Volumes",   inv.Volumes.Count, 20);
        Row("Total (TB)",    inv.Volumes.Sum(v => v.TotalTB).ToString("F2"), 21);
        Row("Used (TB)",     inv.Volumes.Sum(v => v.UsedTB).ToString("F2"), 22);

        Header("Health", 24);
        Row("Overall Health Score", ExtractOverallHealthScore(inv.HealthChecks), 25);
        Row("Errors",        inv.HealthChecks.Count(h => h.Severity == HealthSeverity.Error), 26);
        Row("Warnings",      inv.HealthChecks.Count(h => h.Severity == HealthSeverity.Warning), 27);
        Row("Recommendations", inv.HealthChecks.Count(h => h.Category == "Recommendation"), 28);
        Row("Imbalance Findings", inv.HealthChecks.Count(h => h.Category == "Imbalance"), 29);
        Row("Open Alerts", inv.AlertInsights.Count(a => a.Status == AlertStatus.Open), 30);
        Row("High/Critical Alerts", inv.AlertInsights.Count(a => a.Priority is AlertPriority.High or AlertPriority.Critical), 31);
        Row("Snapshot Changes", inv.DriftChanges.Count, 32);
        Row("Compliance Findings", inv.HealthChecks.Count(h => h.Category == "Compliance"), 33);
        Row("OK",            inv.HealthChecks.Count(h => h.Severity == HealthSeverity.OK), 34);

        if (updates is not null)
        {
            Header("Updates", 34);
            Row("Nodes requiring reboot", updates.Count(u => u.Readiness == UpdateReadiness.RequiresReboot), 35);
            Row("Nodes with critical updates", updates.Count(u => u.CriticalCount > 0), 36);
            Row("Nodes up to date", updates.Count(u => u.Readiness == UpdateReadiness.UpToDate), 37);
        }

        ws.SheetView.FreezeRows(1);
    }


private static int ExtractOverallHealthScore(IReadOnlyList<HealthCheck> checks)
{
    var overall = checks.FirstOrDefault(h => h.Category == "Score" && h.Message.StartsWith("Overall environment health score", StringComparison.OrdinalIgnoreCase));
    if (overall is null) return 0;

    var match = System.Text.RegularExpressions.Regex.Match(overall.Message, @"(\d+)/100");
    return match.Success && int.TryParse(match.Groups[1].Value, out var score) ? score : 0;
}

// ─────────────────────────────────────────────────────────────────────────
//  Headers & row mappers — existing tabs
    // ─────────────────────────────────────────────────────────────────────────
    internal static readonly string[] VmInfoHeaders = [
        "Name","Power State","Guest OS","vCPU","Mem (GB)","Used Disk (GB)",
        "Host","Cluster","Integration Svcs","NIC Count","IP Address",
        "Uptime","Checkpoints","VM ID","Generation","Secure Boot","Config Ver","Clustered","Notes"
    ];
    internal static IEnumerable<object?[]> VmInfoRows(IReadOnlyList<VmInfo> d) =>
        d.Select(v => new object?[]{v.Name,v.PowerState,v.GuestOS,v.vCPU,v.MemoryGB,v.UsedDiskGB,
            v.Host,v.Cluster,v.IntegrationServices,v.NicCount,v.PrimaryIP,
            v.Uptime,v.Checkpoints,v.VmId,v.Generation,v.SecureBoot,v.ConfigVersion,v.IsClustered,v.Notes});

    internal static readonly string[] VmDiskHeaders = [
        "VM Name","Disk #","Type","Format","Path","Size (GB)","Used (GB)",
        "Controller","Ctrl #","Ctrl Loc","IOPS Limit","Shared","Fixed"
    ];
    internal static IEnumerable<object?[]> VmDiskRows(IReadOnlyList<VmDisk> d) =>
        d.Select(v => new object?[]{v.VmName,v.DiskNumber,v.DiskType,v.Format,v.Path,v.SizeGB,v.UsedGB,
            v.Controller,v.ControllerNumber,v.ControllerLocation,v.IopsLimit,v.Shared,v.IsFixed});

    internal static readonly string[] VmNicHeaders = [
        "VM Name","Adapter","MAC","Dynamic MAC","Switch","VLAN","IP Addresses",
        "Bandwidth (Mbps)","DHCP Guard","Router Guard","MAC Spoofing","Connected"
    ];
    internal static IEnumerable<object?[]> VmNicRows(IReadOnlyList<VmNic> d) =>
        d.Select(n => new object?[]{n.VmName,n.AdapterName,n.MacAddress,n.DynamicMac,n.SwitchName,n.VlanId,
            n.IpAddresses,n.BandwidthMbps,n.DhcpGuard,n.RouterGuard,n.MacSpoofing,n.IsConnected});

    internal static readonly string[] VmSnapHeaders = [
        "VM Name","Snapshot Name","Type","Created","Age (days)","Size (GB)","Parent","Host","Notes"
    ];
    internal static IEnumerable<object?[]> VmSnapRows(IReadOnlyList<VmSnapshot> d) =>
        d.Select(s => new object?[]{s.VmName,s.SnapshotName,s.SnapshotType,s.CreationTime,s.AgeDays,
            s.SizeGB,s.ParentSnapshotName,s.Host,s.Notes});

    internal static readonly string[] HvHostHeaders = [
        "Node","OS","Build","CPU Model","Sockets","Cores","Logical Procs",
        "Total RAM (GB)","Used RAM (GB)","Running VMs","HV Ver","BIOS","Status",
        "NUMA Span","Max Live Mig","Live Mig Auth","Cluster"
    ];
    internal static IEnumerable<object?[]> HvHostRows(IReadOnlyList<HvHost> d) =>
        d.Select(h => new object?[]{h.NodeName,h.OperatingSystem,h.OsBuild,h.CpuModel,h.CpuSockets,
            h.CoresTotal,h.LogicalProcessors,h.TotalRamGB,h.UsedRamGB,h.RunningVMs,
            h.HyperVVersion,h.BiosVersion,h.NodeStatus,h.NumaSpanEnabled,
            h.MaxLiveMigrations,h.LiveMigrationAuth,h.ClusterName});

    internal static readonly string[] HvClusterHeaders = [
        "Cluster","Nodes","Quorum Type","Quorum Resource","CSV Volumes","Total vCPU",
        "Total RAM (GB)","Running VMs","HA","S2D","Status","Functional Level","Domain","Stretched"
    ];
    internal static IEnumerable<object?[]> HvClusterRows(IReadOnlyList<HvCluster> d) =>
        d.Select(c => new object?[]{c.ClusterName,c.NodeCount,c.QuorumType,c.QuorumResource,c.CsvVolumeCount,
            c.TotalVCpu,c.TotalRamGB,c.RunningVMs,c.HAEnabled,c.S2DEnabled,
            c.ClusterStatus,c.FunctionalLevel,c.Domain,c.StretchedCluster});

    internal static readonly string[] HvStorageHeaders = [
        "Volume","CSV Path","Total (TB)","Used (TB)","Free (TB)","Used %",
        "Resiliency","FileSystem","VHDs","Health","Pool","IOPS","Latency (ms)","Owner"
    ];
    internal static IEnumerable<object?[]> HvStorageRows(IReadOnlyList<HvStorage> d) =>
        d.Select(v => new object?[]{v.VolumeName,v.CsvPath,v.TotalTB,v.UsedTB,v.FreeTB,v.UsedPercent,
            v.ResiliencyType,v.FileSystem,v.VhdCount,v.Health,v.StoragePool,v.Iops,v.LatencyMs,v.OwnerNode});

    internal static readonly string[] HvSwitchHeaders = [
        "Switch","Type","Bound NICs","VMs","SET","Mgmt OS","IOV","Nodes","Notes"
    ];
    internal static IEnumerable<object?[]> HvSwitchRows(IReadOnlyList<HvSwitch> d) =>
        d.Select(s => new object?[]{s.SwitchName,s.SwitchType,s.BoundNics,s.VmsConnected,s.SetTeaming,
            s.AllowManagementOS,s.IovEnabled,s.Nodes,s.Notes});

    internal static readonly string[] HvNicHeaders = [
        "Node","Adapter","MAC","Speed","Status","RDMA","VLAN","Team","Driver","Description","PFC"
    ];
    internal static IEnumerable<object?[]> HvNicRows(IReadOnlyList<HvPhysicalNic> d) =>
        d.Select(n => new object?[]{n.NodeName,n.AdapterName,n.MacAddress,n.Speed,n.LinkStatus,
            n.RdmaEnabled,n.VlanId,n.TeamName,n.DriverVersion,n.Description,n.PfcEnabled});

    internal static readonly string[] HvHealthHeaders = [
        "Severity","Category","Object","Message","Detail","Recommendation","Detected At"
    ];
    internal static IEnumerable<object?[]> HvHealthRows(IReadOnlyList<HealthCheck> d) =>
        d.Select(h => new object?[]{h.Severity.ToString(),h.Category,h.ObjectName,
            h.Message,h.Detail,h.Recommendation,h.DetectedAt});

    internal static readonly string[] AzArcHeaders = [
        "Resource","Type","Arc Status","Subscription","Resource Group","Region",
        "Agent Version","Extensions","Last Sync","Tags","Resource ID"
    ];
    internal static IEnumerable<object?[]> AzArcRows(IReadOnlyList<AzureArcResource> d) =>
        d.Select(r => new object?[]{r.ResourceName,r.ResourceType,r.ArcStatus,r.SubscriptionId,
            r.ResourceGroup,r.Region,r.ArcAgentVersion,r.Extensions,r.LastSyncTime,r.Tags,r.ArcResourceId});

    internal static readonly string[] AzS2DHeaders = [
        "Pool","Health","Operational","Total (TB)","Provisioned (TB)","Used (TB)","Free (TB)",
        "Fault Tolerance","Cache Mode","Drives","Failed","Warning","Drive Types","Tiers"
    ];
    internal static IEnumerable<object?[]> AzS2DRows(IReadOnlyList<AzureS2DPool> d) =>
        d.Select(p => new object?[]{p.PoolName,p.Health,p.OperationalStatus,p.TotalTB,p.ProvisionedTB,
            p.UsedTB,p.FreeTB,p.FaultTolerance,p.CacheMode,
            p.TotalDrives,p.FailedDrives,p.WarningDrives,p.DriveTypes,p.TierSummary});

    // ─── NEW v1.1 tabs ────────────────────────────────────────────────────────
    internal static readonly string[] UpdateHeaders = [
        "Node","Readiness","Pending","Critical","Requires Reboot",
        "Last Installed","Pending KBs","CAU Last Run","CAU Result","Windows Version"
    ];
    internal static IEnumerable<object?[]> UpdateRows(IReadOnlyList<NodeUpdateStatus> d) =>
        d.Select(u => new object?[]{u.NodeName,u.Readiness.ToString(),u.PendingCount,u.CriticalCount,
            u.RequiresReboot,u.LastInstalled,u.PendingKBs,u.CauLastRun,u.CauLastResult,u.WindowsVersion});

    internal static readonly string[] PerfHeaders = [
        "Object","Type","Metric","Current","Peak","Average","Unit","Trend"
    ];
    internal static IEnumerable<object?[]> PerfRows(IReadOnlyList<PerfSummaryRow> d) =>
        d.Select(p => new object?[]{p.ObjectName,p.ObjectType,p.MetricName,
            $"{p.CurrentValue:F1}",$"{p.PeakValue:F1}",$"{p.AvgValue:F1}",p.Unit,p.Trend});

    // Helper used by MainViewModel for single-tab XLSX export
    public static string[] GetHeaders(string tab) => tab switch
    {
        "vmInfo"     => VmInfoHeaders,
        "vmDisk"     => VmDiskHeaders,
        "vmNIC"      => VmNicHeaders,
        "vmSnapshot" => VmSnapHeaders,
        "hvHost"     => HvHostHeaders,
        "hvCluster"  => HvClusterHeaders,
        "hvStorage"  => HvStorageHeaders,
        "hvSwitch"   => HvSwitchHeaders,
        "hvNIC"      => HvNicHeaders,
        "hvHealth"   => HvHealthHeaders,
        "azArc"      => AzArcHeaders,
        "azS2D"      => AzS2DHeaders,
        "hvUpdate"   => UpdateHeaders,
        "hvPerf"     => PerfHeaders,
        _            => VmInfoHeaders,
    };



private static string[] AlertHeaders() => ["Detected", "Object", "Category", "Severity", "Priority", "Status", "Trigger", "Impact", "Recommended Action", "Recommendation Category"];

private static object?[] AlertCells(AlertInsight a) =>
[
    a.DetectedAt,
    a.ObjectName,
    a.Category,
    a.Severity.ToString(),
    a.Priority.ToString(),
    a.Status.ToString(),
    a.TriggerReason,
    a.Impact,
    a.RecommendedAction,
    a.RecommendationCategory
];



private static string[] ChangeHeaders() => ["Detected", "Category", "Object", "Change Type", "Previous", "Current", "Impact"];

private static object?[] ChangeCells(DriftChange c) =>
[
    c.DetectedAt,
    c.Category,
    c.ObjectName,
    c.ChangeType,
    c.PreviousValue,
    c.CurrentValue,
    c.Impact
];

}