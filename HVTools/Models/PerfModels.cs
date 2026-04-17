// =============================================================================
//  PerfModels.cs  — Performance History + Update Status models
// =============================================================================

namespace HVTools.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Performance History  (hvPerf tab)
//  Data pulled from the Storage Spaces Direct Health Service via
//  Get-ClusterPerf / Get-StorageHealthReport in PowerShell.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PerfSample
{
    public DateTime Timestamp   { get; init; }
    public string   ObjectName  { get; init; } = string.Empty;   // cluster, node, or CSV name
    public string   ObjectType  { get; init; } = string.Empty;   // Cluster, Node, Volume, VM
    public string   MetricName  { get; init; } = string.Empty;   // IOPS.Read, Latency.Average, etc.
    public double   Value       { get; init; }
    public string   Unit        { get; init; } = string.Empty;   // IOPS, ms, GB/s, %
}

/// <summary>
/// A named time-series — one per metric/object combination shown in the chart.
/// </summary>
public sealed class PerfSeries
{
    public string             SeriesName { get; init; } = string.Empty;
    public string             Unit       { get; init; } = string.Empty;
    public List<PerfSample>   Samples    { get; init; } = [];
}

/// <summary>
/// Summary row shown in the hvPerf DataGrid — last-known value + sparkline reference.
/// </summary>
public sealed class PerfSummaryRow
{
    public string ObjectName  { get; init; } = string.Empty;
    public string ObjectType  { get; init; } = string.Empty;
    public string MetricName  { get; init; } = string.Empty;
    public double CurrentValue{ get; init; }
    public double PeakValue   { get; init; }
    public double AvgValue    { get; init; }
    public string Unit        { get; init; } = string.Empty;
    public string Trend       { get; init; } = string.Empty;  // ↑ ↓ →
    /// <summary>Reference to the full series for chart rendering.</summary>
    public PerfSeries? Series  { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  Update / CAU Status  (hvUpdate tab)
// ─────────────────────────────────────────────────────────────────────────────
public enum UpdateReadiness { UpToDate, PendingUpdates, RequiresReboot, Unknown }

public sealed class NodeUpdateStatus
{
    public string           NodeName         { get; init; } = string.Empty;
    public UpdateReadiness  Readiness        { get; init; }
    public int              PendingCount     { get; init; }
    public int              CriticalCount    { get; init; }
    public DateTime?        LastChecked      { get; init; }
    public DateTime?        LastInstalled    { get; init; }
    public string           PendingKBs       { get; init; } = string.Empty;  // comma-sep
    public bool             RequiresReboot   { get; init; }
    public string           CauLastRun       { get; init; } = string.Empty;
    public string           CauLastResult    { get; init; } = string.Empty;
    public string           WindowsVersion   { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Multi-cluster session
// ─────────────────────────────────────────────────────────────────────────────
public sealed class ClusterSession
{
    public string           ClusterName   { get; init; } = string.Empty;
    public ConnectionSettings Settings    { get; init; } = new();
    public InventoryResult? Inventory     { get; set; }
    public DateTime?        CollectedAt   { get; set; }
    public string           Status        { get; set; } = "Disconnected";
    public bool             IsActive      { get; set; }
}
