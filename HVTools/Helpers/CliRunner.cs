// =============================================================================
//  ClusterIQ CLI Mode
//  When ClusterIQ.exe is launched with command-line arguments it runs in
//  headless mode — connecting, collecting, and exporting without showing the UI.
//
//  Examples:
//    # Export all to XLSX using current user credentials
//    ClusterIQ.exe --host hci-cluster01 --export C:\Reports\inv.xlsx
//
//    # Export specific sheets with explicit credentials
//    ClusterIQ.exe --host hci-cluster01 --user CORP\hvadmin --pass Secret1 \
//                --export C:\Reports\inv.xlsx --sheets vmInfo,hvHealth
//
//    # Include Azure plane data
//    ClusterIQ.exe --host hci-cluster01 --export inv.xlsx \
//                --azure --subscription <sub-id> --rg rg-hci-prod
// =============================================================================

using System.Security;
using HVTools.Services;
using Microsoft.Extensions.Logging;

namespace HVTools;

public static class CliRunner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var parsed = ParseArgs(args);

        if (parsed.TryGetValue("--help", out _) || parsed.TryGetValue("-h", out _))
        {
            PrintHelp();
            return 0;
        }

        if (!parsed.TryGetValue("--host", out var host) || string.IsNullOrWhiteSpace(host))
        {
            Console.Error.WriteLine("ERROR: --host is required.");
            return 1;
        }

        if (!parsed.TryGetValue("--export", out var exportPath) || string.IsNullOrWhiteSpace(exportPath))
        {
            Console.Error.WriteLine("ERROR: --export <path> is required.");
            return 1;
        }

        // ── Set up services ──────────────────────────────────────────────
        var logFactory = LoggerFactory.Create(b => b
            .AddConsole()
            .SetMinimumLevel(LogLevel.Information));

        var ps        = new PowerShellRunner(logFactory.CreateLogger<PowerShellRunner>());
        var hvSvc     = new HyperVService(ps, logFactory.CreateLogger<HyperVService>());
        var azSvc     = new AzureLocalService(logFactory.CreateLogger<AzureLocalService>());
        var healthSvc = new HealthCheckService();
        var exportSvc = new ExportService();

        // ── Connect ──────────────────────────────────────────────────────
        Console.WriteLine($"[ClusterIQ] Connecting to {host}...");

        if (parsed.TryGetValue("--user", out var user) && parsed.TryGetValue("--pass", out var pass))
        {
            var secPwd = new SecureString();
            foreach (var ch in pass) secPwd.AppendChar(ch);
            await ps.ConnectAsync(host, user, secPwd);
        }
        else
        {
            await ps.ConnectAsync(host);
        }

        Console.WriteLine("[ClusterIQ] Connected. Collecting inventory...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var (vms, disks, nics, snaps, hosts, clusters, volumes, switches, physNics) =
            await hvSvc.CollectAllAsync(host);

        var s2d = await hvSvc.GetS2DPoolsAsync(host);

        IReadOnlyList<HVTools.Models.AzureArcResource> arc = Array.Empty<HVTools.Models.AzureArcResource>();
        if (parsed.ContainsKey("--azure") &&
            parsed.TryGetValue("--subscription", out var sub) &&
            parsed.TryGetValue("--rg", out var rg))
        {
            Console.WriteLine("[ClusterIQ] Querying Azure plane...");
            if (parsed.TryGetValue("--tenant", out var tenant))
                azSvc.AuthenticateInteractive(tenant);
            else
                azSvc.AuthenticateInteractive();

            arc = await azSvc.GetArcResourcesAsync(sub, rg);
        }

        Console.WriteLine("[ClusterIQ] Running health checks...");
        var partialInv = new HVTools.Models.InventoryResult
        {
            VMs = vms, VmDisks = disks, VmNics = nics, VmSnapshots = snaps,
            Hosts = hosts, Clusters = clusters, Volumes = volumes,
            Switches = switches, PhysicalNics = physNics,
            S2DPools = s2d, ArcResources = arc,
        };

        var checks = healthSvc.Analyse(partialInv);
        sw.Stop();

        var inventory = new HVTools.Models.InventoryResult
        {
            Connection         = new HVTools.Models.ConnectionSettings { HostOrCluster = host },
            CollectedAt        = DateTime.UtcNow,
            CollectionDuration = sw.Elapsed,
            VMs          = vms,   VmDisks      = disks,  VmNics    = nics,
            VmSnapshots  = snaps, Hosts        = hosts,  Clusters  = clusters,
            Volumes      = volumes, Switches   = switches, PhysicalNics = physNics,
            S2DPools     = s2d,   ArcResources = arc,    HealthChecks  = checks,
        };

        Console.WriteLine($"[ClusterIQ] Collected {vms.Count} VMs, {hosts.Count} hosts, " +
                          $"{checks.Count(c => c.Severity == HVTools.Models.HealthSeverity.Error)} errors " +
                          $"in {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"[ClusterIQ] Exporting to {exportPath}...");

        await exportSvc.ExportAllAsync(inventory, exportPath);
        Console.WriteLine($"[ClusterIQ] Done. {exportPath}");

        ps.Dispose();
        return 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    private static Dictionary<string, string> ParseArgs(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--") || args[i].StartsWith("-"))
            {
                var key = args[i];
                var val = (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                          ? args[++i] : "true";
                result[key] = val;
            }
        }
        return result;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            ClusterIQ — Hyper-V & Azure Local Inventory Tool
            ================================================
            Usage:
              ClusterIQ.exe [options]

            Options:
              --host <name>         Hyper-V host or cluster name (required)
              --user <domain\user>  Username (optional, uses current user if omitted)
              --pass <password>     Password (optional)
              --export <path>       Output XLSX file path (required for CLI mode)
              --sheets <list>       Comma-separated sheet names to export (default: all)
              --azure               Also collect Azure Local / Arc data
              --subscription <id>   Azure subscription ID (required with --azure)
              --rg <name>           Azure resource group name (required with --azure)
              --tenant <id>         Azure tenant ID (optional)
              --help, -h            Show this help

            Examples:
              ClusterIQ.exe --host hci-cluster01 --export C:\Reports\inv.xlsx
              ClusterIQ.exe --host hci-cluster01 --user CORP\admin --pass P@ss1 \
                          --export inv.xlsx --azure --subscription <sub-id> --rg rg-hci
            """);
    }
}
