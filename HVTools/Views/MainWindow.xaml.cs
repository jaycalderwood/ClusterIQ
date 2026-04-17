using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HVTools.Models;
using HVTools.ViewModels;
using HVTools.Views;
using Microsoft.Win32;

namespace HVTools;

public partial class MainWindow : Window
{
    private bool _isLoaded;
    private readonly MainViewModel _vm;
    private DispatcherTimer? _perfRefreshTimer;
    private bool _perfRefreshInProgress;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;

        InitializeComponent();

        SizeChanged += (_, _) => RedrawChart(_vm.SelectedPerfRow);

        _perfRefreshTimer = new System.Windows.Threading.DispatcherTimer();
        _perfRefreshTimer.Tick += PerfRefreshTimer_Tick;

        Loaded += (_, _) =>
        {
            _isLoaded = true;
            ApplyPerfRefreshInterval(_vm.PerfRefreshSeconds);
            RedrawChart(_vm.SelectedPerfRow);
        };
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        var pwd = PasswordBox.SecurePassword;
        await _vm.ConnectAsync(pwd.Length > 0 ? pwd : null);
    }

    private async void ExportAllXlsx_Click(object sender, RoutedEventArgs e)
    {
        var path = PickSaveFile(
            "Excel Workbook (*.xlsx)|*.xlsx",
            $"ClusterIQ_Export_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        if (path is not null)
            await _vm.ExportAllXlsxAsync(path);
    }

    private async void ExportTabXlsx_Click(object sender, RoutedEventArgs e)
    {
        var path = PickSaveFile(
            "Excel Workbook (*.xlsx)|*.xlsx",
            $"ClusterIQ_{_vm.SelectedTab}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");

        if (path is not null)
            await _vm.ExportCurrentTabXlsxAsync(path);
    }

    private async void ExportTabCsv_Click(object sender, RoutedEventArgs e)
    {
        var path = PickSaveFile(
            "CSV file (*.csv)|*.csv",
            $"ClusterIQ_{_vm.SelectedTab}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        if (path is not null)
            await _vm.ExportCurrentTabCsvAsync(path);
    }

    private async void ExportAllCsv_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder for CSV export",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            await _vm.ExportAllCsvAsync(dlg.SelectedPath);
    }

    private static string? PickSaveFile(string filter, string defaultName)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = filter,
            FileName = defaultName
        };

        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow
        {
            Owner = this
        };

        helpWindow.ShowDialog();
    }


private void SettingsButton_Click(object sender, RoutedEventArgs e)
{
    var win = new SettingsWindow(_vm)
    {
        Owner = this
    };
    win.ShowDialog();
}


private bool ConfirmVmAction(string action, string vmName)
{
    var result = System.Windows.MessageBox.Show(
        this,
        $"Are you sure you want to {action.ToLowerInvariant()} VM '{vmName}'?",
        "ClusterIQ Confirmation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    return result == MessageBoxResult.Yes;
}

private bool ConfirmLiveMigrate(string vmName, string destinationHost)
{
    var result = System.Windows.MessageBox.Show(
        this,
        $"Are you sure you want to live migrate VM '{vmName}' to host '{destinationHost}'?",
        "ClusterIQ Confirmation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question);

    return result == MessageBoxResult.Yes;
}

private async void LiveMigrateVmButton_Click(object sender, RoutedEventArgs e)
{
    if (_vm.SelectedVm is null) return;
    if (string.IsNullOrWhiteSpace(_vm.LiveMigrationTargetHost))
    {
        System.Windows.MessageBox.Show(
            this,
            "Enter a destination host before running live migration.",
            "ClusterIQ",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return;
    }

    if (!ConfirmLiveMigrate(_vm.SelectedVm.Name, _vm.LiveMigrationTargetHost)) return;
    await _vm.MigrateSelectedVmAsync();
}



private void AboutButton_Click(object sender, RoutedEventArgs e)
{
    var aboutWindow = new AboutWindow
    {
        Owner = this
    };

    aboutWindow.ShowDialog();
}
private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (sender is System.Windows.Controls.TabControl tc && tc.SelectedItem is System.Windows.Controls.TabItem ti)
    {
        var header = ti.Header?.ToString();
        if (!string.IsNullOrWhiteSpace(header))
            _vm.SelectedTab = header;
    }
}
private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) { }
private async void StartVmButton_Click(object sender, RoutedEventArgs e)
{
    if (_vm?.SelectedVm is null) return;
    if (!ConfirmVmAction("Start", _vm.SelectedVm.Name)) return;
    _vm.StatusText = $"Submitting start for {_vm.SelectedVm.Name}...";
    await _vm.StartSelectedVmAsync();
}
private async void StopVmButton_Click(object sender, RoutedEventArgs e)
{
    if (_vm?.SelectedVm is null) return;
    if (!ConfirmVmAction("Stop", _vm.SelectedVm.Name)) return;
    _vm.StatusText = $"Submitting stop for {_vm.SelectedVm.Name}...";
    await _vm.StopSelectedVmAsync();
}
private async void RestartVmButton_Click(object sender, RoutedEventArgs e)
{
    if (_vm?.SelectedVm is null) return;
    if (!ConfirmVmAction("Restart", _vm.SelectedVm.Name)) return;
    _vm.StatusText = $"Submitting restart for {_vm.SelectedVm.Name}...";
    await _vm.RestartSelectedVmAsync();
}
private void SessionItem_Click(object sender, RoutedEventArgs e) { }
private void RemoveSession_Click(object sender, RoutedEventArgs e) { }
private void PerfRefreshIntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (!_isLoaded || _vm is null)
        return;

    if (sender is not System.Windows.Controls.ComboBox combo ||
        combo.SelectedItem is not System.Windows.Controls.ComboBoxItem item)
        return;

    var content = item.Content?.ToString() ?? "Off";
    int seconds = content switch
    {
        "5 sec" => 5,
        "10 sec" => 10,
        "15 sec" => 15,
        "30 sec" => 30,
        "60 sec" => 60,
        _ => 0
    };

    _vm.PerfRefreshSeconds = seconds;
    ApplyPerfRefreshInterval(seconds);
}

