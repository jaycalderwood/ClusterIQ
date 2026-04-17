// =============================================================================
//  HyperVService  (v1.1 — PS scripts use @"..." verbatim strings)
// =============================================================================

using HVTools.Models;
using Microsoft.Extensions.Logging;

namespace HVTools.Services;

public sealed class ClusterMigrationResult
{
    public string VmName { get; init; } = string.Empty;
    public string SourceHost { get; init; } = string.Empty;
    public string DestinationHost { get; init; } = string.Empty;
    public string CurrentOwner { get; init; } = string.Empty;
    public string ClusterRole { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}


public sealed class HyperVService
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<HyperVService> _logger;

    public HyperVService(PowerShellRunner ps, ILogger<HyperVService> logger)
    {
        _ps = ps; _logger = logger;
    }


    private static string EscapePs(string value) => value.Replace("'", "''");

    private static string ClusterPrelude(string target) =>
        "$t = '" + EscapePs(target) + "'\n" + """
function Invoke-HVCommand {
    param(
        [string]$ComputerName,
        [scriptblock]$ScriptBlock,
        [object[]]$ArgumentList = @()
    )
    if ($null -ne $script:HVToolsCredential) {
        Invoke-Command -ComputerName $ComputerName -Credential $script:HVToolsCredential -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList -ErrorAction SilentlyContinue
    } else {
        Invoke-Command -ComputerName $ComputerName -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList -ErrorAction SilentlyContinue
    }
}

function Get-HVNodes {
    param([string]$ClusterOrHost)

    $nodes = @()
    try {
        if ($null -ne $script:HVToolsCredential) {
            $nodes = @(Invoke-HVCommand -ComputerName $ClusterOrHost -ScriptBlock {
                param($cluster)
                Import-Module FailoverClusters -ErrorAction SilentlyContinue
                try {
                    @(Get-ClusterNode -Cluster $cluster -ErrorAction Stop | Select-Object -ExpandProperty Name)
                } catch {
                    @()
                }
            } -ArgumentList @($ClusterOrHost))
        } else {
            $nodes = @(Get-ClusterNode -Cluster $ClusterOrHost -ErrorAction Stop | Select-Object -ExpandProperty Name)
        }
    } catch {
        $nodes = @()
    }

    if (-not $nodes -or $nodes.Count -eq 0) {
        $nodes = @($ClusterOrHost)
    }

    $nodes
}

$nodes = @(Get-HVNodes -ClusterOrHost $t)
""" + "\n";


    public async Task<(
        IReadOnlyList<VmInfo>        VMs,
        IReadOnlyList<VmDisk>        Disks,
        IReadOnlyList<VmNic>         Nics,
        IReadOnlyList<VmSnapshot>    Snapshots,
        IReadOnlyList<HvHost>        Hosts,
        IReadOnlyList<HvCluster>     Clusters,
        IReadOnlyList<HvStorage>     Volumes,
        IReadOnlyList<HvSwitch>      Switches,
        IReadOnlyList<HvPhysicalNic> PhysicalNics
    )> CollectAllAsync(string target, CancellationToken ct = default)
    {
        var hostsTask    = GetHostsAsync(target, ct);
        var clusterTask  = GetClustersAsync(target, ct);
        var switchesTask = GetSwitchesAsync(target, ct);
        var physNicsTask = GetPhysicalNicsAsync(target, ct);
        var storageTask  = GetStorageVolumesAsync(target, ct);
        await Task.WhenAll(hostsTask, clusterTask, switchesTask, physNicsTask, storageTask);
        var hosts = await hostsTask;
        var vmsTask       = GetVMsAsync(target, hosts, ct);
        var disksTask     = GetVMDisksAsync(target, ct);
        var nicsTask      = GetVMNicsAsync(target, ct);
        var snapshotsTask = GetVMSnapshotsAsync(target, ct);
        await Task.WhenAll(vmsTask, disksTask, nicsTask, snapshotsTask);
        return (await vmsTask, await disksTask, await nicsTask, await snapshotsTask,
                hosts, await clusterTask, await storageTask, await switchesTask, await physNicsTask);
    }

