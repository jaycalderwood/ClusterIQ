
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace HVTools.Services;

public sealed class AppUpdateService
{
    private const string RepoOwner = "jaycalderwood";
    private const string RepoName = "ClusterIQ";
    private const string LatestReleaseApi = "https://api.github.com/repos/jaycalderwood/ClusterIQ/releases/latest";

    public sealed class AppUpdateInfo
    {
        public bool UpdateAvailable { get; init; }
        public string CurrentVersion { get; init; } = string.Empty;
        public string LatestVersion { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;
        public string ReleaseNotes { get; init; } = string.Empty;
        public string PublishedAt { get; init; } = string.Empty;
        public string AssetName { get; init; } = string.Empty;
        public string Error { get; init; } = string.Empty;
    }

    public string GetCurrentVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    }

    public async Task<AppUpdateInfo> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        var currentVersion = GetCurrentVersion();

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ClusterIQ");
        client.Timeout = TimeSpan.FromSeconds(20);

        try
        {
            var json = await client.GetStringAsync(LatestReleaseApi, ct);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var latestTag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? string.Empty : string.Empty;
            var latestVersion = NormalizeVersion(latestTag);
            var releaseNotes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;
            var publishedAt = root.TryGetProperty("published_at", out var pubEl) ? pubEl.GetString() ?? string.Empty : string.Empty;

            string assetName = string.Empty;
            string downloadUrl = string.Empty;

            if (root.TryGetProperty("assets", out var assetsEl))
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                    var url = asset.TryGetProperty("browser_download_url", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;

                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    if (name.Contains("ClusterIQ", StringComparison.OrdinalIgnoreCase) &&
                        (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        assetName = name;
                        downloadUrl = url;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(downloadUrl) &&
                        (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        assetName = name;
                        downloadUrl = url;
                    }
                }
            }

            var updateAvailable = CompareVersions(latestVersion, currentVersion) > 0 && !string.IsNullOrWhiteSpace(downloadUrl);

            return new AppUpdateInfo
            {
                UpdateAvailable = updateAvailable,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseNotes,
                PublishedAt = publishedAt,
                AssetName = assetName
            };
        }
        catch (Exception ex)
        {
            return new AppUpdateInfo
            {
                CurrentVersion = currentVersion,
                Error = ex.Message
            };
        }
    }

    public async Task DownloadAndInstallUpdateAsync(AppUpdateInfo info, CancellationToken ct = default)
    {
        if (!info.UpdateAvailable || string.IsNullOrWhiteSpace(info.DownloadUrl))
            throw new InvalidOperationException("No update is available.");

        var appExe = Process.GetCurrentProcess().MainModule?.FileName
                     ?? throw new InvalidOperationException("Could not determine current executable path.");
        var appDir = Path.GetDirectoryName(appExe)
                     ?? throw new InvalidOperationException("Could not determine application directory.");

        var tempRoot = Path.Combine(Path.GetTempPath(), "ClusterIQ_Update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        var assetName = string.IsNullOrWhiteSpace(info.AssetName) ? "ClusterIQ_Update.zip" : info.AssetName;
        var packagePath = Path.Combine(tempRoot, assetName);

        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ClusterIQ");
            using var response = await client.GetAsync(info.DownloadUrl, ct);
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(packagePath);
            await response.Content.CopyToAsync(fs, ct);
        }

        var extractionRoot = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractionRoot);

        if (packagePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(packagePath, extractionRoot, true);
        }
        else if (packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(packagePath, Path.Combine(extractionRoot, Path.GetFileName(appExe)), true);
        }

        var updaterScript = Path.Combine(tempRoot, "apply_update.ps1");
        var script = $@"
$ErrorActionPreference = 'Stop'
$targetDir = '{EscapeForPs(appDir)}'
$targetExe = '{EscapeForPs(appExe)}'
$extractRoot = '{EscapeForPs(extractionRoot)}'

Start-Sleep -Seconds 3

$rootChildren = Get-ChildItem -LiteralPath $extractRoot -Force
if ($rootChildren.Count -eq 1 -and $rootChildren[0].PSIsContainer) {{
    $sourceDir = $rootChildren[0].FullName
}} else {{
    $sourceDir = $extractRoot
}}

Copy-Item -Path (Join-Path $sourceDir '*') -Destination $targetDir -Recurse -Force

Start-Process -FilePath $targetExe
";
        await File.WriteAllTextAsync(updaterScript, script, ct);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{updaterScript}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });

        System.Windows.Application.Current.Shutdown();
    }

    private static string NormalizeVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "0.0.0";

        return value.Trim().TrimStart('v', 'V');
    }

    private static int CompareVersions(string left, string right)
    {
        if (Version.TryParse(NormalizeVersion(left), out var l) &&
            Version.TryParse(NormalizeVersion(right), out var r))
        {
            return l.CompareTo(r);
        }

        return string.Compare(NormalizeVersion(left), NormalizeVersion(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeForPs(string value)
    {
        return value.Replace("'", "''");
    }
}
