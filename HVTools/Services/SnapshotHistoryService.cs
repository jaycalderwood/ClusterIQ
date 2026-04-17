using System.IO;
using System.Text.Json;
using HVTools.Models;

namespace HVTools.Services;

public sealed class SnapshotHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string FolderPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClusterIQ", "snapshots");

    public EnvironmentSnapshot BuildSnapshot(InventoryResult inv)
    {
        return new EnvironmentSnapshot
        {
            EnvironmentName = inv.Connection.HostOrCluster,
            CapturedAt = DateTime.Now,
            OverallHealthScore = ExtractOverallHealthScore(inv.HealthChecks),
            Hosts = inv.Hosts.Select(h => new SnapshotHost
            {
                NodeName = h.NodeName,
                NodeStatus = h.NodeStatus,
                RunningVMs = h.RunningVMs,
                TotalRamGB = (int)h.TotalRamGB,
                UsedRamGB = (int)h.UsedRamGB
            }).ToList(),
            Vms = inv.VMs.Select(v => new SnapshotVm
            {
                Name = v.Name,
                HostNode = v.Host,
                PowerState = v.PowerState,
                MemoryAssignedGB = (int)v.MemoryGB,
                ProcessorCount = v.vCPU
            }).ToList(),
            ArcResources = inv.ArcResources.Select(a => new SnapshotArc
            {
                ResourceName = a.ResourceName,
                ArcStatus = a.ArcStatus
            }).ToList()
        };
    }

    public void Save(EnvironmentSnapshot snapshot, int retentionCount = 10)
    {
        Directory.CreateDirectory(FolderPath);
        var file = Path.Combine(FolderPath, $"{Sanitize(snapshot.EnvironmentName)}_{snapshot.CapturedAt:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(file, json);
        PurgeOldSnapshots(snapshot.EnvironmentName, retentionCount);
    }

    public void PurgeOldSnapshots(string environmentName, int retentionCount)
    {
        if (retentionCount < 1) retentionCount = 1;
        Directory.CreateDirectory(FolderPath);
        var prefix = Sanitize(environmentName) + "_";
        var files = new DirectoryInfo(FolderPath)
            .GetFiles(prefix + "*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        foreach (var old in files.Skip(retentionCount))
        {
            try { old.Delete(); } catch { }
        }
    }

    public EnvironmentSnapshot? LoadPrevious(string environmentName)
    {
        Directory.CreateDirectory(FolderPath);
        var prefix = Sanitize(environmentName) + "_";
        var file = new DirectoryInfo(FolderPath)
            .GetFiles(prefix + "*.json")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Skip(1)
            .FirstOrDefault();

        if (file is null) return null;

        try
        {
            var json = File.ReadAllText(file.FullName);
            return JsonSerializer.Deserialize<EnvironmentSnapshot>(json);
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<DriftChange> Compare(EnvironmentSnapshot? previous, EnvironmentSnapshot current)
    {
        if (previous is null) return Array.Empty<DriftChange>();

        var changes = new List<DriftChange>();

        var prevHosts = previous.Hosts.ToDictionary(h => h.NodeName, StringComparer.OrdinalIgnoreCase);
        var currHosts = current.Hosts.ToDictionary(h => h.NodeName, StringComparer.OrdinalIgnoreCase);

        foreach (var host in current.Hosts)
        {
            if (!prevHosts.TryGetValue(host.NodeName, out var prev))
            {
                changes.Add(new DriftChange
                {
                    Category = "Host",
                    ObjectName = host.NodeName,
                    ChangeType = "Added",
                    PreviousValue = "Not present",
                    CurrentValue = $"Status={host.NodeStatus}",
                    Impact = "New host detected in the environment."
                });
                continue;
            }

            if (!string.Equals(prev.NodeStatus, host.NodeStatus, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new DriftChange
                {
                    Category = "Host",
                    ObjectName = host.NodeName,
                    ChangeType = "State Changed",
                    PreviousValue = prev.NodeStatus,
                    CurrentValue = host.NodeStatus,
                    Impact = "Host operational state changed between collections."
                });
            }

            if (prev.RunningVMs != host.RunningVMs)
            {
                changes.Add(new DriftChange
                {
                    Category = "Host",
                    ObjectName = host.NodeName,
                    ChangeType = "VM Load Changed",
                    PreviousValue = prev.RunningVMs.ToString(),
                    CurrentValue = host.RunningVMs.ToString(),
                    Impact = "Workload placement or failover activity changed host load."
                });
            }

            if (prev.UsedRamGB != host.UsedRamGB)
            {
                changes.Add(new DriftChange
                {
                    Category = "Host",
                    ObjectName = host.NodeName,
                    ChangeType = "Memory Usage Changed",
                    PreviousValue = $"{prev.UsedRamGB} GB",
                    CurrentValue = $"{host.UsedRamGB} GB",
                    Impact = "Host memory pressure changed since the previous snapshot."
                });
            }
        }

        foreach (var host in previous.Hosts.Where(h => !currHosts.ContainsKey(h.NodeName)))
        {
            changes.Add(new DriftChange
            {
                Category = "Host",
                ObjectName = host.NodeName,
                ChangeType = "Removed",
                PreviousValue = $"Status={host.NodeStatus}",
                CurrentValue = "Not present",
                Impact = "A previously observed host is missing from the current snapshot."
            });
        }

        var prevVms = previous.Vms.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);
        var currVms = current.Vms.ToDictionary(v => v.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var vm in current.Vms)
        {
            if (!prevVms.TryGetValue(vm.Name, out var prev))
            {
                changes.Add(new DriftChange
                {
                    Category = "VM",
                    ObjectName = vm.Name,
                    ChangeType = "Added",
                    PreviousValue = "Not present",
                    CurrentValue = $"{vm.PowerState} on {vm.HostNode}",
                    Impact = "A new virtual machine was detected."
                });
                continue;
            }

            if (!string.Equals(prev.HostNode, vm.HostNode, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new DriftChange
                {
                    Category = "VM",
                    ObjectName = vm.Name,
                    ChangeType = "Moved",
                    PreviousValue = prev.HostNode,
                    CurrentValue = vm.HostNode,
                    Impact = "VM placement changed between snapshots."
                });
            }

            if (!string.Equals(prev.PowerState, vm.PowerState, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new DriftChange
                {
                    Category = "VM",
                    ObjectName = vm.Name,
                    ChangeType = "Power State Changed",
                    PreviousValue = prev.PowerState,
                    CurrentValue = vm.PowerState,
                    Impact = "VM operational state changed between snapshots."
                });
            }

            if (prev.MemoryAssignedGB != vm.MemoryAssignedGB)
            {
                changes.Add(new DriftChange
                {
                    Category = "VM",
                    ObjectName = vm.Name,
                    ChangeType = "Memory Changed",
                    PreviousValue = $"{prev.MemoryAssignedGB} GB",
                    CurrentValue = $"{vm.MemoryAssignedGB} GB",
                    Impact = "VM assigned memory changed."
                });
            }

            if (prev.ProcessorCount != vm.ProcessorCount)
            {
                changes.Add(new DriftChange
                {
                    Category = "VM",
                    ObjectName = vm.Name,
                    ChangeType = "CPU Changed",
                    PreviousValue = prev.ProcessorCount.ToString(),
                    CurrentValue = vm.ProcessorCount.ToString(),
                    Impact = "VM virtual CPU configuration changed."
                });
            }
        }

        foreach (var vm in previous.Vms.Where(v => !currVms.ContainsKey(v.Name)))
        {
            changes.Add(new DriftChange
            {
                Category = "VM",
                ObjectName = vm.Name,
                ChangeType = "Removed",
                PreviousValue = $"{vm.PowerState} on {vm.HostNode}",
                CurrentValue = "Not present",
                Impact = "A previously observed VM is missing from the current snapshot."
            });
        }

        var prevArc = previous.ArcResources.ToDictionary(a => a.ResourceName, StringComparer.OrdinalIgnoreCase);
        foreach (var arc in current.ArcResources)
        {
            if (prevArc.TryGetValue(arc.ResourceName, out var prev) &&
                !string.Equals(prev.ArcStatus, arc.ArcStatus, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new DriftChange
                {
                    Category = "Azure Arc",
                    ObjectName = arc.ResourceName,
                    ChangeType = "Status Changed",
                    PreviousValue = prev.ArcStatus,
                    CurrentValue = arc.ArcStatus,
                    Impact = "Arc management state changed between snapshots."
                });
            }
        }

        if (previous.OverallHealthScore != current.OverallHealthScore)
        {
            changes.Add(new DriftChange
            {
                Category = "Environment",
                ObjectName = current.EnvironmentName,
                ChangeType = "Health Score Changed",
                PreviousValue = $"{previous.OverallHealthScore}/100",
                CurrentValue = $"{current.OverallHealthScore}/100",
                Impact = "Overall operational health changed since the last snapshot."
            });
        }

        return changes.OrderBy(c => c.Category).ThenBy(c => c.ObjectName).ThenBy(c => c.ChangeType).ToList();
    }

    public IReadOnlyList<HealthCheck> ToHealthChecks(IReadOnlyList<DriftChange> changes)
    {
        return changes.Select(c => new HealthCheck
        {
            Severity = c.Category switch
            {
                "Environment" => HealthSeverity.Info,
                "Azure Arc" => HealthSeverity.Warning,
                _ => HealthSeverity.Info
            },
            Category = "Drift",
            ObjectName = c.ObjectName,
            Message = $"{c.ChangeType}: {c.PreviousValue} → {c.CurrentValue}",
            Detail = $"{c.Category} change detected at {c.DetectedAt:g}.",
            Recommendation = "Review whether this change was expected and update operational records if required."
        }).ToList();
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "environment" : name;
    }

    private static int ExtractOverallHealthScore(IReadOnlyList<HealthCheck> checks)
    {
        var overall = checks.FirstOrDefault(h => h.Category == "Score" && h.Message.StartsWith("Overall environment health score", StringComparison.OrdinalIgnoreCase));
        if (overall is null) return 0;

        var match = System.Text.RegularExpressions.Regex.Match(overall.Message, @"(\d+)/100");
        return match.Success && int.TryParse(match.Groups[1].Value, out var score) ? score : 0;
    }
}
