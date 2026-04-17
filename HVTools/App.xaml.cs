// =============================================================================
//  App.xaml.cs  (v1.0)
// =============================================================================

using System.Linq;
using System.Windows;
using HVTools.Services;
using HVTools.ViewModels;
using Microsoft.Extensions.Logging;

namespace HVTools;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
base.OnStartup(e);

DispatcherUnhandledException += (_, ex) =>
{
    System.Windows.MessageBox.Show(
        ex.Exception.ToString(),
        "ClusterIQ Startup Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
    ex.Handled = true;
    Shutdown(-1);
};

AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
{
    var message = ex.ExceptionObject?.ToString() ?? "Unknown startup error";
    System.Windows.MessageBox.Show(
        message,
        "ClusterIQ Startup Error",
        MessageBoxButton.OK,
        MessageBoxImage.Error);
};

        var prefs = UserPreferencesStore.Load();
        HVTools.ThemeManager.Apply(prefs.IsDarkMode ? "DarkTheme" : "LightTheme");

        var logFactory = LoggerFactory.Create(b => b
            .AddDebug()
            .SetMinimumLevel(LogLevel.Information));

        var psRunner   = new PowerShellRunner(logFactory.CreateLogger<PowerShellRunner>());
        var hvSvc      = new HyperVService(psRunner, logFactory.CreateLogger<HyperVService>());
        var azSvc      = new AzureLocalService(logFactory.CreateLogger<AzureLocalService>());
        var healthSvc  = new HealthCheckService();
        var exportSvc  = new ExportService();
        var perfSvc    = new PerfHistoryService(psRunner, logFactory.CreateLogger<PerfHistoryService>());
        var updateSvc  = new UpdateService(psRunner, logFactory.CreateLogger<UpdateService>());

        var vm = new MainViewModel(
            psRunner, hvSvc, azSvc, healthSvc, exportSvc, perfSvc, updateSvc,
            logFactory.CreateLogger<MainViewModel>());
        vm.ApplyUserPreferences(prefs);

        var window = new MainWindow(vm);
            MainWindow = window;
            window.Show();

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                var updater = new AppUpdateService();
                var info = await updater.CheckForUpdatesAsync();
                if (info.UpdateAvailable)
                {
                    Dispatcher.Invoke(() =>
                    {
                        vm.StatusText = $"Update available: {info.LatestVersion} — open About to review and install.";
                    });
                }
            }
            catch
            {
            }
        });

        // CLI mode: only run headless when explicitly requested
var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
var runCli = args.Any(a =>
    string.Equals(a, "--cli", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(a, "/cli", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase));

if (runCli)
{
    Dispatcher.BeginInvoke(async () =>
    {
        int code = await CliRunner.RunAsync(args);
        Shutdown(code);
    });
}
    }
}
