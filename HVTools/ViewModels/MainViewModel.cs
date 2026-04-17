// =============================================================================
//  MainViewModel  (v1.0)
//  New: multi-cluster sidebar, performance history, update status, dark mode,
//       CSV export option.
// =============================================================================

using System.Collections.ObjectModel;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HVTools.Models;
using HVTools.Services;
using Microsoft.Extensions.Logging;

namespace HVTools.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly PowerShellRunner    _ps;
    private readonly HyperVService       _hvSvc;
    private readonly AzureLocalService   _azSvc;
    private readonly HealthCheckService  _healthSvc;
    private readonly ExportService       _exportSvc;
    private readonly PerfHistoryService  _perfSvc;
    private readonly UpdateService       _updateSvc;
    private readonly HealthScoreService  _scoreSvc = new();
    private readonly RecommendationService _recommendationSvc = new();
    private readonly AlertInsightService _alertInsightSvc;
    private readonly AlertThresholdSettings _alertThresholds;
    private readonly SnapshotHistoryService _snapshotSvc = new();
    private readonly PlacementRecommendationService _placementSvc = new();
    private readonly CapacityForecastService _forecastSvc = new();
    private readonly ComplianceCheckService _complianceSvc = new();
    private readonly NotificationService _notificationSvc;
    private readonly ActionLogService _actionLogSvc = new();
    private readonly NotificationSettings _notificationSettings;
    private readonly ILogger<MainViewModel> _logger;

    // ─── Connection fields ────────────────────────────────────────────────────
    [ObservableProperty] private string _targetHost       = string.Empty;
    [ObservableProperty] private string _username         = string.Empty;
    [ObservableProperty] private bool   _useCurrentUser   = true;
    [ObservableProperty] private bool   _saveConnectionInfo = false;
    [ObservableProperty] private bool   _connectAzure     = false;
    [ObservableProperty] private string _azureSubscription  = string.Empty;
    [ObservableProperty] private string _azureResourceGroup = string.Empty;
    [ObservableProperty] private string _azureTenantId      = string.Empty;
    [ObservableProperty] private string _liveMigrationAuthMode = "Kerberos";
    [ObservableProperty] private bool _applyLiveMigrationAuthOnConnect = false;

    // ─── State ────────────────────────────────────────────────────────────────
    [ObservableProperty] private bool   _isConnected;
    [ObservableProperty] private bool   _isCollecting;
    [ObservableProperty] private string _statusText      = "Not connected";
    [ObservableProperty] private string _connectionBadge = "Disconnected";
    [ObservableProperty] private int    _progress;
    [ObservableProperty] private string _selectedTab     = "vmInfo";

    // ─── Dark mode ────────────────────────────────────────────────────────────
    [ObservableProperty] private bool _isDarkMode = false;

    // ─── Multi-cluster ────────────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<ClusterSession> _clusterSessions = [];
    [ObservableProperty] private ClusterSession? _activeSession;
    [ObservableProperty] private ObservableCollection<SavedEnvironmentProfile> _savedProfiles = [];
    [ObservableProperty] private SavedEnvironmentProfile? _selectedSavedProfile;
    [ObservableProperty] private string _profileName = string.Empty;

    // ─── Inventory collections ────────────────────────────────────────────────
    [ObservableProperty] private ObservableCollection<VmInfo>           _vmList      = [];
    [ObservableProperty] private VmInfo? _selectedVm;
    [ObservableProperty] private string _liveMigrationTargetHost = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _liveMigrationHosts = [];
    [ObservableProperty] private ObservableCollection<VmDisk>           _diskList    = [];
    [ObservableProperty] private ObservableCollection<VmNic>            _nicList     = [];
    [ObservableProperty] private ObservableCollection<VmSnapshot>       _snapList    = [];
    [ObservableProperty] private ObservableCollection<HvHost>           _hostList    = [];
    [ObservableProperty] private ObservableCollection<HvCluster>        _clusterList = [];
    [ObservableProperty] private ObservableCollection<HvStorage>        _storageList = [];
    [ObservableProperty] private ObservableCollection<HvSwitch>         _switchList  = [];
    [ObservableProperty] private ObservableCollection<HvPhysicalNic>    _physNicList = [];
    [ObservableProperty] private ObservableCollection<HealthCheck>      _healthList  = [];
    [ObservableProperty] private ObservableCollection<AzureArcResource> _arcList     = [];
    [ObservableProperty] private ObservableCollection<AzureS2DPool>     _s2dList     = [];
    [ObservableProperty] private ObservableCollection<NodeUpdateStatus> _updateList  = [];
    [ObservableProperty] private ObservableCollection<PerfSummaryRow>   _perfList    = [];

    // ─── Summary metrics ──────────────────────────────────────────────────────
    [ObservableProperty] private int _totalVMs;
    [ObservableProperty] private int _runningVMs;
    [ObservableProperty] private int _offVMs;
    [ObservableProperty] private int _hostCount;
    [ObservableProperty] private int _snapshotCount;
    [ObservableProperty] private int _healthAlertCount;
    [ObservableProperty] private int _pendingUpdateCount;
    [ObservableProperty] private int _overallHealthScore;
    [ObservableProperty] private int _recommendationCount;
    [ObservableProperty] private int _imbalanceCount;
    [ObservableProperty] private int _openAlertCount;
    [ObservableProperty] private int _criticalAlertCount;
    [ObservableProperty] private int _driftChangeCount;
    [ObservableProperty] private int _placementRecommendationCount;
    [ObservableProperty] private int _forecastFindingCount;
    [ObservableProperty] private int _complianceFindingCount;
    [ObservableProperty] private int _snapshotRetentionCount = 10;
    [ObservableProperty] private bool _enableFileNotifications = true;
    [ObservableProperty] private string _notificationOutputFolder = string.Empty;
    [ObservableProperty] private bool _enableTeamsWebhook = false;
    [ObservableProperty] private string _teamsWebhookUrl = string.Empty;
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<DriftChange> _driftChanges = new();
    [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<AlertInsight> _alertInsights = new();

    [ObservableProperty] private double _hostCpuWarningPercent = 75;
    [ObservableProperty] private double _hostCpuCriticalPercent = 90;
    [ObservableProperty] private int _memoryImbalanceWarningSpread = 20;
    [ObservableProperty] private int _memoryImbalanceCriticalSpread = 30;
    [ObservableProperty] private int _vmSpreadWarning = 3;
    [ObservableProperty] private int _vmSpreadCritical = 5;

    // ─── Selected perf row (for sparkline chart) ─────────────────────────────
    [ObservableProperty] private PerfSummaryRow? _selectedPerfRow;
    [ObservableProperty] private int _perfRefreshSeconds = 15;
    [ObservableProperty] private DateTime? _lastRefreshedAt;

    private InventoryResult?              _lastInventory;
    private IReadOnlyList<NodeUpdateStatus> _lastUpdates  = [];
    private IReadOnlyList<PerfSummaryRow>   _lastPerf     = [];

    public MainViewModel(
        PowerShellRunner ps, HyperVService hvSvc, AzureLocalService azSvc,
        HealthCheckService healthSvc, ExportService exportSvc,
        PerfHistoryService perfSvc, UpdateService updateSvc,
        ILogger<MainViewModel> logger)
    {
        _ps = ps; _hvSvc = hvSvc; _azSvc = azSvc;
        _healthSvc = healthSvc; _exportSvc = exportSvc;
        _perfSvc = perfSvc; _updateSvc = updateSvc;
        _logger = logger;
        _alertThresholds = AlertSettingsStore.Load();
        _alertInsightSvc = new AlertInsightService(_alertThresholds);
        _notificationSettings = NotificationSettingsStore.Load();
        EnableFileNotifications = _notificationSettings.EnableFileNotifications;
        NotificationOutputFolder = _notificationSettings.NotificationOutputFolder;
        EnableTeamsWebhook = _notificationSettings.EnableTeamsWebhook;
        TeamsWebhookUrl = _notificationSettings.TeamsWebhookUrl;
        SnapshotRetentionCount = _notificationSettings.SnapshotRetentionCount <= 0 ? 10 : _notificationSettings.SnapshotRetentionCount;
        _notificationSvc = new NotificationService(_notificationSettings);
        SavedProfiles = new(System.Linq.Enumerable.OrderBy(SavedEnvironmentProfileStore.Load(), p => p.ProfileName));
    }

public void ApplyUserPreferences(UserPreferences prefs)
{
    SaveConnectionInfo = prefs.SaveConnectionInfo;
    IsDarkMode = prefs.IsDarkMode;

    if (SaveConnectionInfo)
    {
        TargetHost = prefs.TargetHost ?? string.Empty;
        Username = prefs.Username ?? string.Empty;
        UseCurrentUser = prefs.UseCurrentUser;
        ConnectAzure = prefs.ConnectAzure;
        AzureSubscription = prefs.AzureSubscription ?? string.Empty;
        AzureResourceGroup = prefs.AzureResourceGroup ?? string.Empty;
        AzureTenantId = prefs.AzureTenantId ?? string.Empty;
        LiveMigrationAuthMode = string.IsNullOrWhiteSpace(prefs.LiveMigrationAuthMode) ? "Kerberos" : prefs.LiveMigrationAuthMode;
        ApplyLiveMigrationAuthOnConnect = prefs.ApplyLiveMigrationAuthOnConnect;
    }
}

private void PersistPreferences()
{
    var prefs = new UserPreferences
    {
        IsDarkMode = IsDarkMode,
        SaveConnectionInfo = SaveConnectionInfo,
        TargetHost = SaveConnectionInfo ? TargetHost : string.Empty,
        Username = SaveConnectionInfo ? Username : string.Empty,
        UseCurrentUser = SaveConnectionInfo ? UseCurrentUser : true,
        ConnectAzure = SaveConnectionInfo && ConnectAzure,
        AzureSubscription = SaveConnectionInfo ? AzureSubscription : string.Empty,
        AzureResourceGroup = SaveConnectionInfo ? AzureResourceGroup : string.Empty,
        AzureTenantId = SaveConnectionInfo ? AzureTenantId : string.Empty,
        LiveMigrationAuthMode = string.IsNullOrWhiteSpace(LiveMigrationAuthMode) ? "Kerberos" : LiveMigrationAuthMode,
        ApplyLiveMigrationAuthOnConnect = ApplyLiveMigrationAuthOnConnect,
    };

    UserPreferencesStore.Save(prefs);
}

partial void OnSaveConnectionInfoChanged(bool value) => PersistPreferences();
partial void OnTargetHostChanged(string value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnUsernameChanged(string value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnUseCurrentUserChanged(bool value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnConnectAzureChanged(bool value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnAzureSubscriptionChanged(string value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnAzureResourceGroupChanged(string value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnAzureTenantIdChanged(string value) { if (SaveConnectionInfo) PersistPreferences(); }
partial void OnIsDarkModeChanged(bool value) => PersistPreferences();
partial void OnLiveMigrationAuthModeChanged(string value) => PersistPreferences();
partial void OnApplyLiveMigrationAuthOnConnectChanged(bool value) => PersistPreferences();

    // ─────────────────────────────────────────────────────────────────────────
    //  Dark Mode toggle
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        ThemeManager.Apply(IsDarkMode ? "DarkTheme" : "LightTheme");
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Connect — adds a new ClusterSession to the sidebar
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public async Task ConnectAsync(SecureString? password = null)
    {
        if (string.IsNullOrWhiteSpace(TargetHost))
        {
            StatusText = "Enter a host or cluster name.";
            return;
        }

        // Check if already connected to this cluster
        var existing = ClusterSessions.FirstOrDefault(
            s => s.ClusterName.Equals(TargetHost, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SwitchToSession(existing);
            return;
        }

        var session = new ClusterSession
        {
            ClusterName = TargetHost,
            Settings    = BuildConnectionSettings(),
            Status      = "Connecting…",
            IsActive    = true,
        };

        ClusterSessions.Add(session);
        ActiveSession = session;
        IsCollecting  = true;
        StatusText    = $"Connecting to {TargetHost}…";
        Progress      = 5;

        try
        {
            if (UseCurrentUser)
                await _ps.ConnectLocalAsync();
            else
                await _ps.ConnectAsync(TargetHost, Username, password);

            if (ConnectAzure)
            {
                StatusText = "Authenticating to Azure…";
                Progress   = 10;
                _azSvc.AuthenticateInteractive(string.IsNullOrWhiteSpace(AzureTenantId) ? null : AzureTenantId);
            }

            session.Status    = "Connected";
            IsConnected       = true;
            ConnectionBadge   = "Connected";
            Progress          = 15;

            if (ApplyLiveMigrationAuthOnConnect)
            {
                StatusText = $"Applying live migration auth mode ({LiveMigrationAuthMode}) to connected hosts…";
                await _hvSvc.SetLiveMigrationAuthenticationForClusterAsync(session.ClusterName, LiveMigrationAuthMode);
            }

            await CollectForSessionAsync(session);
            PersistPreferences();
        }
        catch (Exception ex)
        {
            session.Status  = "Failed";
            IsConnected     = false;
            ConnectionBadge = "Failed";
            StatusText      = $"Connection failed: {ex.Message}";
            _logger.LogError(ex, "Connection to {Host} failed", TargetHost);
        }
        finally
        {
            IsCollecting = false;
            Progress = 0;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Switch between connected clusters
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public void SwitchToSession(ClusterSession session)
    {
        foreach (var s in ClusterSessions) s.IsActive = false;
        session.IsActive = true;
        ActiveSession    = session;

        if (session.Inventory is not null)
        {
            PopulateCollections(session.Inventory, _lastUpdates, _lastPerf);
            LastRefreshedAt = session.CollectedAt.HasValue ? session.CollectedAt.Value.ToLocalTime() : DateTime.Now;
        }

        StatusText      = $"Viewing {session.ClusterName}";
        IsConnected     = session.Status == "Connected";
        ConnectionBadge = session.Status;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Remove a cluster session
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public void RemoveSession(ClusterSession session)
    {
        ClusterSessions.Remove(session);
        if (ClusterSessions.Count > 0 && ActiveSession == session)
            SwitchToSession(ClusterSessions.Last());
        else if (ClusterSessions.Count == 0)
        {
            ActiveSession = null;
            IsConnected   = false;
            ClearCollections();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Disconnect current session
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public void Disconnect()
    {
        if (ActiveSession is not null)
        {
            ActiveSession.Status = "Disconnected";
            RemoveSession(ActiveSession);
        }
        _ps.Dispose();
        IsConnected     = false;
        ConnectionBadge = "Disconnected";
        StatusText      = "Disconnected";
        PersistPreferences();
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Collect (refresh)
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public async Task CollectAsync()
    {
        if (ActiveSession is null || !_ps.IsConnected) { StatusText = "Not connected."; return; }
        await CollectForSessionAsync(ActiveSession);
        var selectedName = SelectedVm?.Name ?? string.Empty;
        var migratedVm = VmList.FirstOrDefault(v => v.Name.Equals(selectedName, StringComparison.OrdinalIgnoreCase));
        if (migratedVm is not null)
        {
            StatusText = migratedVm.Host.Equals(LiveMigrationTargetHost, StringComparison.OrdinalIgnoreCase)
                ? $"Live migration confirmed: {selectedName} is now on {LiveMigrationTargetHost}."
                : $"Live migration command finished, but inventory shows {selectedName} on {migratedVm.Host}.";
        }
    }

    private async Task CollectForSessionAsync(ClusterSession session)
    {
        IsCollecting = true;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            StatusText = "Collecting VM data…";          Progress = 20;
            var (vms, disks, nics, snaps, hosts, clusters, volumes, switches, physNics) =
                await _hvSvc.CollectAllAsync(session.ClusterName);

            StatusText = "Collecting S2D pool data…";   Progress = 45;
            var s2d = await _hvSvc.GetS2DPoolsAsync(session.ClusterName);

            StatusText = "Collecting performance data…"; Progress = 55;
            var perf = await _perfSvc.GetPerfSummaryAsync(session.ClusterName);

            StatusText = "Collecting update status…";   Progress = 65;
            var updates = await _updateSvc.GetUpdateStatusAsync(session.ClusterName);

            IReadOnlyList<AzureArcResource> arc = [];
            if (ConnectAzure)
            {
                if (string.IsNullOrWhiteSpace(AzureSubscription))
                {
                    throw new InvalidOperationException("Azure subscription is required when Connect Azure is enabled.");
                }

                StatusText = string.IsNullOrWhiteSpace(AzureResourceGroup)
                    ? "Querying Azure Arc resources across subscription…"
                    : "Querying Azure Arc resources…";
                Progress = 75;

                arc = await _azSvc.GetArcResourcesAsync(
                    AzureSubscription,
                    string.IsNullOrWhiteSpace(AzureResourceGroup) ? null : AzureResourceGroup);
            }

            StatusText = "Running health checks…";      Progress = 85;

            // Build partial first for health check analysis
            var partialInv = new InventoryResult
            {
                VMs = vms, VmDisks = disks, VmNics = nics, VmSnapshots = snaps,
                Hosts = hosts, Clusters = clusters, Volumes = volumes,
                Switches = switches, PhysicalNics = physNics,
                S2DPools = s2d, ArcResources = arc,
            };
            var baseChecks = _healthSvc.Analyse(partialInv, updates, perf);
            var scoreChecks = _scoreSvc.Analyse(partialInv, updates, perf);
            var recommendationChecks = _recommendationSvc.Analyse(partialInv, updates, perf, baseChecks.Concat(scoreChecks).ToList());
            var checks = baseChecks.Concat(scoreChecks).Concat(recommendationChecks).ToList();
            var overallScore = _scoreSvc.CalculateOverallScore(partialInv, updates, perf);
            var alertInsights = _alertInsightSvc.Build(partialInv, updates, perf, checks);
            var snapshotBase = new InventoryResult
            {
                Connection = partialInv.Connection,
                VMs = partialInv.VMs,
                VmDisks = partialInv.VmDisks,
                VmNics = partialInv.VmNics,
                VmSnapshots = partialInv.VmSnapshots,
                Hosts = partialInv.Hosts,
                Clusters = partialInv.Clusters,
                Volumes = partialInv.Volumes,
                Switches = partialInv.Switches,
                PhysicalNics = partialInv.PhysicalNics,
                HealthChecks = checks,
                ArcResources = partialInv.ArcResources,
                S2DPools = partialInv.S2DPools,
                AlertInsights = alertInsights
            };
            var currentSnapshot = _snapshotSvc.BuildSnapshot(snapshotBase);
            var previousSnapshot = _snapshotSvc.LoadPrevious(partialInv.Connection.HostOrCluster);
            var driftChanges = _snapshotSvc.Compare(previousSnapshot, currentSnapshot);
            var driftChecks = _snapshotSvc.ToHealthChecks(driftChanges);
            checks = checks.Concat(driftChecks).ToList();
            var placementChecks = _placementSvc.Analyse(partialInv, perf);
            var forecastChecks = _forecastSvc.Analyse(partialInv.Connection.HostOrCluster);
            var complianceChecks = _complianceSvc.Analyse(partialInv);
            checks = checks.Concat(placementChecks).Concat(forecastChecks).Concat(complianceChecks).ToList();

            sw.Stop();
            var inventory = new InventoryResult
            {
                Connection          = session.Settings,
                CollectedAt         = DateTime.UtcNow,
                CollectionDuration  = sw.Elapsed,
                VMs = vms, VmDisks = disks, VmNics = nics, VmSnapshots = snaps,
                Hosts = hosts, Clusters = clusters, Volumes = volumes,
                Switches = switches, PhysicalNics = physNics,
                S2DPools = s2d, ArcResources = arc, HealthChecks = checks,
            };

            session.Inventory   = inventory;
            session.CollectedAt = DateTime.UtcNow;
            session.Status      = "Connected";
            LastRefreshedAt     = DateTime.Now;

            _lastInventory = inventory;
            _lastUpdates   = updates;
            _lastPerf      = perf;

            PopulateCollections(inventory, updates, perf);

            _snapshotSvc.Save(currentSnapshot, SnapshotRetentionCount);
            await _notificationSvc.NotifyAsync(inventory, checks);

            StatusText = $"Collected {vms.Count} VMs, {hosts.Count} hosts, " +
                         $"{checks.Count(c => c.Severity == HealthSeverity.Error)} errors, " +
                         $"score {overallScore}/100, alerts {alertInsights.Count(a => a.Status == AlertStatus.Open)}, changes {driftChanges.Count}, placement {placementChecks.Count}, forecast {forecastChecks.Count}, compliance {complianceChecks.Count} ({sw.Elapsed.TotalSeconds:F1}s)";
            Progress = 100;
            await Task.Delay(400);
            Progress = 0;
        }
        catch (Exception ex)
        {
            StatusText = $"Collection error: {ex.Message}";
            _logger.LogError(ex, "Collection failed for {Host}", session.ClusterName);
        }
        finally
        {
            IsCollecting = false;
        }
    }

public async Task RefreshPerfOnlyAsync()
{
    if (ActiveSession is null || !_ps.IsConnected || IsCollecting) return;

    try
    {
        var latest = await _perfSvc.GetPerfSummaryAsync(ActiveSession.ClusterName);
        _lastPerf = MergePerfRows(_lastPerf, latest);

        string? selectedKey = SelectedPerfRow is null
            ? null
            : $"{SelectedPerfRow.ObjectType}|{SelectedPerfRow.ObjectName}|{SelectedPerfRow.MetricName}";

        PerfList = new(_lastPerf);

        if (!string.IsNullOrWhiteSpace(selectedKey))
        {
            SelectedPerfRow = PerfList.FirstOrDefault(p =>
                $"{p.ObjectType}|{p.ObjectName}|{p.MetricName}" == selectedKey);
        }

        StatusText = $"Performance refreshed at {DateTime.Now:HH:mm:ss}";
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Performance refresh failed for {Host}", ActiveSession.ClusterName);
        StatusText = $"Performance refresh error: {ex.Message}";
    }
}


[RelayCommand]
public void SaveCurrentProfile()
{
    var profileName = string.IsNullOrWhiteSpace(ProfileName) ? TargetHost : ProfileName;
    if (string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(TargetHost))
    {
        StatusText = "Enter a profile name and host/cluster before saving.";
        return;
    }

    var profiles = SavedEnvironmentProfileStore.Load().ToList();
    var existing = profiles.FirstOrDefault(p => p.ProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase));
    if (existing is null)
    {
        existing = new SavedEnvironmentProfile();
        profiles.Add(existing);
    }

    existing.ProfileName = profileName;
    existing.TargetHost = TargetHost;
    existing.Username = Username;
    existing.UseCurrentUser = UseCurrentUser;
    existing.ConnectAzure = ConnectAzure;
    existing.AzureSubscription = AzureSubscription;
    existing.AzureResourceGroup = AzureResourceGroup;
    existing.AzureTenantId = AzureTenantId;

    SavedEnvironmentProfileStore.Save(profiles.OrderBy(p => p.ProfileName).ToList());
    RefreshSavedProfiles();
    SelectedSavedProfile = SavedProfiles.FirstOrDefault(p => p.ProfileName.Equals(profileName, StringComparison.OrdinalIgnoreCase));
    StatusText = $"Saved profile '{profileName}'.";
}

[RelayCommand]
public void LoadSelectedProfile()
{
    if (SelectedSavedProfile is null)
    {
        StatusText = "Select a saved profile first.";
        return;
    }

    ProfileName = SelectedSavedProfile.ProfileName;
    TargetHost = SelectedSavedProfile.TargetHost;
    Username = SelectedSavedProfile.Username;
    UseCurrentUser = SelectedSavedProfile.UseCurrentUser;
    ConnectAzure = SelectedSavedProfile.ConnectAzure;
    AzureSubscription = SelectedSavedProfile.AzureSubscription;
    AzureResourceGroup = SelectedSavedProfile.AzureResourceGroup;
    AzureTenantId = SelectedSavedProfile.AzureTenantId;
    StatusText = $"Loaded profile '{SelectedSavedProfile.ProfileName}'.";
}

[RelayCommand]
public void DeleteSelectedProfile()
{
    if (SelectedSavedProfile is null)
    {
        StatusText = "Select a saved profile first.";
        return;
    }

    var name = SelectedSavedProfile.ProfileName;
    var profiles = SavedEnvironmentProfileStore.Load()
        .Where(p => !p.ProfileName.Equals(name, StringComparison.OrdinalIgnoreCase))
        .ToList();
    SavedEnvironmentProfileStore.Save(profiles);
    RefreshSavedProfiles();
    SelectedSavedProfile = null;
    StatusText = $"Deleted profile '{name}'.";
}


[RelayCommand]
public async Task StartSelectedVmAsync()
{
    await ExecuteSelectedVmActionAsync("Start");
}

[RelayCommand]
public async Task StopSelectedVmAsync()
{
    await ExecuteSelectedVmActionAsync("Stop");
}

[RelayCommand]
public async Task RestartSelectedVmAsync()
{
    await ExecuteSelectedVmActionAsync("Restart");
}

private async Task ExecuteSelectedVmActionAsync(string action)
{
    if (SelectedVm is null)
    {
        StatusText = "Select a VM first.";
        return;
    }

    if (ActiveSession is null || !_ps.IsConnected)
    {
        StatusText = "Connect to an environment first.";
        return;
    }

    var vm = SelectedVm;
    _actionLogSvc.Write($"Requested {action} for VM {vm.Name} on host {vm.Host}.");
    StatusText = $"{action} requested for {vm.Name}...";

    try
    {
        StatusText = $"Submitting {action} command for {vm.Name} on {vm.Host}...";
        await _hvSvc.ExecuteVmPowerActionAsync(vm.Name, vm.Host, action);
        _actionLogSvc.Write($"Completed {action} for VM {vm.Name} on host {vm.Host}.");
        StatusText = $"{action} command submitted for {vm.Name}. Refreshing inventory to confirm state...";
        await CollectForSessionAsync(ActiveSession);
        var refreshedVm = VmList.FirstOrDefault(v => v.Name.Equals(vm.Name, StringComparison.OrdinalIgnoreCase));
        if (refreshedVm is not null)
        {
            StatusText = $"{action} command submitted for {vm.Name}. Current state: {refreshedVm.PowerState} on {refreshedVm.Host}.";
        }
    }
    catch (Exception ex)
    {
        _actionLogSvc.Write($"Failed {action} for VM {vm.Name} on host {vm.Host}: {ex.Message}");
        StatusText = $"{action} failed for {vm.Name}: {ex.Message}";
        throw;
    }
}

[RelayCommand]
public async Task MigrateSelectedVmAsync()
{
    if (SelectedVm is null)
    {
        StatusText = "Select a VM first.";
        return;
    }

    if (string.IsNullOrWhiteSpace(LiveMigrationTargetHost))
    {
        StatusText = "Select a destination host for live migration.";
        return;
    }

    if (SelectedVm.Host.Equals(LiveMigrationTargetHost, StringComparison.OrdinalIgnoreCase))
    {
        StatusText = "Destination host must be different from the current host.";
        return;
    }

    if (ActiveSession is null || !_ps.IsConnected)
    {
        StatusText = "Connect to an environment first.";
        return;
    }

    var vm = SelectedVm;
    _actionLogSvc.Write($"Requested Live Migrate for VM {vm.Name} from {vm.Host} to {LiveMigrationTargetHost}.");
    StatusText = $"Submitting live migration for {vm.Name} from {vm.Host} to {LiveMigrationTargetHost}...";

    try
    {
        var progress = new Progress<string>(message => StatusText = message);
        var migrationResult = await _hvSvc.ExecuteLiveMigrationAsync(vm.Name, vm.Host, LiveMigrationTargetHost, progress);
        _actionLogSvc.Write($"Migration tracking result for VM {vm.Name}: status={migrationResult.Status}, currentOwner={migrationResult.CurrentOwner}, details={migrationResult.Details}");
        StatusText = $"Migration {migrationResult.Status}: {vm.Name} → target {LiveMigrationTargetHost}, current owner {migrationResult.CurrentOwner}.";
        _actionLogSvc.Write($"Completed Live Migrate for VM {vm.Name} to {LiveMigrationTargetHost}.");
        StatusText = $"Live migration submitted for {vm.Name}. Waiting for inventory to settle…";

        VmInfo? refreshedVm = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(3000);
            await CollectForSessionAsync(ActiveSession);
            refreshedVm = VmList.FirstOrDefault(v => v.Name.Equals(vm.Name, StringComparison.OrdinalIgnoreCase));
            if (refreshedVm is not null && refreshedVm.Host.Equals(LiveMigrationTargetHost, StringComparison.OrdinalIgnoreCase))
                break;
        }

        if (refreshedVm is not null)
        {
            SelectedVm = refreshedVm;
            if (System.Windows.Application.Current is not null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Data.CollectionViewSource.GetDefaultView(VmList)?.Refresh();
                    OnPropertyChanged(nameof(VmList));
                });
            }
            else
            {
                OnPropertyChanged(nameof(VmList));
            }

            if (refreshedVm.Host.Equals(LiveMigrationTargetHost, StringComparison.OrdinalIgnoreCase))
            {
                StatusText = $"Live migration complete: {vm.Name} is now on {refreshedVm.Host}.";
            }
            else
            {
                StatusText = $"Migration {migrationResult.Status}: inventory shows {vm.Name} on {refreshedVm.Host}. {migrationResult.Details}";
            }
        }
        else
        {
            StatusText = $"Live migration submitted for {vm.Name}. VM not found in refreshed inventory.";
        }
    }
    catch (Exception ex)
    {
        _actionLogSvc.Write($"Failed Live Migrate for VM {vm.Name}: {ex.Message}");
        StatusText = $"Live migration failed for {vm.Name}: {ex.Message}";
        throw;
    }
}

partial void OnSelectedVmChanged(VmInfo? value)
{
    if (value is null)
        return;

    var hosts = HostList
        .Select(h => h.NodeName)
        .Where(n => !string.IsNullOrWhiteSpace(n))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(n => n)
        .Where(h => !h.Equals(value.Host, StringComparison.OrdinalIgnoreCase))
        .ToList();

    LiveMigrationHosts = new(hosts);
    if (hosts.Count > 0)
    {
        if (string.IsNullOrWhiteSpace(LiveMigrationTargetHost) ||
            LiveMigrationTargetHost.Equals(value.Host, StringComparison.OrdinalIgnoreCase) ||
            !hosts.Contains(LiveMigrationTargetHost, StringComparer.OrdinalIgnoreCase))
        {
            LiveMigrationTargetHost = hosts[0];
        }
    }
    else
    {
        LiveMigrationTargetHost = string.Empty;
    }
}

    // ─────────────────────────────────────────────────────────────────────────
    //  Export
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public async Task ExportAllXlsxAsync(string filePath)
    {
        if (_lastInventory is null) { StatusText = "No data to export."; return; }
        StatusText = "Exporting…";
        await _exportSvc.ExportAllAsync(_lastInventory, filePath, _lastUpdates, _lastPerf);
        StatusText = $"Exported → {filePath}";
    }

    [RelayCommand]
    public async Task ExportCurrentTabXlsxAsync(string filePath)
    {
        if (_lastInventory is null) { StatusText = "No data."; return; }
        await _exportSvc.ExportCurrentTabAsync(_lastInventory, SelectedTab, filePath,
            asCsv: false, _lastUpdates, _lastPerf);
        StatusText = $"Tab '{SelectedTab}' exported → {filePath}";
    }

    [RelayCommand]
    public async Task ExportCurrentTabCsvAsync(string filePath)
    {
        if (_lastInventory is null) { StatusText = "No data."; return; }
        await _exportSvc.ExportCurrentTabAsync(_lastInventory, SelectedTab, filePath,
            asCsv: true, _lastUpdates, _lastPerf);
        StatusText = $"Tab '{SelectedTab}' exported as CSV → {filePath}";
    }

    [RelayCommand]
    public async Task ExportAllCsvAsync(string folderPath)
    {
        if (_lastInventory is null) { StatusText = "No data."; return; }
        await _exportSvc.ExportAllAsCsvAsync(_lastInventory, folderPath, _lastUpdates, _lastPerf);
        StatusText = $"All tabs exported as CSV → {folderPath}";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Filter
    // ─────────────────────────────────────────────────────────────────────────
    [RelayCommand]
    public void ApplyFilter(string query)
    {
        if (_lastInventory is null) return;
        if (string.IsNullOrWhiteSpace(query)) { PopulateCollections(_lastInventory, _lastUpdates, _lastPerf); return; }
        var q = query;

        switch (SelectedTab)
        {
            case "vmInfo":
                VmList = new ObservableCollection<VmInfo>(
                    _lastInventory.VMs.Where(v =>
                        v.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        v.PowerState.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        v.Host.Contains(q, StringComparison.OrdinalIgnoreCase)));
                break;
            case "hvHealth":
                HealthList = new ObservableCollection<HealthCheck>(
                    _lastInventory.HealthChecks.Where(h =>
                        h.Message.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        h.ObjectName.Contains(q, StringComparison.OrdinalIgnoreCase)));
                break;
            case "hvUpdate":
                UpdateList = new ObservableCollection<NodeUpdateStatus>(
                    _lastUpdates.Where(u => u.NodeName.Contains(q, StringComparison.OrdinalIgnoreCase)));
                break;
            case "hvPerf":
                PerfList = new ObservableCollection<PerfSummaryRow>(
                    _lastPerf.Where(p =>
                        p.ObjectName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        p.MetricName.Contains(q, StringComparison.OrdinalIgnoreCase)));
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private void PopulateCollections(InventoryResult inv,
        IReadOnlyList<NodeUpdateStatus> updates,
        IReadOnlyList<PerfSummaryRow>   perf)
    {
        VmList       = new(inv.VMs);
        DiskList     = new(inv.VmDisks);
        NicList      = new(inv.VmNics);
        SnapList     = new(inv.VmSnapshots);
        HostList     = new(inv.Hosts);
        ClusterList  = new(inv.Clusters);
        StorageList  = new(inv.Volumes);
        SwitchList   = new(inv.Switches);
        PhysNicList  = new(inv.PhysicalNics);
        HealthList   = new(inv.HealthChecks);
        ArcList      = new(inv.ArcResources);
        S2dList      = new(inv.S2DPools);
        UpdateList   = new(updates);
        PerfList     = new(perf);

        TotalVMs          = inv.VMs.Count;
        RunningVMs        = inv.VMs.Count(v => v.PowerState == "Running");
        OffVMs            = inv.VMs.Count(v => v.PowerState is "Off" or "Saved");
        HostCount         = inv.Hosts.Count;
        SnapshotCount     = inv.VmSnapshots.Count;
        HealthAlertCount  = inv.HealthChecks.Count(h => h.Severity is HealthSeverity.Error or HealthSeverity.Warning);
        PendingUpdateCount = updates.Count(u => u.PendingCount > 0);
        OverallHealthScore = ExtractOverallHealthScore(inv.HealthChecks);
        RecommendationCount = inv.HealthChecks.Count(h => h.Category == "Recommendation");
        ImbalanceCount = inv.HealthChecks.Count(h => h.Category == "Imbalance");
        AlertInsights = new(inv.AlertInsights ?? []);
        OpenAlertCount = AlertInsights.Count(a => a.Status == AlertStatus.Open);
        CriticalAlertCount = AlertInsights.Count(a => a.Priority == AlertPriority.Critical || a.Priority == AlertPriority.High);
        DriftChanges = new(inv.DriftChanges ?? System.Array.Empty<DriftChange>());
        DriftChangeCount = DriftChanges.Count;
        PlacementRecommendationCount = inv.HealthChecks.Count(h => h.Category == "Placement");
        ForecastFindingCount = inv.HealthChecks.Count(h => h.Category == "Forecast");
        ComplianceFindingCount = inv.HealthChecks.Count(h => h.Category == "Compliance");
        var hostNames = inv.Hosts.Select(h => h.NodeName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
        LiveMigrationHosts = new(hostNames);
        if (SelectedVm is not null)
        {
            OnSelectedVmChanged(SelectedVm);
        }
    }

    private void ClearCollections()
    {
        VmList.Clear(); DiskList.Clear(); NicList.Clear(); SnapList.Clear();
        HostList.Clear(); ClusterList.Clear(); StorageList.Clear(); SwitchList.Clear();
        PhysNicList.Clear(); HealthList.Clear(); ArcList.Clear(); S2dList.Clear();
        UpdateList.Clear(); PerfList.Clear();
        TotalVMs = RunningVMs = OffVMs = HostCount = SnapshotCount =
            HealthAlertCount = PendingUpdateCount = 0;
    }

private static IReadOnlyList<PerfSummaryRow> MergePerfRows(
    IReadOnlyList<PerfSummaryRow> existing,
    IReadOnlyList<PerfSummaryRow> incoming)
{
    var map = existing.ToDictionary(
        r => $"{r.ObjectType}|{r.ObjectName}|{r.MetricName}",
        r => r);

    foreach (var row in incoming)
    {
        var key = $"{row.ObjectType}|{row.ObjectName}|{row.MetricName}";
        if (!map.TryGetValue(key, out var prior) || prior.Series is null)
        {
            map[key] = row;
            continue;
        }

        var samples = prior.Series.Samples.ToList();
        if (row.Series is not null && row.Series.Samples.Count > 0)
        {
            foreach (var s in row.Series.Samples.OrderBy(s => s.Timestamp))
            {
                bool duplicate = samples.Any(x =>
                    x.Timestamp == s.Timestamp &&
                    x.Value == s.Value &&
                    x.ObjectName == s.ObjectName &&
                    x.MetricName == s.MetricName);

                if (!duplicate)
                    samples.Add(s);
            }
        }

        samples = samples.OrderBy(s => s.Timestamp).TakeLast(240).ToList();
        var current = samples.Last().Value;
        var peak = samples.Max(s => s.Value);
        var avg = samples.Average(s => s.Value);
        var trend = current > avg * 1.1 ? "↑" : current < avg * 0.9 ? "↓" : "→";

        map[key] = new PerfSummaryRow
        {
            ObjectName = row.ObjectName,
            ObjectType = row.ObjectType,
            MetricName = row.MetricName,
            CurrentValue = current,
            PeakValue = peak,
            AvgValue = Math.Round(avg, 1),
            Unit = row.Unit,
            Trend = trend,
            Series = new PerfSeries
            {
                SeriesName = row.Series?.SeriesName ?? $"{row.ObjectName} — {row.MetricName}",
                Unit = row.Unit,
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


private static int ExtractOverallHealthScore(IReadOnlyList<HealthCheck> checks)
{
    var overall = checks.FirstOrDefault(h => h.Category == "Score" && h.Message.StartsWith("Overall environment health score", StringComparison.OrdinalIgnoreCase));
    if (overall is null) return 0;

    var match = System.Text.RegularExpressions.Regex.Match(overall.Message, @"(\d+)/100");
    return match.Success && int.TryParse(match.Groups[1].Value, out var score) ? score : 0;
}

private ConnectionSettings BuildConnectionSettings() => new()
    {
        HostOrCluster       = TargetHost,
        UseCurrentUser      = UseCurrentUser,
        Username            = Username,
        ConnectAzurePlane   = ConnectAzure,
        AzureSubscriptionId = AzureSubscription,
        AzureResourceGroup  = AzureResourceGroup,
        AzureTenantId       = AzureTenantId,
    };

    private void RefreshSavedProfiles()
    {
        SavedProfiles = new(System.Linq.Enumerable.OrderBy(SavedEnvironmentProfileStore.Load(), p => p.ProfileName));
    }

}