    // ─── VMs ─────────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<VmInfo>> GetVMsAsync(string target,
        IReadOnlyList<HvHost> hosts, CancellationToken ct = default)
    {
        // Inject target as a PS variable so the rest is a plain verbatim string
        string script = ClusterPrelude(target) + """
foreach ($node in $nodes) {
    Invoke-HVCommand -ComputerName $node -ArgumentList @($t) -ScriptBlock {
        param($cluster)
        Import-Module Hyper-V -ErrorAction SilentlyContinue
        Import-Module FailoverClusters -ErrorAction SilentlyContinue

        Get-VM -ErrorAction SilentlyContinue | ForEach-Object {
            $vm = $_
            $is = Get-VMIntegrationService -VMName $vm.Name -ErrorAction SilentlyContinue
            $isVer = ($is | Where-Object { $_.Name -eq 'Guest Service Interface' } |
                      Select-Object -ExpandProperty Version -ErrorAction SilentlyContinue)
            $nics = Get-VMNetworkAdapter -VMName $vm.Name -ErrorAction SilentlyContinue
            $ips = ($nics.IPAddresses | Where-Object { $_ -match '^\d+\.\d+' }) -join ', '
            $snaps = (Get-VMSnapshot -VMName $vm.Name -ErrorAction SilentlyContinue | Measure-Object).Count
            $up = if ($vm.State -eq 'Running') {
                $u = $vm.Uptime
                "$($u.Days)d $($u.Hours)h $($u.Minutes)m"
            } else { '' }

            [PSCustomObject]@{
                Name            = $vm.Name
                State           = $vm.State.ToString()
                GuestOS         = $vm.Notes
                ProcessorCount  = $vm.ProcessorCount
                MemoryGB        = [math]::Round($vm.MemoryAssigned / 1GB, 0)
                Host            = $env:COMPUTERNAME
                IsClustered     = ($null -ne (Get-ClusterResource -Cluster $cluster -ErrorAction SilentlyContinue |
                                    Where-Object { $_.OwnerGroup -eq $vm.Name }))
                Generation      = $vm.Generation
                ConfigVersion   = $vm.Version
                SecureBoot      = $vm.SecureBoot
                NicCount        = @($nics).Count
                PrimaryIP       = $ips
                Uptime          = $up
                Checkpoints     = $snaps
                IntegrationSvcs = if ($isVer) { $isVer } else { 'Unknown' }
                VmId            = $vm.VMId
                Notes           = $vm.Notes
            }
        }
    }
}
""";
        var results = await _ps.RunScriptAsync(script, ct);
        var list = new List<VmInfo>(results.Count);
        foreach (var obj in results)
            list.Add(new VmInfo {
                Name                = PowerShellRunner.GetStr(obj, "Name"),
                PowerState          = PowerShellRunner.GetStr(obj, "State"),
                GuestOS             = PowerShellRunner.GetStr(obj, "GuestOS"),
                vCPU                = PowerShellRunner.GetInt(obj, "ProcessorCount"),
                MemoryGB            = PowerShellRunner.GetLong(obj, "MemoryGB"),
                Host                = PowerShellRunner.GetStr(obj, "Host"),
                IsClustered         = PowerShellRunner.GetBool(obj, "IsClustered"),
                Generation          = PowerShellRunner.GetStr(obj, "Generation"),
                ConfigVersion       = PowerShellRunner.GetStr(obj, "ConfigVersion"),
                SecureBoot          = PowerShellRunner.GetStr(obj, "SecureBoot"),
                NicCount            = PowerShellRunner.GetInt(obj, "NicCount"),
                PrimaryIP           = PowerShellRunner.GetStr(obj, "PrimaryIP"),
                Uptime              = PowerShellRunner.GetStr(obj, "Uptime"),
                Checkpoints         = PowerShellRunner.GetInt(obj, "Checkpoints"),
                IntegrationServices = PowerShellRunner.GetStr(obj, "IntegrationSvcs"),
                Notes               = PowerShellRunner.GetStr(obj, "Notes"),
            });
        _logger.LogInformation("Collected {Count} VMs", list.Count);

        return list;
    }


private async Task<IReadOnlyList<HvHost>> GetHostsAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + @"
foreach ($node in $nodes) {
    Invoke-HVCommand -ComputerName $node -ArgumentList @($t) -ScriptBlock {
        param($clusterName)
        Import-Module Hyper-V -ErrorAction SilentlyContinue

        $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
        $cpu = @(Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue)
        $cs  = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
        $bios = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue | Select-Object -First 1
        $hvh = Get-VMHost -ErrorAction SilentlyContinue
        $running = @(Get-VM -ErrorAction SilentlyContinue | Where-Object State -eq 'Running')
        $totalRamGB = [int64][math]::Round(($cs.TotalPhysicalMemory / 1GB), 0)
        $usedRamGB  = [int64][math]::Round((($running | Measure-Object MemoryAssigned -Sum).Sum / 1GB), 0)

        [pscustomobject]@{
            NodeName          = $env:COMPUTERNAME
            OperatingSystem   = $os.Caption
            OsBuild           = $os.BuildNumber
            CpuModel          = ($cpu | Select-Object -First 1 -ExpandProperty Name)
            CpuSockets        = @($cpu).Count
            CoresTotal        = (@($cpu) | Measure-Object NumberOfCores -Sum).Sum
            LogicalProcessors = (@($cpu) | Measure-Object NumberOfLogicalProcessors -Sum).Sum
            TotalRamGB        = $totalRamGB
            UsedRamGB         = $usedRamGB
            RunningVMs        = @($running).Count
            HyperVVersion     = $hvh.Version
            BiosVersion       = $bios.SMBIOSBIOSVersion
            NodeStatus        = 'Up'
            PowerScheme       = ''
            NumaSpanEnabled   = [bool]$hvh.NumaSpanningEnabled
            LiveMigrationAuth = $hvh.VirtualMachineMigrationAuthenticationType
            MaxLiveMigrations = [int]$hvh.MaximumVirtualMachineMigrations
            ClusterName       = $clusterName
        }
    }
}
";
    var results = await _ps.RunScriptAsync(script, ct);

    var list = new List<HvHost>();
    foreach (var obj in results)
    {
        list.Add(new HvHost
        {
            NodeName = PowerShellRunner.GetStr(obj, "NodeName"),
            OperatingSystem = PowerShellRunner.GetStr(obj, "OperatingSystem"),
            OsBuild = PowerShellRunner.GetStr(obj, "OsBuild"),
            CpuModel = PowerShellRunner.GetStr(obj, "CpuModel"),
            CpuSockets = PowerShellRunner.GetInt(obj, "CpuSockets"),
            CoresTotal = PowerShellRunner.GetInt(obj, "CoresTotal"),
            LogicalProcessors = PowerShellRunner.GetInt(obj, "LogicalProcessors"),
            TotalRamGB = PowerShellRunner.GetLong(obj, "TotalRamGB"),
            UsedRamGB = PowerShellRunner.GetLong(obj, "UsedRamGB"),
            RunningVMs = PowerShellRunner.GetInt(obj, "RunningVMs"),
            HyperVVersion = PowerShellRunner.GetStr(obj, "HyperVVersion"),
            BiosVersion = PowerShellRunner.GetStr(obj, "BiosVersion"),
            NodeStatus = PowerShellRunner.GetStr(obj, "NodeStatus"),
            PowerScheme = PowerShellRunner.GetStr(obj, "PowerScheme"),
            NumaSpanEnabled = PowerShellRunner.GetBool(obj, "NumaSpanEnabled"),
            LiveMigrationAuth = PowerShellRunner.GetStr(obj, "LiveMigrationAuth"),
            MaxLiveMigrations = PowerShellRunner.GetInt(obj, "MaxLiveMigrations"),
            ClusterName = PowerShellRunner.GetStr(obj, "ClusterName")
        });
    }
    return list;
}

