// =============================================================================
//  PerfHistoryService  (v1.0) — @"..." verbatim strings throughout)
// =============================================================================

using HVTools.Models;
using Microsoft.Extensions.Logging;

namespace HVTools.Services;

public sealed class PerfHistoryService
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<PerfHistoryService> _logger;

    public PerfHistoryService(PowerShellRunner ps, ILogger<PerfHistoryService> logger)
    {
        _ps = ps; _logger = logger;
    }

    public async Task<IReadOnlyList<PerfSummaryRow>> GetPerfSummaryAsync(
        string clusterName, CancellationToken ct = default)
    {
        _logger.LogInformation("Collecting perf history from {Cluster}", clusterName);

        string script = "$t = '" + clusterName + "'\r\n" + @"
$subsys = Get-StorageSubSystem -CimSession $t -ErrorAction SilentlyContinue |
          Where-Object { $_.Model -eq 'Clustered Windows Storage' }
if (-not $subsys) { Write-Warning 'No S2D subsystem'; return }

$metrics = @('Volume.IOPS.Read','Volume.IOPS.Write','Volume.IOPS.Total',
             'Volume.Throughput.Read','Volume.Throughput.Write','Volume.Latency.Average',
             'Node.CPU.Usage','Node.Memory.Usage',
             'Node.Storage.Throughput.Read','Node.Storage.Throughput.Write')

foreach ($metric in $metrics) {
    try {
        $report = $subsys | Get-StorageHealthReport -ErrorAction SilentlyContinue
        if (-not $report) { continue }
        $keyword = $metric.Split('.')[-1]
        $records = $report.Records | Where-Object { $_.Name -like ""*$keyword*"" }
        foreach ($r in $records) {
            [PSCustomObject]@{
                ObjectName = $r.ElementName
                ObjectType = if ($metric -like 'Volume.*') { 'Volume' }
                             elseif ($metric -like 'Node.*') { 'Node' } else { 'VM' }
                MetricName = $metric
                Value      = $r.Value
                Unit       = $r.Units
                Timestamp  = $r.Timestamp
            }
        }
    } catch { }
}";

        var results = await _ps.RunScriptAsync(script, ct);

        var groups = results
            .GroupBy(obj => PowerShellRunner.GetStr(obj, "ObjectName") + "|" +
                            PowerShellRunner.GetStr(obj, "MetricName"))
            .ToList();

        var rows = new List<PerfSummaryRow>(groups.Count);
        foreach (var g in groups)
        {
            var samples = g.Select(obj => new PerfSample
            {
                Timestamp  = obj.Properties["Timestamp"]?.Value is DateTime dt ? dt : DateTime.Now,
                ObjectName = PowerShellRunner.GetStr(obj, "ObjectName"),
                ObjectType = PowerShellRunner.GetStr(obj, "ObjectType"),
                MetricName = PowerShellRunner.GetStr(obj, "MetricName"),
                Value      = double.TryParse(PowerShellRunner.GetStr(obj, "Value"), out var v) ? v : 0,
                Unit       = PowerShellRunner.GetStr(obj, "Unit"),
            }).OrderBy(s => s.Timestamp).ToList();

            if (samples.Count == 0) continue;

            var values  = samples.Select(s => s.Value).ToList();
            var current = values.Last();
            var peak    = values.Max();
            var avg     = values.Average();
            var trend   = current > avg * 1.1 ? "↑" : current < avg * 0.9 ? "↓" : "→";

            rows.Add(new PerfSummaryRow
            {
                ObjectName   = samples[0].ObjectName,
                ObjectType   = samples[0].ObjectType,
                MetricName   = samples[0].MetricName,
                CurrentValue = current,
                PeakValue    = peak,
                AvgValue     = Math.Round(avg, 1),
                Unit         = samples[0].Unit,
                Trend        = trend,
                Series       = new PerfSeries
                {
                    SeriesName = samples[0].ObjectName + " — " + samples[0].MetricName,
                    Unit       = samples[0].Unit,
                    Samples    = samples,
                },
            });
        }

        var liveRows = await GetLivePerfFallbackAsync(clusterName, ct);
        rows = MergeRows(rows, liveRows);

        _logger.LogInformation("Collected {Count} perf metric series", rows.Count);
        return rows;
    }


