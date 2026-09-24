# Headless Playbook Executor

**ClusterIQ.Headless** is a companion console / Windows Service binary that hosts the maintenance scheduler. With it installed, scheduled playbooks fire on cadence even when nobody has ClusterIQ.exe open.

The headless executor uses the same `playbooks.json`, the same `playbook-runs.json` audit trail, and the same step runners as the GUI. Authoring still happens in the main app — operators design playbooks via the Autopilot tab, then the headless service runs them on schedule.

## Why it exists

Pre-v2.0 Tier 4, scheduled playbooks only fired while the GUI was running. That's fine for a single-operator workflow but breaks down for production cluster maintenance: nobody wants their nightly snapshot cleanup to depend on a desktop session staying logged in.

The headless executor solves this without duplicating logic: it imports the GUI's services and hosts them in a console / SCM-aware container. Same code paths, different lifecycle.

## Mental model

Two binaries, one configuration:

| | ClusterIQ.exe (GUI) | ClusterIQ.Headless.exe |
|---|---|---|
| Authoring (`playbooks.json` writes) | Yes | No |
| Scheduled playbook execution | Yes (when no headless lease) | Yes |
| Manual playbook execution | Yes (always) | Only via `--run-once` |
| Audit trail writes (`playbook-runs.json`) | Yes | Yes |
| Notification dispatch on playbook completion | Yes (when GUI is the active scheduler) | Yes (when headless is the active scheduler) |
| Scheduled-report execution | Yes (full report contents) | Yes (run-history + inventory sections; see Limitations) |

When the GUI sees a fresh headless lease, it pauses its scheduler and shows a banner. Manual runs from the Autopilot tab still work.

## Five CLI modes

```
ClusterIQ.Headless                          # default — Windows Service worker
ClusterIQ.Headless --service                # explicit Service mode
ClusterIQ.Headless --interactive            # foreground console
ClusterIQ.Headless --run-once <id|name>     # run a single playbook by id or name, then exit
ClusterIQ.Headless --install                # register as a Windows Service (admin required)
ClusterIQ.Headless --uninstall              # stop and remove the service (admin required)
```

**`--service`** and **the default** are equivalent. SCM launches the binary with the registered command-line; `Microsoft.Extensions.Hosting` detects the SCM environment.

**`--interactive`** runs the same background service inline in a console. Useful for debugging — logs go to both stdout and the file sink.

**`--run-once`** skips the scheduler entirely. Connect, materialize the playbook by id (or by case-insensitive name match), call `RunPlaybookManuallyAsync`, exit. Useful for cron-driven invocation patterns and testing individual playbooks from the command line. Exit codes: `0` success, `2` connection failure, `3` playbook not found.

## Setup

### 1. Connection configuration

Headless connects to the cluster on startup using saved settings. The lookup order is:

1. `%PROGRAMDATA%\ClusterIQ\headless-connection.json` (preferred — machine-wide)
2. `%APPDATA%\ClusterIQ\headless-connection.json` (fallback for interactive testing)
3. If neither exists, the service emits a template at the preferred path and refuses to start

The file is the `ConnectionSettings` JSON shape the GUI uses. Three auth modes are supported in v2.0:

**A. Integrated Windows authentication (default, recommended).** The service account model — the service runs under a domain account with cluster admin and WinRM uses the process identity.

```json
{
  "HostOrCluster": "prod-cluster.contoso.local",
  "TargetType": "AzureLocalCluster",
  "UseCurrentUser": true,
  "ConnectAzurePlane": false
}
```

**B. Windows Credential Manager.** When the service account itself shouldn't have cluster admin, provision a separate credential via `cmdkey` (the credential is stored in the LSA secret store, encrypted per-user, never on disk in a config file):

```cmd
cmdkey /generic:ClusterIQ.Headless.Prod /user:CONTOSO\svc-clusteriq /pass:<secret>
```

Then point the JSON at it:

```json
{
  "HostOrCluster": "prod-cluster.contoso.local",
  "UseCurrentUser": false,
  "CredentialTarget": "ClusterIQ.Headless.Prod"
}
```

The credential is readable only by the user that ran `cmdkey`. If headless runs as `LocalSystem`, run `cmdkey` from a SYSTEM context (psexec -s) or, preferably, change the service account first (`sc.exe config ClusterIQ.Headless obj= "CONTOSO\svc-runner" password= "<password>"`) and run cmdkey as that account.

**C. Inline username/password (discouraged).** Supported as a fallback when neither integrated nor Credential Manager fits, but the password is then plaintext-on-disk:

```json
{
  "HostOrCluster": "prod-cluster.contoso.local",
  "UseCurrentUser": false,
  "Username": "CONTOSO\\svc-clusteriq",
  "Password": "<secret>"
}
```

Headless logs a warning at startup whenever this mode is in use, so the choice is visible in the log.

### 2. Install as a Windows Service

Requires Administrator:

```cmd
ClusterIQ.Headless.exe --install
sc.exe start ClusterIQ.Headless
```

The installer runs three `sc.exe` commands: `create` (with `start= auto`), `description`, and `failure` (set to restart 3× at 60s intervals on crash). The `binPath` registered with SCM includes the `--service` argument so subsequent SCM launches unambiguously pick Service mode.

### 3. Configure the service account

The headless service runs as `LocalSystem` by default — which works on a clustered host where LocalSystem is granted cluster admin (via Add-ClusterAccess or domain group membership). For most production setups, change to a dedicated domain service account:

```cmd
sc.exe config ClusterIQ.Headless obj= "CONTOSO\svc-clusteriq" password= "<password>"
```

Grant the service account:
- Local Administrator on each cluster node
- Cluster Administrator permissions
- Hyper-V Administrators group membership

Document the grant in your runbook — when the password rotates, both Windows (the service password) and the cluster (the access list) need updating.

### 4. Verify

After starting, check the log file:

```
%PROGRAMDATA%\ClusterIQ\logs\headless-<date>.log
```

A successful startup logs:
1. Headless lease claimed at `%PROGRAMDATA%\ClusterIQ\headless.lease`
2. `Connecting PowerShell to <host>`
3. `Connected.`
4. `Maintenance scheduler started; tick interval is 60s by default`

If any of these are missing, the startup failed and SCM marked the service as Failed — check the log for the specific error.

## Coordination with the GUI

When ClusterIQ.exe starts, the GUI scheduler reads `%PROGRAMDATA%\ClusterIQ\headless.lease`. If the file exists and the heartbeat is fresh (≤ 90 seconds old) and the recorded PID is alive, the GUI:

- Stops its own scheduler so playbooks don't fire twice
- Shows a top-of-window blue banner: "ClusterIQ Headless is running."

The lease is heartbeated every 30 seconds, so a stale lease (90s without heartbeat) is reclaimable. If the headless service crashes, the GUI banner clears within 90s and the GUI scheduler resumes.

The GUI checks the lease state every 30 seconds. Manual playbook runs from the Autopilot tab always work, regardless of the lease.

## Lease file format

Stored at `%PROGRAMDATA%\ClusterIQ\headless.lease` (CommonApplicationData rather than AppData because LocalSystem doesn't have a user profile but does have CommonApplicationData access):

```
12848
2026-06-24T14:30:00.0000000Z
2026-06-24T14:45:30.0000000Z
```

Line 1 is the process ID, line 2 the start time, line 3 the heartbeat. The heartbeat is the file's `LastWriteTimeUtc`, not the content — touching mtime is cheaper than rewriting the file every 30 seconds.

## Service uninstall

```cmd
ClusterIQ.Headless.exe --uninstall
```

Wraps `sc.exe stop` (best-effort — ignores "not running") followed by `sc.exe delete`. Requires Administrator. After uninstall the GUI scheduler resumes within 90 seconds (the lease becomes stale; the GUI's next poll clears the banner).

## Honest limitations

- **Single-node assumption.** If you install the service on multiple cluster nodes simultaneously, both will claim the lease and one will lose. Behavior on this race isn't graceful — the loser logs the failure and SCM marks it failed. Don't install on more than one node; run headless on the management server, not the cluster nodes themselves.
- **Inventory analyzers not wired in v1.** `InventoryCollector` populates the Hyper-V plane (VMs, hosts, storage, networking, snapshots, S2D) but doesn't yet run the post-collection analyzers that produce `HealthChecks` and `AlertInsights`. Reports fired from headless will show those two sections empty (the GUI fills them). Wiring the analyzers into the headless host is straightforward — they're pure post-processing — and is a v2.1 follow-up.
- **Azure plane omitted.** `InventoryCollector` skips Azure Arc and Azure Local plane data. The GUI's Azure paths use interactive auth that the unattended service can't reproduce without operator-provisioned service principals; deferred to v2.1.
- **GUI-accumulated state remains GUI-only.** Drift history, posture deltas, and the recommendation queue are derived from the GUI's running session state, not from disk. Headless reports therefore render those sections empty. The maintenance-runs section, inventory sections, and executive summary are the headline contents of a headless-fired report.