private async Task<IReadOnlyList<HvCluster>> GetClustersAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + @"
try {
    $cluster = Get-Cluster -Name $t -ErrorAction Stop
    $nodes = @(Get-ClusterNode -Cluster $t -ErrorAction SilentlyContinue)
    $csvs  = @(Get-ClusterSharedVolume -Cluster $t -ErrorAction SilentlyContinue)

    $running = 0
    $totalVCpu = 0
    $totalRam = 0

    foreach ($n in $nodes) {
        $r = Invoke-HVCommand -ComputerName $n.Name -ScriptBlock {
            $vms = @(Get-VM -ErrorAction SilentlyContinue)
            $cs  = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
            [pscustomobject]@{
                RunningVMs = @($vms | Where-Object State -eq 'Running').Count
                TotalVCpu  = (@($vms) | Measure-Object ProcessorCount -Sum).Sum
                TotalRamGB = [int64][math]::Round(($cs.TotalPhysicalMemory / 1GB), 0)
            }
        }

        foreach ($item in @($r)) {
            $running += [int]$item.RunningVMs
            $totalVCpu += [int]$item.TotalVCpu
            $totalRam += [int64]$item.TotalRamGB
        }
    }

    [pscustomobject]@{
        ClusterName      = $cluster.Name
        NodeCount        = @($nodes).Count
        QuorumType       = $cluster.QuorumType
        QuorumResource   = $cluster.QuorumResource
        CsvVolumeCount   = @($csvs).Count
        TotalVCpu        = [int]$totalVCpu
        TotalRamGB       = [int64]$totalRam
        RunningVMs       = [int]$running
        HAEnabled        = $true
        S2DEnabled       = [bool]($cluster.S2DEnabled -or $cluster.StorageSpacesDirect)
        ClusterStatus    = $cluster.State
        FunctionalLevel  = $cluster.ClusterFunctionalLevel
        NetworkName      = $cluster.Name
        Domain           = $env:USERDOMAIN
        StretchedCluster = $false
        SiteCount        = ''
    }
} catch {
    [pscustomobject]@{
        ClusterName      = $t
        NodeCount        = 0
        QuorumType       = ''
        QuorumResource   = ''
        CsvVolumeCount   = 0
        TotalVCpu        = 0
        TotalRamGB       = 0
        RunningVMs       = 0
        HAEnabled        = $false
        S2DEnabled       = $false
        ClusterStatus    = 'Error'
        FunctionalLevel  = ''
        NetworkName      = $t
        Domain           = $env:USERDOMAIN
        StretchedCluster = $false
        SiteCount        = $_.Exception.Message
    }
}";
    var results = await _ps.RunScriptAsync(script, ct);

    var list = new List<HvCluster>();
    foreach (var obj in results)
    {
        list.Add(new HvCluster
        {
            ClusterName = PowerShellRunner.GetStr(obj, "ClusterName"),
            NodeCount = PowerShellRunner.GetInt(obj, "NodeCount"),
            QuorumType = PowerShellRunner.GetStr(obj, "QuorumType"),
            QuorumResource = PowerShellRunner.GetStr(obj, "QuorumResource"),
            CsvVolumeCount = PowerShellRunner.GetInt(obj, "CsvVolumeCount"),
            TotalVCpu = PowerShellRunner.GetInt(obj, "TotalVCpu"),
            TotalRamGB = PowerShellRunner.GetLong(obj, "TotalRamGB"),
            RunningVMs = PowerShellRunner.GetInt(obj, "RunningVMs"),
            HAEnabled = PowerShellRunner.GetBool(obj, "HAEnabled"),
            S2DEnabled = PowerShellRunner.GetBool(obj, "S2DEnabled"),
            ClusterStatus = PowerShellRunner.GetStr(obj, "ClusterStatus"),
            FunctionalLevel = PowerShellRunner.GetStr(obj, "FunctionalLevel"),
            NetworkName = PowerShellRunner.GetStr(obj, "NetworkName"),
            Domain = PowerShellRunner.GetStr(obj, "Domain"),
            StretchedCluster = PowerShellRunner.GetBool(obj, "StretchedCluster"),
            SiteCount = PowerShellRunner.GetStr(obj, "SiteCount")
        });
    }
    return list;
}

