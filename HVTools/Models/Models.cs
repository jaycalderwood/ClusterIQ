// =============================================================================
//  HVTools — Models
//  One file per logical group. All properties use init-only setters so they
//  can be bound directly to WPF DataGrid columns.
// =============================================================================

using System.ComponentModel;

namespace HVTools.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  Connection settings
// ─────────────────────────────────────────────────────────────────────────────
public enum TargetType { HyperVHost, FailoverCluster, AzureLocalCluster }

public sealed class ConnectionSettings
{
    public string HostOrCluster { get; set; } = string.Empty;
    public TargetType TargetType { get; set; } = TargetType.AzureLocalCluster;
    public string? Username { get; set; }
    public string? Password { get; set; }          // held as SecureString in real auth path
    public bool UseCurrentUser { get; set; } = true;

    // Azure Local additional settings
    public string? AzureSubscriptionId { get; set; }
    public string? AzureResourceGroup { get; set; }
    public string? AzureTenantId { get; set; }
    public bool ConnectAzurePlane { get; set; } = false;
}

// ─────────────────────────────────────────────────────────────────────────────
//  VmInfo — one row per virtual machine
// ─────────────────────────────────────────────────────────────────────────────
public sealed class VmInfo
{
    public string Name { get; init; } = string.Empty;
    public string PowerState { get; init; } = string.Empty;   // Running, Off, Saved, Paused
    public string GuestOS { get; init; } = string.Empty;
    public int vCPU { get; init; }
    public long MemoryGB { get; init; }
    public long UsedDiskGB { get; init; }
    public string Host { get; init; } = string.Empty;
    public string Cluster { get; init; } = string.Empty;
    public string IntegrationServices { get; init; } = string.Empty;
    public int NicCount { get; init; }
    public string PrimaryIP { get; init; } = string.Empty;
    public string Uptime { get; init; } = string.Empty;
    public int Checkpoints { get; init; }
    public Guid VmId { get; init; }
    public string Generation { get; init; } = string.Empty;
    public string SecureBoot { get; init; } = string.Empty;
    public string ConfigVersion { get; init; } = string.Empty;
    public bool IsClustered { get; init; }
    public string Notes { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  VmDisk — one row per virtual hard disk
// ─────────────────────────────────────────────────────────────────────────────
public sealed class VmDisk
{
    public string VmName { get; init; } = string.Empty;
    public int DiskNumber { get; init; }
    public string DiskType { get; init; } = string.Empty;   // OS, Data, ISO, Passthrough
    public string Format { get; init; } = string.Empty;     // VHDX, VHD
    public string Path { get; init; } = string.Empty;
    public long SizeGB { get; init; }
    public long UsedGB { get; init; }
    public string Controller { get; init; } = string.Empty; // SCSI, IDE
    public int ControllerNumber { get; init; }
    public int ControllerLocation { get; init; }
    public long IopsLimit { get; init; }
    public bool Shared { get; init; }
    public string FragmentationPercent { get; init; } = string.Empty;
    public bool IsFixed { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VmNic — one row per VM network adapter
// ─────────────────────────────────────────────────────────────────────────────
public sealed class VmNic
{
    public string VmName { get; init; } = string.Empty;
    public string AdapterName { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public bool DynamicMac { get; init; }
    public string SwitchName { get; init; } = string.Empty;
    public string VlanId { get; init; } = string.Empty;
    public string IpAddresses { get; init; } = string.Empty;
    public string IPv6Addresses { get; init; } = string.Empty;
    public long BandwidthMbps { get; init; }
    public bool DhcpGuard { get; init; }
    public bool RouterGuard { get; init; }
    public bool PortMirroring { get; init; }
    public bool IovWeight { get; init; }
    public string MacSpoofing { get; init; } = string.Empty;
    public bool IsConnected { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  VmSnapshot — one row per checkpoint
// ─────────────────────────────────────────────────────────────────────────────
public sealed class VmSnapshot
{
    public string VmName { get; init; } = string.Empty;
    public string SnapshotName { get; init; } = string.Empty;
    public string SnapshotType { get; init; } = string.Empty; // Standard, Production, ProductionOnly
    public DateTime CreationTime { get; init; }
    public int AgeDays => (DateTime.Now - CreationTime).Days;
    public long SizeGB { get; init; }
    public string ParentSnapshotName { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public Guid SnapshotId { get; init; }
    public string Notes { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  HvHost — one row per Hyper-V host node
// ─────────────────────────────────────────────────────────────────────────────
public sealed class HvHost
{
    public string NodeName { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string CpuModel { get; init; } = string.Empty;
    public int CpuSockets { get; init; }
    public int CoresTotal { get; init; }
    public int LogicalProcessors { get; init; }
    public long TotalRamGB { get; init; }
    public long UsedRamGB { get; init; }
    public int RunningVMs { get; init; }
    public string HyperVVersion { get; init; } = string.Empty;
    public string BiosVersion { get; init; } = string.Empty;
    public string NodeStatus { get; init; } = string.Empty;  // Online, Down, Joining
    public string PowerScheme { get; init; } = string.Empty;
    public bool NumaSpanEnabled { get; init; }
    public string LiveMigrationAuth { get; init; } = string.Empty;
    public int MaxLiveMigrations { get; init; }
    public string ClusterName { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  HvCluster — one row per cluster
// ─────────────────────────────────────────────────────────────────────────────
public sealed class HvCluster
{
    public string ClusterName { get; init; } = string.Empty;
    public int NodeCount { get; init; }
    public string QuorumType { get; init; } = string.Empty;
    public string QuorumResource { get; init; } = string.Empty;
    public int CsvVolumeCount { get; init; }
    public int TotalVCpu { get; init; }
    public long TotalRamGB { get; init; }
    public int RunningVMs { get; init; }
    public bool HAEnabled { get; init; }
    public bool S2DEnabled { get; init; }
    public string ClusterStatus { get; init; } = string.Empty;
    public string FunctionalLevel { get; init; } = string.Empty;
    public string NetworkName { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public bool StretchedCluster { get; init; }
    public string SiteCount { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  HvStorage — one row per CSV / volume
// ─────────────────────────────────────────────────────────────────────────────
public sealed class HvStorage
{
    public string VolumeName { get; init; } = string.Empty;
    public string CsvPath { get; init; } = string.Empty;
    public double TotalTB { get; init; }
    public double UsedTB { get; init; }
    public double FreeTB { get; init; }
    public int UsedPercent => TotalTB > 0 ? (int)(UsedTB / TotalTB * 100) : 0;
    public string ResiliencyType { get; init; } = string.Empty;
    public string FileSystem { get; init; } = string.Empty;
    public int VhdCount { get; init; }
    public string Health { get; init; } = string.Empty;
    public string StoragePool { get; init; } = string.Empty;
    public long Iops { get; init; }
    public double LatencyMs { get; init; }
    public string OwnerNode { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  HvSwitch — one row per virtual switch
// ─────────────────────────────────────────────────────────────────────────────
public sealed class HvSwitch
{
    public string SwitchName { get; init; } = string.Empty;
    public string SwitchType { get; init; } = string.Empty;  // External, Internal, Private
    public string BoundNics { get; init; } = string.Empty;
    public string Vlans { get; init; } = string.Empty;
    public int VmsConnected { get; init; }
    public bool SetTeaming { get; init; }
    public bool AllowManagementOS { get; init; }
    public string Bandwidth { get; init; } = string.Empty;
    public string Nodes { get; init; } = string.Empty;
    public bool IovEnabled { get; init; }
    public string Notes { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  HvPhysicalNic — one row per physical host NIC
// ─────────────────────────────────────────────────────────────────────────────
public sealed class HvPhysicalNic
{
    public string NodeName { get; init; } = string.Empty;
    public string AdapterName { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public string Speed { get; init; } = string.Empty;
    public string LinkStatus { get; init; } = string.Empty;  // Up, Down, NotPresent
    public bool RdmaEnabled { get; init; }
    public string VlanId { get; init; } = string.Empty;
    public string TeamName { get; init; } = string.Empty;
    public string DriverVersion { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string InterfaceDescription { get; init; } = string.Empty;
    public bool PfcEnabled { get; init; }
    public bool EtsEnabled { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
//  HealthCheck — one row per finding
// ─────────────────────────────────────────────────────────────────────────────
public enum HealthSeverity { Info, Warning, Error, OK }

public sealed class HealthCheck
{
    public HealthSeverity Severity { get; init; }
    public string Category { get; init; } = string.Empty;  // VM, Host, Storage, Network, Azure
    public string ObjectName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Recommendation { get; init; } = string.Empty;
    public DateTime DetectedAt { get; init; } = DateTime.Now;
}

// ─────────────────────────────────────────────────────────────────────────────
//  AzureArcResource — one row per Arc-registered resource
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AzureArcResource
{
    public string ResourceName { get; init; } = string.Empty;
    public string ResourceType { get; init; } = string.Empty;
    public string ArcStatus { get; init; } = string.Empty;   // Connected, Disconnected, Expired
    public string SubscriptionId { get; init; } = string.Empty;
    public string ResourceGroup { get; init; } = string.Empty;
    public string Region { get; init; } = string.Empty;
    public string ArcAgentVersion { get; init; } = string.Empty;
    public string Extensions { get; init; } = string.Empty;
    public DateTime? LastSyncTime { get; init; }
    public string Tags { get; init; } = string.Empty;
    public string ArcResourceId { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  AzureS2DPool — one row per S2D storage pool
// ─────────────────────────────────────────────────────────────────────────────
public sealed class AzureS2DPool
{
    public string PoolName { get; init; } = string.Empty;
    public string Health { get; init; } = string.Empty;
    public string OperationalStatus { get; init; } = string.Empty;
    public double TotalTB { get; init; }
    public double ProvisionedTB { get; init; }
    public double UsedTB { get; init; }
    public double FreeTB => TotalTB - UsedTB;
    public string FaultTolerance { get; init; } = string.Empty;
    public string CacheMode { get; init; } = string.Empty;
    public int TotalDrives { get; init; }
    public int FailedDrives { get; init; }
    public int WarningDrives { get; init; }
    public string DriveTypes { get; init; } = string.Empty;  // NVMe, SSD, HDD mix
    public string TierSummary { get; init; } = string.Empty;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Inventory — the full collected result set passed around in the app
// ─────────────────────────────────────────────────────────────────────────────
public sealed class InventoryResult
{
    public ConnectionSettings Connection { get; init; } = new();
    public DateTime CollectedAt { get; init; } = DateTime.UtcNow;
    public TimeSpan CollectionDuration { get; init; }

    public IReadOnlyList<VmInfo> VMs { get; init; } = [];
    public IReadOnlyList<VmDisk> VmDisks { get; init; } = [];
    public IReadOnlyList<VmNic> VmNics { get; init; } = [];
    public IReadOnlyList<VmSnapshot> VmSnapshots { get; init; } = [];
    public IReadOnlyList<HvHost> Hosts { get; init; } = [];
    public IReadOnlyList<HvCluster> Clusters { get; init; } = [];
    public IReadOnlyList<HvStorage> Volumes { get; init; } = [];
    public IReadOnlyList<HvSwitch> Switches { get; init; } = [];
    public IReadOnlyList<HvPhysicalNic> PhysicalNics { get; init; } = [];
    public IReadOnlyList<HealthCheck> HealthChecks { get; init; }
    public IReadOnlyList<AlertInsight> AlertInsights { get; init; } = System.Array.Empty<AlertInsight>();
    public IReadOnlyList<DriftChange> DriftChanges { get; init; } = System.Array.Empty<DriftChange>();
    public IReadOnlyList<AzureArcResource> ArcResources { get; init; } = [];
    public IReadOnlyList<AzureS2DPool> S2DPools { get; init; } = [];
}
