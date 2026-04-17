// =============================================================================
//  UpdateService  (v1.1 — @"..." verbatim strings throughout)
// =============================================================================

using HVTools.Models;
using Microsoft.Extensions.Logging;

namespace HVTools.Services;

public sealed class UpdateService
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<UpdateService> _logger;

    public UpdateService(PowerShellRunner ps, ILogger<UpdateService> logger)
    {
        _ps = ps; _logger = logger;
    }

    public async Task<IReadOnlyList<NodeUpdateStatus>> GetUpdateStatusAsync(
        string clusterName, CancellationToken ct = default)
    {
        _logger.LogInformation("Collecting update status from {Cluster}", clusterName);

        string script = "$t = '" + clusterName + "'\r\n" + @"
$nodes = (Get-ClusterNode -Cluster $t -ErrorAction SilentlyContinue).Name
if (-not $nodes) { $nodes = @($t) }

$cauRun = Get-CauReport -ClusterName $t -ErrorAction SilentlyContinue |
          Sort-Object StartTime -Descending | Select-Object -First 1

foreach ($node in $nodes) {
    $psWuAvail = $null -ne (Get-Module PSWindowsUpdate -ListAvailable -ErrorAction SilentlyContinue)
    $pending   = @()
    $critical  = 0
    $kbs       = ''
    $reboot    = $false

    if ($psWuAvail) {
        $updates  = Get-WUList -ComputerName $node -MicrosoftUpdate -ErrorAction SilentlyContinue
        $pending  = $updates
        $critical = ($updates | Where-Object { $_.AutoSelectOnWebSites -eq $true } | Measure-Object).Count
        $kbs      = ($updates | Select-Object -ExpandProperty KB -ErrorAction SilentlyContinue) -join ', '
    } else {
        $wuSb = {
            $s = New-Object -ComObject Microsoft.Update.Session
            $searcher = $s.CreateUpdateSearcher()
            try {
                $res = $searcher.Search(""IsInstalled=0 and Type='Software'"")
                $res.Updates | Select-Object Title, MsrcSeverity, RebootRequired
            } catch { @() }
        }
        $updates  = Invoke-Command -ComputerName $node -ScriptBlock $wuSb -ErrorAction SilentlyContinue
        $pending  = if ($updates) { @($updates) } else { @() }
        $critical = ($pending | Where-Object { $_.MsrcSeverity -eq 'Critical' } | Measure-Object).Count
        $reboot   = ($pending | Where-Object { $_.RebootRequired } | Measure-Object).Count -gt 0
        $kbs      = ($pending | Select-Object -First 10 -ExpandProperty Title -ErrorAction SilentlyContinue) -join '; '
    }

    $lastInstSb = {
        $s = New-Object -ComObject Microsoft.Update.Session
        $h = $s.CreateUpdateSearcher()
        $total = $h.GetTotalHistoryCount()
        if ($total -gt 0) {
            ($h.QueryHistory(0,[Math]::Min($total,5)) |
             Where-Object { $_.ResultCode -eq 2 } |
             Sort-Object Date -Descending | Select-Object -First 1).Date
        }
    }
    $lastInst = Invoke-Command -ComputerName $node -ScriptBlock $lastInstSb -ErrorAction SilentlyContinue

    $rebootKey  = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired'
    $rebootPend = Invoke-Command -ComputerName $node -ScriptBlock { param($k) Test-Path $k } `
                  -ArgumentList $rebootKey -ErrorAction SilentlyContinue

    $os = Get-CimInstance -ComputerName $node Win32_OperatingSystem -ErrorAction SilentlyContinue

    $readiness = if ($rebootPend -or $reboot) { 'RequiresReboot' }
                 elseif (@($pending).Count -gt 0) { 'PendingUpdates' }
                 else { 'UpToDate' }

    [PSCustomObject]@{
        NodeName       = $node
        Readiness      = $readiness
        PendingCount   = @($pending).Count
        CriticalCount  = $critical
        LastInstalled  = $lastInst
        PendingKBs     = $kbs
        RequiresReboot = [bool]($rebootPend -or $reboot)
        CauLastRun     = if ($cauRun) { $cauRun.StartTime.ToString('yyyy-MM-dd') } else { 'Never' }
        CauLastResult  = if ($cauRun) { $cauRun.CauJobState } else { 'N/A' }
        WindowsVersion = $os.Caption
    }
}";
        var results = await _ps.RunScriptAsync(script, ct);
        var list = new List<NodeUpdateStatus>(results.Count);
        foreach (var obj in results)
        {
            var readinessStr = PowerShellRunner.GetStr(obj, "Readiness");
            var readiness = readinessStr switch
            {
                "RequiresReboot" => UpdateReadiness.RequiresReboot,
                "PendingUpdates" => UpdateReadiness.PendingUpdates,
                "UpToDate"       => UpdateReadiness.UpToDate,
                _                => UpdateReadiness.Unknown,
            };
            var lastInstalled = obj.Properties["LastInstalled"]?.Value is DateTime dt ? dt : (DateTime?)null;
            list.Add(new NodeUpdateStatus
            {
                NodeName       = PowerShellRunner.GetStr(obj, "NodeName"),
                Readiness      = readiness,
                PendingCount   = PowerShellRunner.GetInt(obj, "PendingCount"),
                CriticalCount  = PowerShellRunner.GetInt(obj, "CriticalCount"),
                LastChecked    = DateTime.Now,
                LastInstalled  = lastInstalled,
                PendingKBs     = PowerShellRunner.GetStr(obj, "PendingKBs"),
                RequiresReboot = PowerShellRunner.GetBool(obj, "RequiresReboot"),
                CauLastRun     = PowerShellRunner.GetStr(obj, "CauLastRun"),
                CauLastResult  = PowerShellRunner.GetStr(obj, "CauLastResult"),
                WindowsVersion = PowerShellRunner.GetStr(obj, "WindowsVersion"),
            });
        }
        _logger.LogInformation("Update status collected for {Count} nodes", list.Count);
        return list;
    }
}