private async Task<IReadOnlyList<HvSwitch>> GetSwitchesAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + @"
foreach ($node in $nodes) {
    Invoke-HVCommand -ComputerName $node -ScriptBlock {
        Import-Module Hyper-V -ErrorAction SilentlyContinue
        Get-VMSwitch -ErrorAction SilentlyContinue | ForEach-Object {
            [pscustomobject]@{
                SwitchName        = $_.Name
                SwitchType        = $_.SwitchType.ToString()
                BoundNics         = ($_.NetAdapterInterfaceDescriptions -join ', ')
                Vlans             = ''
                VmsConnected      = [int]((Get-VMNetworkAdapter -All -ErrorAction SilentlyContinue | Where-Object SwitchName -eq $_.Name).Count)
                SetTeaming        = [bool]$_.EmbeddedTeamingEnabled
                AllowManagementOS = [bool]$_.AllowManagementOS
                Bandwidth         = ''
                Nodes             = $env:COMPUTERNAME
                IovEnabled        = [bool]$_.IovEnabled
                Notes             = ''
            }
        }
    }
}";
    var results = await _ps.RunScriptAsync(script, ct);
    var list = new List<HvSwitch>();
    foreach (var obj in results)
    {
        list.Add(new HvSwitch
        {
            SwitchName = PowerShellRunner.GetStr(obj, "SwitchName"),
            SwitchType = PowerShellRunner.GetStr(obj, "SwitchType"),
            BoundNics = PowerShellRunner.GetStr(obj, "BoundNics"),
            Vlans = PowerShellRunner.GetStr(obj, "Vlans"),
            VmsConnected = PowerShellRunner.GetInt(obj, "VmsConnected"),
            SetTeaming = PowerShellRunner.GetBool(obj, "SetTeaming"),
            AllowManagementOS = PowerShellRunner.GetBool(obj, "AllowManagementOS"),
            Bandwidth = PowerShellRunner.GetStr(obj, "Bandwidth"),
            Nodes = PowerShellRunner.GetStr(obj, "Nodes"),
            IovEnabled = PowerShellRunner.GetBool(obj, "IovEnabled"),
            Notes = PowerShellRunner.GetStr(obj, "Notes")
        });
    }
    return list;
}

private async Task<IReadOnlyList<HvPhysicalNic>> GetPhysicalNicsAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + @"
foreach ($node in $nodes) {
    Invoke-HVCommand -ComputerName $node -ScriptBlock {
        $rdmaMap = @{}
        try {
            Get-NetAdapterRdma -ErrorAction SilentlyContinue | ForEach-Object { $rdmaMap[$_.Name] = $_.Enabled }
        } catch {}

        Get-NetAdapter -Physical -ErrorAction SilentlyContinue | ForEach-Object {
            $name = $_.Name
            [pscustomobject]@{
                NodeName             = $env:COMPUTERNAME
                AdapterName          = $name
                MacAddress           = $_.MacAddress
                Speed                = $_.LinkSpeed
                LinkStatus           = $_.Status
                RdmaEnabled          = [bool]($rdmaMap[$name])
                VlanId               = ''
                TeamName             = ''
                DriverVersion        = ''
                Description          = $_.InterfaceDescription
                InterfaceDescription = $_.InterfaceDescription
                PfcEnabled           = $false
                EtsEnabled           = $false
            }
        }
    }
}";
    var results = await _ps.RunScriptAsync(script, ct);
    var list = new List<HvPhysicalNic>();
    foreach (var obj in results)
    {
        list.Add(new HvPhysicalNic
        {
            NodeName = PowerShellRunner.GetStr(obj, "NodeName"),
            AdapterName = PowerShellRunner.GetStr(obj, "AdapterName"),
            MacAddress = PowerShellRunner.GetStr(obj, "MacAddress"),
            Speed = PowerShellRunner.GetStr(obj, "Speed"),
            LinkStatus = PowerShellRunner.GetStr(obj, "LinkStatus"),
            RdmaEnabled = PowerShellRunner.GetBool(obj, "RdmaEnabled"),
            VlanId = PowerShellRunner.GetStr(obj, "VlanId"),
            TeamName = PowerShellRunner.GetStr(obj, "TeamName"),
            DriverVersion = PowerShellRunner.GetStr(obj, "DriverVersion"),
            Description = PowerShellRunner.GetStr(obj, "Description"),
            InterfaceDescription = PowerShellRunner.GetStr(obj, "InterfaceDescription"),
            PfcEnabled = PowerShellRunner.GetBool(obj, "PfcEnabled"),
            EtsEnabled = PowerShellRunner.GetBool(obj, "EtsEnabled")
        });
    }
    return list;
}

