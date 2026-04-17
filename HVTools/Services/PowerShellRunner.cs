// =============================================================================
//  PowerShellRunner
//  Hosts a PowerShell runspace that targets a remote machine via WinRM.
//  All heavy lifting (Get-VM, Get-VMHost, etc.) runs through this runner.
// =============================================================================

using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Security;
using Microsoft.Extensions.Logging;

namespace HVTools.Services;

/// <summary>
/// Wraps a PowerShell runspace connected to a remote Hyper-V host or cluster
/// management endpoint via WinRM/CimSession.
/// </summary>
public sealed class PowerShellRunner : IDisposable
{
    private readonly ILogger<PowerShellRunner> _logger;
    private Runspace? _runspace;
    private bool _disposed;

    public bool IsConnected => _runspace?.RunspaceStateInfo.State == RunspaceState.Opened;

    public PowerShellRunner(ILogger<PowerShellRunner> logger) => _logger = logger;

    // ─────────────────────────────────────────────────────────────────────────
    //  Connect
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Opens a remote runspace using pass-through credentials or explicit
    /// username/password. The remote machine must have WinRM enabled and the
    /// account must be in the local Administrators group.
    /// </summary>
    public async Task ConnectAsync(string computerName,
                                    string? username = null,
                                    SecureString? password = null,
                                    CancellationToken ct = default)
    {
        _runspace?.Dispose();

        _logger.LogInformation("Connecting PowerShell runspace to {Host}", computerName);

        PSCredential? credential = null;
        if (!string.IsNullOrWhiteSpace(username) && password is not null)
            credential = new PSCredential(username, password);

        // Build a WSMan connection to the remote host.
        // The remote endpoint must have the Hyper-V PowerShell module installed
        // (Install-WindowsFeature Hyper-V-PowerShell) and FailoverClusters module.
        var connInfo = new WSManConnectionInfo(
            useSsl: false,
            computerName: computerName,
            port: 5985,
            appName: "/wsman",
            shellUri: "http://schemas.microsoft.com/powershell/Microsoft.PowerShell",
            credential: credential)
        {
            AuthenticationMechanism = AuthenticationMechanism.Negotiate,
            OperationTimeout = 60_000,
            OpenTimeout = 30_000,
        };

        _runspace = RunspaceFactory.CreateRunspace(connInfo);

        await Task.Run(() => _runspace.Open(), ct);
        _runspace.SessionStateProxy.SetVariable("HVToolsCredential", credential);

        // Pre-import the modules we need on the remote side.
        await RunScriptAsync("""
            Import-Module Hyper-V -ErrorAction SilentlyContinue
            Import-Module FailoverClusters -ErrorAction SilentlyContinue
            Import-Module Storage -ErrorAction SilentlyContinue
            """, ct);

        _logger.LogInformation("Runspace opened for {Host}", computerName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ConnectLocal — for when HVTools runs ON the Hyper-V host itself
    // ─────────────────────────────────────────────────────────────────────────
    public async Task ConnectLocalAsync(CancellationToken ct = default)
    {
        _runspace?.Dispose();
        _runspace = RunspaceFactory.CreateRunspace();
        await Task.Run(() => _runspace.Open(), ct);
        _runspace.SessionStateProxy.SetVariable("HVToolsCredential", null);

        await RunScriptAsync("""
            Import-Module Hyper-V -ErrorAction SilentlyContinue
            Import-Module FailoverClusters -ErrorAction SilentlyContinue
            Import-Module Storage -ErrorAction SilentlyContinue
            """, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RunScript — execute arbitrary PS script, return PSObjects
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Collection<PSObject>> RunScriptAsync(string script,
                                                            CancellationToken ct = default)
    {
        ThrowIfNotConnected();

        return await Task.Run(() =>
        {
            using var ps = PowerShell.Create();
            ps.Runspace = _runspace;
            ps.AddScript(script);

            var results = ps.Invoke();

            if (ps.HadErrors)
            {
                foreach (var err in ps.Streams.Error)
                    _logger.LogWarning("PS error: {Err}", err.Exception?.Message);
            }

            return results;
        }, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  RunCommand — execute a single cmdlet with named parameters
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<Collection<PSObject>> RunCommandAsync(string cmdlet,
                                                              Dictionary<string, object?>? parameters = null,
                                                              CancellationToken ct = default)
    {
        ThrowIfNotConnected();

        return await Task.Run(() =>
        {
            using var ps = PowerShell.Create();
            ps.Runspace = _runspace;
            ps.AddCommand(cmdlet);

            if (parameters is not null)
            {
                foreach (var (key, val) in parameters)
                    if (val is not null) ps.AddParameter(key, val);
            }

            var results = ps.Invoke();

            if (ps.HadErrors)
                foreach (var err in ps.Streams.Error)
                    _logger.LogWarning("PS cmdlet error [{Cmd}]: {Err}", cmdlet, err.Exception?.Message);

            return results;
        }, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Safely reads a property from a PSObject, returning a default if absent.
    /// </summary>
    public static T? GetProp<T>(PSObject obj, string propName)
    {
        var val = obj.Properties[propName]?.Value;
        if (val is null) return default;
        try { return (T)Convert.ChangeType(val, typeof(T)); }
        catch { return default; }
    }

    public static string GetStr(PSObject obj, string propName)
        => obj.Properties[propName]?.Value?.ToString() ?? string.Empty;

    public static long GetLong(PSObject obj, string propName)
        => long.TryParse(obj.Properties[propName]?.Value?.ToString(), out var v) ? v : 0;

    public static int GetInt(PSObject obj, string propName)
        => int.TryParse(obj.Properties[propName]?.Value?.ToString(), out var v) ? v : 0;

    public static bool GetBool(PSObject obj, string propName)
        => string.Equals(obj.Properties[propName]?.Value?.ToString(), "true",
                         StringComparison.OrdinalIgnoreCase);

    private void ThrowIfNotConnected()
    {
        if (_runspace is null || _runspace.RunspaceStateInfo.State != RunspaceState.Opened)
            throw new InvalidOperationException("PowerShell runspace is not open. Call ConnectAsync first.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runspace?.Dispose();
    }
}
