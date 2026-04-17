using System.Text.Json.Serialization;

namespace HVTools.Models;

public sealed class EnvironmentSnapshot
{
    public string SnapshotId { get; set; } = Guid.NewGuid().ToString("N");
    public string EnvironmentName { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; } = DateTime.Now;
    public List<SnapshotHost> Hosts { get; set; } = new();
    public List<SnapshotVm> Vms { get; set; } = new();
    public List<SnapshotArc> ArcResources { get; set; } = new();
    public int OverallHealthScore { get; set; }
}

public sealed class SnapshotHost
{
    public string NodeName { get; set; } = string.Empty;
    public string NodeStatus { get; set; } = string.Empty;
    public int RunningVMs { get; set; }
    public int TotalRamGB { get; set; }
    public int UsedRamGB { get; set; }
}

public sealed class SnapshotVm
{
    public string Name { get; set; } = string.Empty;
    public string HostNode { get; set; } = string.Empty;
    public string PowerState { get; set; } = string.Empty;
    public int MemoryAssignedGB { get; set; }
    public int ProcessorCount { get; set; }
}

public sealed class SnapshotArc
{
    public string ResourceName { get; set; } = string.Empty;
    public string ArcStatus { get; set; } = string.Empty;
}

public sealed class DriftChange
{
    public DateTime DetectedAt { get; set; } = DateTime.Now;
    public string Category { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string PreviousValue { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
}