private async Task<IReadOnlyList<HvStorage>> GetStorageVolumesAsync(string target, CancellationToken ct = default)
{
    var results = await _ps.RunScriptAsync(@"
Get-Volume -CimSession '" + target + @"' -ErrorAction SilentlyContinue | ForEach-Object {
    [pscustomobject]@{
        Name = $_.FileSystemLabel
        Path = $_.Path
        SizeGB = [math]::Round($_.Size / 1GB, 2)
        FreeGB = [math]::Round($_.SizeRemaining / 1GB, 2)
        FileSystem = $_.FileSystem
    }
}", ct);
    var list = new List<HvStorage>();
    foreach (System.Management.Automation.PSObject obj in results)
    {
        var totalGb = ParseDoubleValue(obj, "SizeGB");
        var freeGb = ParseDoubleValue(obj, "FreeGB");
        list.Add(new HvStorage
        {
            VolumeName = PowerShellRunner.GetStr(obj, "Name"),
            CsvPath = PowerShellRunner.GetStr(obj, "Path"),
            TotalTB = totalGb / 1024d,
            FreeTB = freeGb / 1024d,
            UsedTB = (totalGb - freeGb) / 1024d,
            FileSystem = PowerShellRunner.GetStr(obj, "FileSystem")
        });
    }
    return list;
}

public async Task<IReadOnlyList<AzureS2DPool>> GetS2DPoolsAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + @"
$node = $nodes | Select-Object -First 1
if ($node) {
    Invoke-HVCommand -ComputerName $node -ScriptBlock {
        try {
            $subsystem = Get-StorageSubSystem -ErrorAction SilentlyContinue | Where-Object FriendlyName -like 'Clustered Windows Storage*' | Select-Object -First 1
            $pool = $null

            if ($subsystem) {
                $pool = Get-StoragePool -ErrorAction SilentlyContinue | Where-Object {
                    $_.IsPrimordial -eq $false -and $_.FriendlyName -notlike 'Primordial*'
                } | Select-Object -First 1
            }

            if ($null -eq $pool) {
                $pool = Get-StoragePool -ErrorAction SilentlyContinue | Where-Object {
                    $_.IsPrimordial -eq $false -and $_.FriendlyName -notlike 'Primordial*'
                } | Select-Object -First 1
            }

            if ($null -ne $pool) {
                $vd = @($pool | Get-VirtualDisk -ErrorAction SilentlyContinue)
                $pd = @($pool | Get-PhysicalDisk -ErrorAction SilentlyContinue)
                [pscustomobject]@{
                    PoolName          = $pool.FriendlyName
                    Health            = $pool.HealthStatus
                    OperationalStatus = ($pool.OperationalStatus -join ', ')
                    TotalTB           = [math]::Round(($pool.Size / 1TB), 2)
                    ProvisionedTB     = [math]::Round((($vd | Measure-Object Size -Sum).Sum / 1TB), 2)
                    UsedTB            = [math]::Round(($pool.AllocatedSize / 1TB), 2)
                    FaultTolerance    = ''
                    CacheMode         = ''
                    TotalDrives       = @($pd).Count
                    FailedDrives      = @($pd | Where-Object HealthStatus -eq 'Unhealthy').Count
                    WarningDrives     = @($pd | Where-Object HealthStatus -eq 'Warning').Count
                    DriveTypes        = (($pd | Group-Object MediaType | ForEach-Object { $_.Name }) -join ', ')
                    TierSummary       = ''
                }
            } else {
                [pscustomobject]@{
                    PoolName          = 'No S2D pool found'
                    Health            = 'Unknown'
                    OperationalStatus = 'No clustered storage subsystem or storage pool returned'
                    TotalTB           = 0
                    ProvisionedTB     = 0
                    UsedTB            = 0
                    FaultTolerance    = ''
                    CacheMode         = ''
                    TotalDrives       = 0
                    FailedDrives      = 0
                    WarningDrives     = 0
                    DriveTypes        = ''
                    TierSummary       = ''
                }
            }
        } catch {
            [pscustomobject]@{
                PoolName          = 'S2D query failed'
                Health            = 'Error'
                OperationalStatus = $_.Exception.Message
                TotalTB           = 0
                ProvisionedTB     = 0
                UsedTB            = 0
                FaultTolerance    = ''
                CacheMode         = ''
                TotalDrives       = 0
                FailedDrives      = 0
                WarningDrives     = 0
                DriveTypes        = ''
                TierSummary       = ''
            }
        }
    }
} else {
    [pscustomobject]@{
        PoolName          = 'No cluster node found'
        Health            = 'Unknown'
        OperationalStatus = 'The node list returned empty'
        TotalTB           = 0
        ProvisionedTB     = 0
        UsedTB            = 0
        FaultTolerance    = ''
        CacheMode         = ''
        TotalDrives       = 0
        FailedDrives      = 0
        WarningDrives     = 0
        DriveTypes        = ''
        TierSummary       = ''
    }
}";
    var results = await _ps.RunScriptAsync(script, ct);
    var list = new List<AzureS2DPool>();
    foreach (System.Management.Automation.PSObject obj in results)
    {
        list.Add(new AzureS2DPool
        {
            PoolName = PowerShellRunner.GetStr(obj, "PoolName"),
            Health = PowerShellRunner.GetStr(obj, "Health"),
            OperationalStatus = PowerShellRunner.GetStr(obj, "OperationalStatus"),
            TotalTB = ParseDoubleValue(obj, "TotalTB"),
            ProvisionedTB = ParseDoubleValue(obj, "ProvisionedTB"),
            UsedTB = ParseDoubleValue(obj, "UsedTB"),
            FaultTolerance = PowerShellRunner.GetStr(obj, "FaultTolerance"),
            CacheMode = PowerShellRunner.GetStr(obj, "CacheMode"),
            TotalDrives = PowerShellRunner.GetInt(obj, "TotalDrives"),
            FailedDrives = PowerShellRunner.GetInt(obj, "FailedDrives"),
            WarningDrives = PowerShellRunner.GetInt(obj, "WarningDrives"),
            DriveTypes = PowerShellRunner.GetStr(obj, "DriveTypes"),
            TierSummary = PowerShellRunner.GetStr(obj, "TierSummary")
        });
    }
    return list;
}