private async Task<IReadOnlyList<PerfSummaryRow>> GetLivePerfFallbackAsync(
        string clusterName, CancellationToken ct)
    {
        string script = "$t = '" + clusterName + "'\r\n" + @"
function Invoke-HVPerfCommand {
    param(
        [string]$ComputerName,
        [scriptblock]$ScriptBlock
    )
    if ($null -ne $script:HVToolsCredential) {
        Invoke-Command -ComputerName $ComputerName -Credential $script:HVToolsCredential -ScriptBlock $ScriptBlock -ErrorAction SilentlyContinue
    } else {
        Invoke-Command -ComputerName $ComputerName -ScriptBlock $ScriptBlock -ErrorAction SilentlyContinue
    }
}

$nodes = @()
try {
    if ($null -ne $script:HVToolsCredential) {
        $nodes = @(Invoke-HVPerfCommand -ComputerName $t -ScriptBlock {
            Import-Module FailoverClusters -ErrorAction SilentlyContinue
            try { @(Get-ClusterNode -Cluster $using:t -ErrorAction Stop | Select-Object -ExpandProperty Name) } catch { @() }
        })
    } else {
        $nodes = @(Get-ClusterNode -Cluster $t -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
    }
} catch { }

if (-not $nodes) { $nodes = @($t) }

foreach ($node in $nodes) {
    try {
        $items = @(Invoke-HVPerfCommand -ComputerName $node -ScriptBlock {
            Import-Module Hyper-V -ErrorAction SilentlyContinue

            $local = @()
            $os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue
            $cs = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
            $cpu = (Get-Counter '\Processor(_Total)\% Processor Time' -ErrorAction SilentlyContinue).CounterSamples

            if ($cpu) {
                $local += [PSCustomObject]@{
                    ObjectName = $env:COMPUTERNAME
                    ObjectType = 'Node'
                    MetricName = 'Node.CPU.Usage'
                    Value = [math]::Round($cpu.CookedValue, 1)
                    Unit = '%'
                    Timestamp = Get-Date
                }
            }

            if ($os -and $cs -and $cs.TotalPhysicalMemory -gt 0) {
                $usedPct = [math]::Round((($cs.TotalPhysicalMemory - $os.FreePhysicalMemory * 1KB) / $cs.TotalPhysicalMemory) * 100, 1)
                $local += [PSCustomObject]@{
                    ObjectName = $env:COMPUTERNAME
                    ObjectType = 'Node'
                    MetricName = 'Node.Memory.Usage'
                    Value = $usedPct
                    Unit = '%'
                    Timestamp = Get-Date
                }
            }

            foreach ($vm in (Get-VM -ErrorAction SilentlyContinue)) {
                $cpuUsage = if ($null -ne $vm.CPUUsage) { [double]$vm.CPUUsage } else { 0 }
                $memGb = [math]::Round($vm.MemoryAssigned / 1GB, 2)

                $local += [PSCustomObject]@{
                    ObjectName = $vm.Name
                    ObjectType = 'VM'
                    MetricName = 'VM.CPU.Usage'
                    Value = $cpuUsage
                    Unit = '%'
                    Timestamp = Get-Date
                }

                $local += [PSCustomObject]@{
                    ObjectName = $vm.Name
                    ObjectType = 'VM'
                    MetricName = 'VM.Memory.Assigned'
                    Value = $memGb
                    Unit = 'GB'
                    Timestamp = Get-Date
                }
            }

            $local
        })

        foreach ($i in $items) { $i }
    } catch { }
}";
        var results = await _ps.RunScriptAsync(script, ct);
        var rows = new List<PerfSummaryRow>();

        foreach (var obj in results)
        {
            var val = double.TryParse(PowerShellRunner.GetStr(obj, "Value"), out var v) ? v : 0;
            var sample = new PerfSample
            {
                Timestamp = obj.Properties["Timestamp"]?.Value is DateTime dt ? dt : DateTime.Now,
                ObjectName = PowerShellRunner.GetStr(obj, "ObjectName"),
                ObjectType = PowerShellRunner.GetStr(obj, "ObjectType"),
                MetricName = PowerShellRunner.GetStr(obj, "MetricName"),
                Value = val,
                Unit = PowerShellRunner.GetStr(obj, "Unit"),
            };

            rows.Add(new PerfSummaryRow
            {
                ObjectName = sample.ObjectName,
                ObjectType = sample.ObjectType,
                MetricName = sample.MetricName,
                CurrentValue = val,
                PeakValue = val,
                AvgValue = val,
                Unit = sample.Unit,
                Trend = "→",
                Series = new PerfSeries
                {
                    SeriesName = sample.ObjectName + " — " + sample.MetricName,
                    Unit = sample.Unit,
                    Samples = [sample],
                },
            });
        }

        return rows;
    }

private static List<PerfSummaryRow> MergeRows(
        List<PerfSummaryRow> primary,
        IReadOnlyList<PerfSummaryRow> secondary)
    {
        var map = primary.ToDictionary(
            r => $"{r.ObjectType}|{r.ObjectName}|{r.MetricName}",
            r => r);

        foreach (var row in secondary)
        {
            var key = $"{row.ObjectType}|{row.ObjectName}|{row.MetricName}";
            if (!map.TryGetValue(key, out var existing) || existing.Series is null || row.Series is null)
            {
                map[key] = row;
                continue;
            }

            var samples = existing.Series.Samples
                .Concat(row.Series.Samples)
                .OrderBy(s => s.Timestamp)
                .ToList();

            var current = samples.Last().Value;
            var peak = samples.Max(s => s.Value);
            var avg = samples.Average(s => s.Value);
            var trend = current > avg * 1.1 ? "↑" : current < avg * 0.9 ? "↓" : "→";

            map[key] = new PerfSummaryRow
            {
                ObjectName = existing.ObjectName,
                ObjectType = existing.ObjectType,
                MetricName = existing.MetricName,
                CurrentValue = current,
                PeakValue = peak,
                AvgValue = Math.Round(avg, 1),
                Unit = existing.Unit,
                Trend = trend,
                Series = new PerfSeries
                {
                    SeriesName = existing.Series.SeriesName,
                    Unit = existing.Series.Unit,
                    Samples = samples
                }
            };
        }

        return map.Values
            .OrderBy(r => r.ObjectType)
            .ThenBy(r => r.ObjectName)
            .ThenBy(r => r.MetricName)
            .ToList();
    }
}