private void PerfGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (sender is DataGrid grid && grid.SelectedItem is PerfSummaryRow row)
    {
        _vm.SelectedPerfRow = row;
        RedrawChart(row);
    }
    else
    {
        RedrawChart(_vm.SelectedPerfRow);
    }
}
private void RedrawChart(object? selected = null)
{
    if (PerfChartCanvas is null || PerfChartLabel is null)
        return;

    var row = selected as PerfSummaryRow ?? _vm.SelectedPerfRow;
    PerfChartCanvas.Children.Clear();

    var label = new TextBlock
    {
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        Foreground = (System.Windows.Media.Brush)FindResource("MutedForegroundBrush"),
        Text = "Select a row above to view time-series chart"
    };
    Canvas.SetLeft(label, 10);
    Canvas.SetTop(label, 8);

    if (row?.Series?.Samples is null || row.Series.Samples.Count == 0)
    {
        PerfChartCanvas.Children.Add(label);
        return;
    }

    var samples = row.Series.Samples
        .OrderBy(s => s.Timestamp)
        .ToList();

    label.Text = $"{row.ObjectName} — {row.MetricName}   Current: {row.CurrentValue:F1} {row.Unit}   Peak: {row.PeakValue:F1} {row.Unit}";
    PerfChartCanvas.Children.Add(label);

    var width = PerfChartCanvas.ActualWidth;
    var height = PerfChartCanvas.ActualHeight;

    if (width < 100 || height < 80)
        return;

    const double leftPad = 16;
    const double rightPad = 16;
    const double topPad = 34;
    const double bottomPad = 16;

    var plotWidth = Math.Max(10, width - leftPad - rightPad);
    var plotHeight = Math.Max(10, height - topPad - bottomPad);

    var min = samples.Min(s => s.Value);
    var max = samples.Max(s => s.Value);
    if (Math.Abs(max - min) < 0.001)
    {
        max += 1;
        min -= 1;
    }

    var axisBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
    var lineBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");

    var baseline = new Line
    {
        X1 = leftPad,
        Y1 = topPad + plotHeight,
        X2 = leftPad + plotWidth,
        Y2 = topPad + plotHeight,
        Stroke = axisBrush,
        StrokeThickness = 1
    };
    PerfChartCanvas.Children.Add(baseline);

    var polyline = new Polyline
    {
        Stroke = lineBrush,
        StrokeThickness = 2.0
    };

    for (int i = 0; i < samples.Count; i++)
    {
        double x = leftPad + (plotWidth * i / Math.Max(1, samples.Count - 1));
        double normalized = (samples[i].Value - min) / (max - min);
        double y = topPad + plotHeight - (normalized * plotHeight);
        polyline.Points.Add(new System.Windows.Point(x, y));
    }

    PerfChartCanvas.Children.Add(polyline);

    var minText = new TextBlock
    {
        Text = $"{min:F1}",
        FontSize = 10,
        Foreground = (System.Windows.Media.Brush)FindResource("MutedForegroundBrush")
    };
    Canvas.SetLeft(minText, leftPad);
    Canvas.SetTop(minText, topPad + plotHeight - 14);
    PerfChartCanvas.Children.Add(minText);

    var maxText = new TextBlock
    {
        Text = $"{max:F1}",
        FontSize = 10,
        Foreground = (System.Windows.Media.Brush)FindResource("MutedForegroundBrush")
    };
    Canvas.SetLeft(maxText, leftPad);
    Canvas.SetTop(maxText, topPad - 2);
    PerfChartCanvas.Children.Add(maxText);
}
private async void PerfRefreshTimer_Tick(object? sender, EventArgs e)
{
    if (_perfRefreshInProgress || !_vm.IsConnected)
        return;

    try
    {
        _perfRefreshInProgress = true;
        await _vm.RefreshPerfOnlyAsync();
        RedrawChart(_vm.SelectedPerfRow);
    }
    finally
    {
        _perfRefreshInProgress = false;
    }
}
    private void ApplyPerfRefreshInterval(object? value = null)
{
    if (_perfRefreshTimer is null)
        return;

    int seconds = value switch
    {
        int i => i,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => _vm.PerfRefreshSeconds
    };

    if (seconds <= 0)
    {
        _perfRefreshTimer.Stop();
        return;
    }

    _perfRefreshTimer.Interval = TimeSpan.FromSeconds(seconds);
    _perfRefreshTimer.Start();
}
}