private async Task<IReadOnlyList<VmDisk>> GetVMDisksAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + @"
foreach ($node in $nodes) {
    Invoke-HVCommand -ComputerName $node -ScriptBlock {
        Import-Module Hyper-V -ErrorAction SilentlyContinue

        Get-VM -ErrorAction SilentlyContinue | ForEach-Object {
            $vm = $_
            Get-VMHardDiskDrive -VMName $vm.Name -ErrorAction SilentlyContinue | ForEach-Object {
                $drive = $_
                $vhd = $null
                if ($drive.Path -and (Test-Path $drive.Path)) {
                    try { $vhd = Get-VHD -Path $drive.Path -ErrorAction SilentlyContinue } catch {}
                }

                [pscustomobject]@{
                    VmName              = $vm.Name
                    DiskNumber          = [int]0
                    DiskType            = if ($drive.Path -match '\.iso$') { 'ISO' } elseif ($drive.Path -match '\.vhdx?$') { 'OS/Data' } else { 'Disk' }
                    Format              = if ($drive.Path -match '\.vhdx$') { 'VHDX' } elseif ($drive.Path -match '\.vhd$') { 'VHD' } elseif ($drive.Path -match '\.iso$') { 'ISO' } else { '' }
                    Path                = $drive.Path
                    SizeGB              = if ($vhd) { [int64][math]::Round(($vhd.Size / 1GB), 0) } else { 0 }
                    UsedGB              = if ($vhd -and $vhd.FileSize) { [int64][math]::Round(($vhd.FileSize / 1GB), 0) } else { 0 }
                    Controller          = $drive.ControllerType.ToString()
                    ControllerNumber    = [int]$drive.ControllerNumber
                    ControllerLocation  = [int]$drive.ControllerLocation
                    IopsLimit           = [int64]($drive.MaximumIOPS)
                    Shared              = [bool]$drive.SupportPersistentReservations
                    IsFixed             = if ($vhd) { [bool]($vhd.VhdType -eq 'Fixed') } else { $false }
                    FragmentationPercent= ''
                }
            }
        }
    }
}
";
    var results = await _ps.RunScriptAsync(script, ct);
    var list = new List<VmDisk>();
    foreach (System.Management.Automation.PSObject obj in results)
    {
        var vmName = PowerShellRunner.GetStr(obj, "VmName");
        var path = PowerShellRunner.GetStr(obj, "Path");
        if (string.IsNullOrWhiteSpace(vmName) && string.IsNullOrWhiteSpace(path))
            continue;

        list.Add(new VmDisk
        {
            VmName = vmName,
            DiskNumber = PowerShellRunner.GetInt(obj, "DiskNumber"),
            DiskType = PowerShellRunner.GetStr(obj, "DiskType"),
            Format = PowerShellRunner.GetStr(obj, "Format"),
            Path = path,
            SizeGB = PowerShellRunner.GetLong(obj, "SizeGB"),
            UsedGB = PowerShellRunner.GetLong(obj, "UsedGB"),
            Controller = PowerShellRunner.GetStr(obj, "Controller"),
            ControllerNumber = PowerShellRunner.GetInt(obj, "ControllerNumber"),
            ControllerLocation = PowerShellRunner.GetInt(obj, "ControllerLocation"),
            IopsLimit = PowerShellRunner.GetLong(obj, "IopsLimit"),
            Shared = PowerShellRunner.GetBool(obj, "Shared"),
            IsFixed = PowerShellRunner.GetBool(obj, "IsFixed"),
            FragmentationPercent = PowerShellRunner.GetStr(obj, "FragmentationPercent")
        });
    }
    return list;
}

private async Task<IReadOnlyList<VmNic>> GetVMNicsAsync(string target, CancellationToken ct = default)
{
    string script = ClusterPrelude(target) + """
foreach ($node in $nodes) {
    Invoke-HVCommand -ComputerName $node -ScriptBlock {
        Import-Module Hyper-V -ErrorAction SilentlyContinue
        Get-VM -ErrorAction SilentlyContinue | ForEach-Object {
            $vm = $_
            Get-VMNetworkAdapter -VMName $vm.Name -ErrorAction SilentlyContinue | ForEach-Object {
                [pscustomobject]@{
                    VmName = $vm.Name
                    Name = $_.Name
                    SwitchName = $_.SwitchName
                    MacAddress = $_.MacAddress
                }
            }
        }
    }
}
""";
    var results = await _ps.RunScriptAsync(script, ct);
    var list = new List<VmNic>();
    foreach (var obj in results)
    {
        var vmName = PowerShellRunner.GetStr(obj, "VmName");
        var adapterName = PowerShellRunner.GetStr(obj, "Name");
        if (string.IsNullOrWhiteSpace(vmName) && string.IsNullOrWhiteSpace(adapterName))
            continue;

        list.Add(new VmNic
        {
            VmName = vmName,
            AdapterName = adapterName,
            SwitchName = PowerShellRunner.GetStr(obj, "SwitchName"),
            MacAddress = PowerShellRunner.GetStr(obj, "MacAddress")
        });
    }
    return list;
}

