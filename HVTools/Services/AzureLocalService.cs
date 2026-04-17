using System.Net.Http;
// =============================================================================
//  AzureLocalService
//  Queries the Azure plane of an Azure Local (Azure Stack HCI) cluster using
//  the Azure SDK and the Azure Stack HCI REST API.
//
//  Authentication:  Azure.Identity — supports:
//    • Interactive browser login (DefaultAzureCredential)
//    • Service principal (ClientSecretCredential)
//    • Device-code flow for headless scenarios
//
//  Key APIs used:
//    • Azure Stack HCI RP  (Microsoft.AzureStackHCI)  — cluster / node status
//    • Azure Arc (Microsoft.HybridCompute)             — machine inventory
//    • Azure Resource Manager                          — extensions, tags
// =============================================================================

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using HVTools.Models;
// Note: Azure.ResourceManager.HybridCompute SDK not used — Arc data is
// queried via raw REST (HttpClient) to avoid pre-release package dependency.
using Microsoft.Extensions.Logging;

namespace HVTools.Services;

public sealed class AzureLocalService
{
    private readonly ILogger<AzureLocalService> _logger;
    private TokenCredential? _credential;
    private ArmClient? _armClient;
    private readonly HttpClient _http;

    // Management API root
    private const string ArmBase = "https://management.azure.com";
    private const string ApiVersionHci = "2024-01-01";
    private const string ApiVersionArc = "2022-11-10";

    public AzureLocalService(ILogger<AzureLocalService> logger)
    {
        _logger = logger;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Authenticate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Authenticate with Azure using the interactive browser credential.
    /// For service principal auth, pass clientId + clientSecret + tenantId.
    /// </summary>
    public void AuthenticateInteractive(string? tenantId = null)
    {
        _credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            TenantId                   = tenantId,
            ExcludeEnvironmentCredential = false,
            ExcludeManagedIdentityCredential = true,
        });

