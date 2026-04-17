
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using HVTools.Services;

namespace HVTools.Views;

public partial class AboutWindow : Window, INotifyPropertyChanged
{
    private readonly AppUpdateService _updateService = new();
    private AppUpdateService.AppUpdateInfo? _latestInfo;

    private string _versionText = string.Empty;
    private string _updateStatusText = "Ready to check for updates.";
    private string _releaseMetaText = string.Empty;
    private string _releaseNotesText = "No release information loaded.";

    public string VersionText { get => _versionText; set { _versionText = value; OnPropertyChanged(); } }
    public string UpdateStatusText { get => _updateStatusText; set { _updateStatusText = value; OnPropertyChanged(); } }
    public string ReleaseMetaText { get => _releaseMetaText; set { _releaseMetaText = value; OnPropertyChanged(); } }
    public string ReleaseNotesText { get => _releaseNotesText; set { _releaseNotesText = value; OnPropertyChanged(); } }

    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        VersionText = $"Current version: {_updateService.GetCurrentVersion()}";
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatusText = "Checking GitHub for the latest release...";
        ReleaseMetaText = string.Empty;
        ReleaseNotesText = "Loading release information...";

        var info = await _updateService.CheckForUpdatesAsync();
        _latestInfo = info;

        if (!string.IsNullOrWhiteSpace(info.Error))
        {
            UpdateStatusText = $"Update check failed: {info.Error}";
            ReleaseNotesText = "Unable to load release notes.";
            return;
        }

        ReleaseMetaText = $"Latest version: {info.LatestVersion}    Published: {info.PublishedAt}";
        ReleaseNotesText = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "No release notes were published." : info.ReleaseNotes;

        if (info.UpdateAvailable)
            UpdateStatusText = $"Update available: {info.LatestVersion}";
        else
            UpdateStatusText = "You are already on the latest version.";
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_latestInfo is null)
        {
            await RunUpdateCheckIfNeeded();
        }

        if (_latestInfo is null || !_latestInfo.UpdateAvailable)
        {
            System.Windows.MessageBox.Show(this, "No update is currently available.", "ClusterIQ Update", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"Download and apply version {_latestInfo.LatestVersion} now? The application will restart after the update is copied in place.",
            "ClusterIQ Update",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
            return;

        try
        {
            await _updateService.DownloadAndInstallUpdateAsync(_latestInfo);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(this, $"Update failed: {ex.Message}", "ClusterIQ Update", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task RunUpdateCheckIfNeeded()
    {
        var info = await _updateService.CheckForUpdatesAsync();
        _latestInfo = info;

        if (string.IsNullOrWhiteSpace(info.Error))
        {
            ReleaseMetaText = $"Latest version: {info.LatestVersion}    Published: {info.PublishedAt}";
            ReleaseNotesText = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? "No release notes were published." : info.ReleaseNotes;
            UpdateStatusText = info.UpdateAvailable ? $"Update available: {info.LatestVersion}" : "You are already on the latest version.";
        }
        else
        {
            UpdateStatusText = $"Update check failed: {info.Error}";
            ReleaseNotesText = "Unable to load release notes.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