private async Task<IReadOnlyList<VmSnapshot>> GetVMSnapshotsAsync(string target, CancellationToken ct = default)
{
    var results = await _ps.RunScriptAsync(@"
Get-VM -ComputerName '" + target + @"' -ErrorAction SilentlyContinue | ForEach-Object {
    $vm = $_
    Get-VMSnapshot -VMName $vm.Name -ComputerName '" + target + @"' -ErrorAction SilentlyContinue | ForEach-Object {
        [pscustomobject]@{
            VmName = $vm.Name
            SnapshotName = $_.Name
            AgeDays = [int]((Get-Date) - $_.CreationTime).TotalDays
        }
    }
}", ct);
    var list = new List<VmSnapshot>();
    foreach (var obj in results)
    {
        var ageDays = PowerShellRunner.GetInt(obj, "AgeDays");
        list.Add(new VmSnapshot
        {
            VmName = PowerShellRunner.GetStr(obj, "VmName"),
            SnapshotName = PowerShellRunner.GetStr(obj, "SnapshotName"),
            CreationTime = DateTime.Now.AddDays(-ageDays)
        });
    }
    return list;
}

    
    private static double ParseDoubleValue(System.Management.Automation.PSObject obj, string propertyName)
    {
        var s = PowerShellRunner.GetStr(obj, propertyName);
        return double.TryParse(s, out var d) ? d : 0d;
    }


public async Task SetLiveMigrationAuthenticationForClusterAsync(string target, string mode, CancellationToken ct = default)
{
    string normalizedMode = string.Equals(mode, "CredSSP", StringComparison.OrdinalIgnoreCase) ? "CredSSP" : "Kerberos";
    string script = ClusterPrelude(target) +
        "$mode = '" + EscapePs(normalizedMode) + "'\r\n" +
        "foreach ($node in $nodes) {\r\n" +
        "    Invoke-HVCommand -ComputerName $node -ArgumentList @($mode) -ScriptBlock {\r\n" +
        "        param($authMode)\r\n" +
        "        Import-Module Hyper-V -ErrorAction SilentlyContinue\r\n" +
        "        Set-VMHost -VirtualMachineMigrationAuthenticationType $authMode -ErrorAction Stop | Out-Null\r\n" +
        "        [pscustomobject]@{ NodeName = $env:COMPUTERNAME; AuthMode = $authMode; Status = 'Applied' }\r\n" +
        "    }\r\n" +
        "}\r\n";
    await _ps.RunScriptAsync(script, ct);
}




public async Task<ClusterMigrationResult> ExecuteLiveMigrationAsync(string vmName, string sourceHost, string destinationHost, IProgress<string>? progress = null, CancellationToken ct = default)
{
    progress?.Report($"Locating clustered VM role for {vmName}...");
    string script =
        "$vmName = '" + EscapePs(vmName) + "'\r\n" +
        "$sourceHost = '" + EscapePs(sourceHost) + "'\r\n" +
        "$destinationHost = '" + EscapePs(destinationHost) + "'\r\n" +
        "Import-Module FailoverClusters -ErrorAction Stop\r\n" +
        "Import-Module Hyper-V -ErrorAction SilentlyContinue\r\n" +
        "try {\r\n" +
        "    $vmResource = Get-ClusterResource -ErrorAction Stop | Where-Object { $_.ResourceType -eq 'Virtual Machine' -and $_.Name -eq $vmName } | Select-Object -First 1\r\n" +
        "    if (-not $vmResource) {\r\n" +
        "        $vmResource = Get-ClusterResource -ErrorAction Stop | Where-Object { $_.ResourceType -eq 'Virtual Machine' -and $_.Name -like ('*' + $vmName + '*') } | Select-Object -First 1\r\n" +
        "    }\r\n" +
        "    if (-not $vmResource) {\r\n" +
        "        throw ('Cluster virtual machine resource not found for ' + $vmName)\r\n" +
        "    }\r\n" +
        "    $clusterGroup = $vmResource.OwnerGroup\r\n" +
        "    if (-not $clusterGroup) { throw ('Owner group not found for ' + $vmName) }\r\n" +
        "    [pscustomobject]@{ Stage='RoleResolved'; VmName=$vmName; ClusterRole=$clusterGroup.Name; CurrentOwner=$clusterGroup.OwnerNode.Name; Details='Cluster resource resolved' }\r\n" +
        "    Move-ClusterVirtualMachineRole -Name $clusterGroup.Name -Node $destinationHost -MigrationType Live -Wait 0 -ErrorAction Stop | Out-Null\r\n" +
        "    [pscustomobject]@{ Stage='Submitted'; VmName=$vmName; ClusterRole=$clusterGroup.Name; CurrentOwner=$clusterGroup.OwnerNode.Name; Details='Migration submitted' }\r\n" +
        "    for ($i = 0; $i -lt 30; $i++) {\r\n" +
        "        Start-Sleep -Seconds 2\r\n" +
        "        $groupNow = Get-ClusterGroup -Name $clusterGroup.Name -ErrorAction Stop\r\n" +
        "        $owner = $groupNow.OwnerNode.Name\r\n" +
        "        if ($owner -eq $destinationHost) {\r\n" +
        "            [pscustomobject]@{ Stage='Completed'; VmName=$vmName; ClusterRole=$clusterGroup.Name; CurrentOwner=$owner; Details='Cluster owner moved to destination node' }\r\n" +
        "            return\r\n" +
        "        }\r\n" +
        "        [pscustomobject]@{ Stage='InProgress'; VmName=$vmName; ClusterRole=$clusterGroup.Name; CurrentOwner=$owner; Details='Waiting for cluster owner to move' }\r\n" +
        "    }\r\n" +
        "    $finalGroup = Get-ClusterGroup -Name $clusterGroup.Name -ErrorAction Stop\r\n" +
        "    [pscustomobject]@{ Stage='TimedOut'; VmName=$vmName; ClusterRole=$clusterGroup.Name; CurrentOwner=$finalGroup.OwnerNode.Name; Details='Migration did not reach destination during tracking window' }\r\n" +
        "} catch {\r\n" +
        "    Write-Output ('MIGRATION_FAILED: ' + $_.Exception.Message)\r\n" +
        "}\r\n";
    var results = await _ps.RunScriptAsync(script, ct);

    var failure = results
        .Select(r => r?.ToString() ?? string.Empty)
        .FirstOrDefault(s => s.StartsWith("MIGRATION_FAILED:", StringComparison.OrdinalIgnoreCase));

    if (!string.IsNullOrWhiteSpace(failure))
        throw new InvalidOperationException(failure);

    ClusterMigrationResult? latest = null;

    foreach (var obj in results.OfType<System.Management.Automation.PSObject>())
    {
        var stage = PowerShellRunner.GetStr(obj, "Stage");
        var currentOwner = PowerShellRunner.GetStr(obj, "CurrentOwner");
        var clusterRole = PowerShellRunner.GetStr(obj, "ClusterRole");
        var details = PowerShellRunner.GetStr(obj, "Details");

        latest = new ClusterMigrationResult
        {
            VmName = PowerShellRunner.GetStr(obj, "VmName"),
            SourceHost = sourceHost,
            DestinationHost = destinationHost,
            CurrentOwner = currentOwner,
            ClusterRole = clusterRole,
            Status = stage,
            Details = details
        };

        progress?.Report(stage switch
        {
            "RoleResolved" => $"Cluster role resolved: {clusterRole} (owner {currentOwner})",
            "Submitted" => $"Migration submitted: {vmName} → {destinationHost}",
            "InProgress" => $"Migration in progress: current owner {currentOwner}",
            "Completed" => $"Migration complete: {vmName} now on {currentOwner}",
            "TimedOut" => $"Migration tracking timed out: current owner {currentOwner}",
            _ => $"{stage}: {details}"
        });
    }

    return latest ?? new ClusterMigrationResult
    {
        VmName = vmName,
        SourceHost = sourceHost,
        DestinationHost = destinationHost,
        CurrentOwner = sourceHost,
        Status = "Unknown",
        Details = "Migration returned no structured result."
    };
}

public async Task ExecuteVmPowerActionAsync(string vmName, string hostName, string action, CancellationToken ct = default)
{
    string script =
        "$vmName = '" + EscapePs(vmName) + "'\r\n" +
        "$hostName = '" + EscapePs(hostName) + "'\r\n" +
        "$action = '" + EscapePs(action) + "'\r\n" +
        "function Invoke-HVAction {\r\n" +
        "    param(\r\n" +
        "        [string]$ComputerName,\r\n" +
        "        [scriptblock]$ScriptBlock,\r\n" +
        "        [object[]]$ArgumentList = @()\r\n" +
        "    )\r\n" +
        "    if ($null -ne $script:HVToolsCredential) {\r\n" +
        "        Invoke-Command -ComputerName $ComputerName -Credential $script:HVToolsCredential -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList -ErrorAction Stop\r\n" +
        "    } else {\r\n" +
        "        Invoke-Command -ComputerName $ComputerName -ScriptBlock $ScriptBlock -ArgumentList $ArgumentList -ErrorAction Stop\r\n" +
        "    }\r\n" +
        "}\r\n" +
        "Invoke-HVAction -ComputerName $hostName -ArgumentList @($vmName, $action) -ScriptBlock {\r\n" +
        "    param($name, $op)\r\n" +
        "    Import-Module Hyper-V -ErrorAction SilentlyContinue\r\n" +
        "    switch ($op) {\r\n" +
        "        'Start'   { Start-VM -Name $name -ErrorAction Stop | Out-Null }\r\n" +
        "        'Stop'    { Stop-VM -Name $name -Force -TurnOff -ErrorAction Stop | Out-Null }\r\n" +
        "        'Restart' { Restart-VM -Name $name -Force -ErrorAction Stop | Out-Null }\r\n" +
        "        default   { throw \"Unsupported action: $op\" }\r\n" +
        "    }\r\n" +
        "    [pscustomobject]@{ VmName = $name; Action = $op; Status = 'Submitted' }\r\n" +
        "}\r\n";
    await _ps.RunScriptAsync(script, ct);
}
}