        _armClient = new ArmClient(_credential);
        _logger.LogInformation("Azure authentication configured (DefaultAzureCredential)");
    }

    public void AuthenticateServicePrincipal(string tenantId, string clientId, string clientSecret)
    {
        _credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        _armClient  = new ArmClient(_credential);
        _logger.LogInformation("Azure authentication configured (service principal {ClientId})", clientId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Get bearer token for raw REST calls
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_credential is null)
            throw new InvalidOperationException("Call AuthenticateInteractive() first.");

        var ctx = new TokenRequestContext([$"{ArmBase}/.default"]);
        var token = await _credential.GetTokenAsync(ctx, ct);
        return token.Token;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  List Arc-enabled machines (nodes + Arc VMs)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<IReadOnlyList<AzureArcResource>> GetArcResourcesAsync(
        string subscriptionId,
        string? resourceGroup = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Fetching Arc resources from subscription {Sub} {Scope}",
            subscriptionId,
            string.IsNullOrWhiteSpace(resourceGroup) ? "(all resource groups)" : $"resource group {resourceGroup}");

        var token  = await GetTokenAsync(ct);
        var list   = new List<AzureArcResource>();

        // ── 1. Arc-enabled servers (nodes) ─────────────────────────────────
        var serverUrl = string.IsNullOrWhiteSpace(resourceGroup)
            ? $"{ArmBase}/subscriptions/{subscriptionId}/providers/Microsoft.HybridCompute/machines?api-version={ApiVersionArc}"
            : $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.HybridCompute/machines?api-version={ApiVersionArc}";

        var serverJson = await GetJsonAsync(serverUrl, token, ct);
        if (serverJson?.GetProperty("value") is JsonElement serverArr)
        {
            foreach (var m in serverArr.EnumerateArray())
            {
                var props  = m.GetProperty("properties");
                var exts   = await GetExtensionNamesAsync(
                    subscriptionId, resourceGroup,
                    m.GetProperty("name").GetString() ?? "", token, ct);

                list.Add(new AzureArcResource
                {
                    ResourceName     = m.GetProperty("name").GetString() ?? "",
                    ResourceType     = "Arc-enabled Server",
                    ArcStatus        = GetArcStatus(props),
                    SubscriptionId   = subscriptionId,
                    ResourceGroup    = resourceGroup,
                    Region           = m.TryGetProperty("location", out var loc)
                                       ? loc.GetString() ?? "" : "",
                    ArcAgentVersion  = props.TryGetProperty("agentVersion", out var av)
                                       ? av.GetString() ?? "" : "",
                    Extensions       = string.Join(", ", exts),
                    LastSyncTime     = GetArcLastSync(props),
                    Tags             = FlattenTags(m),
                    ArcResourceId    = m.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                });
            }
        }

        // ── 2. Azure Local cluster registration ────────────────────────────
        var hciUrl = string.IsNullOrWhiteSpace(resourceGroup)
            ? $"{ArmBase}/subscriptions/{subscriptionId}/providers/Microsoft.AzureStackHCI/clusters?api-version={ApiVersionHci}"
            : $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}/providers/Microsoft.AzureStackHCI/clusters?api-version={ApiVersionHci}";

        var hciJson = await GetJsonAsync(hciUrl, token, ct);
        if (hciJson?.GetProperty("value") is JsonElement hciArr)
        {
            foreach (var c in hciArr.EnumerateArray())
            {
                var props = c.GetProperty("properties");
                list.Add(new AzureArcResource
                {
                    ResourceName   = c.GetProperty("name").GetString() ?? "",
                    ResourceType   = "Azure Local Cluster",
                    ArcStatus      = props.TryGetProperty("connectivityStatus", out var cs)
                                     ? cs.GetString() ?? "Unknown" : "Unknown",
                    SubscriptionId = subscriptionId,
                    ResourceGroup  = resourceGroup,
                    Region         = c.TryGetProperty("location", out var loc)
                                     ? loc.GetString() ?? "" : "",
                    ArcResourceId  = c.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    Tags           = FlattenTags(c),
                });
            }
        }

        _logger.LogInformation("Found {Count} Arc resources", list.Count);
        return list;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Get extension names for a given Arc machine
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<IEnumerable<string>> GetExtensionNamesAsync(
        string sub, string rg, string machineName,
        string token, CancellationToken ct)
    {
        var url = $"{ArmBase}/subscriptions/{sub}/resourceGroups/{rg}" +
                  $"/providers/Microsoft.HybridCompute/machines/{machineName}" +
                  $"/extensions?api-version={ApiVersionArc}";

        var json = await GetJsonAsync(url, token, ct);
        if (json?.GetProperty("value") is not JsonElement arr) return [];

        return arr.EnumerateArray()
                  .Select(e => e.GetProperty("name").GetString() ?? "")
                  .Where(n => !string.IsNullOrEmpty(n));
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Get Azure Local HCI cluster nodes from Azure resource plane
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<JsonElement?> GetHciClusterDetailsAsync(
        string subscriptionId, string resourceGroup, string clusterName,
        CancellationToken ct = default)
    {
        var token = await GetTokenAsync(ct);
        var url   = $"{ArmBase}/subscriptions/{subscriptionId}/resourceGroups/{resourceGroup}" +
                    $"/providers/Microsoft.AzureStackHCI/clusters/{clusterName}" +
                    $"?api-version={ApiVersionHci}";

        return await GetJsonAsync(url, token, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────────────────
    private async Task<JsonElement?> GetJsonAsync(string url, string token, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Azure REST call failed [{Status}]: {Url}", (int)resp.StatusCode, url);
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Azure REST API: {Url}", url);
            return null;
        }
    }


    private static string GetArcStatus(JsonElement props)
    {
        foreach (var key in new[] { "status", "machineStatus", "connectionStatus", "connectivityStatus", "agentStatus" })
        {
            if (props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                var s = v.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    return s!;
            }
        }

        return "Unknown";
    }

    private static DateTime? GetArcLastSync(JsonElement props)
    {
        foreach (var key in new[] { "lastStatusChange", "lastStatusUpdate", "lastConnectedTime", "lastHeartbeat" })
        {
            if (props.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(v.GetString(), out var dt))
                    return dt;
            }
        }

        return null;
    }

    private static string FlattenTags(JsonElement resource)
    {
        if (!resource.TryGetProperty("tags", out var tags)) return string.Empty;
        if (tags.ValueKind != JsonValueKind.Object) return string.Empty;

        return string.Join(", ", tags.EnumerateObject()
                                      .Select(p => $"{p.Name}={p.Value.GetString()}"));
    }
}
