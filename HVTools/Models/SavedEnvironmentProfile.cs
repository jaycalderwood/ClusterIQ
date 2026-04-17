namespace HVTools.Models;

public sealed class SavedEnvironmentProfile
{
    public string ProfileName { get; set; } = string.Empty;
    public string TargetHost { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool UseCurrentUser { get; set; } = true;
    public bool ConnectAzure { get; set; }
    public string AzureSubscription { get; set; } = string.Empty;
    public string AzureResourceGroup { get; set; } = string.Empty;
    public string AzureTenantId { get; set; } = string.Empty;
    public bool IsFavorite { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(ProfileName) ? TargetHost : ProfileName;
}
