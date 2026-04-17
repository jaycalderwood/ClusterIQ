namespace HVTools.Models;

public enum AlertPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum AlertStatus
{
    Open,
    Acknowledged,
    Resolved
}

public sealed class AlertThresholdSettings
{
    public double HostCpuWarningPercent { get; set; } = 75;
    public double HostCpuCriticalPercent { get; set; } = 90;
    public int MemoryImbalanceWarningSpread { get; set; } = 20;
    public int MemoryImbalanceCriticalSpread { get; set; } = 30;
    public int VmSpreadWarning { get; set; } = 3;
    public int VmSpreadCritical { get; set; } = 5;
    public int ArcDisconnectWarningCycles { get; set; } = 1;
}

public sealed class AlertInsight
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ObjectName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public HealthSeverity Severity { get; set; }
    public AlertPriority Priority { get; set; } = AlertPriority.Medium;
    public AlertStatus Status { get; set; } = AlertStatus.Open;
    public string TriggerReason { get; set; } = string.Empty;
    public string Impact { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public string RecommendationCategory { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; } = DateTime.Now;
}
