// =============================================================================
//  Converters.cs  (v1.1 — adds UpdateReadinessToColorConverter)
// =============================================================================

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HVTools.Models;

namespace HVTools.Converters;

public sealed class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is HealthSeverity sev ? sev switch
        {
            HealthSeverity.Error   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38,  38)),
            HealthSeverity.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(217,119,  6)),
            HealthSeverity.OK      => new SolidColorBrush(System.Windows.Media.Color.FromRgb( 22,163, 74)),
            _                      => new SolidColorBrush(System.Windows.Media.Color.FromRgb( 37, 99,235)),
        } : System.Windows.Media.Brushes.Gray;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class HealthSevToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is HealthSeverity sev ? sev switch
        {
            HealthSeverity.Error   => "!",
            HealthSeverity.Warning => "?",
            HealthSeverity.OK      => "✓",
            _                      => "i",
        } : "i";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class PowerStateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "").ToLowerInvariant() switch
        {
            "running" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220,252,231)),
            "off"     => new SolidColorBrush(System.Windows.Media.Color.FromRgb(243,244,246)),
            "saved"   => new SolidColorBrush(System.Windows.Media.Color.FromRgb(219,234,254)),
            "paused"  => new SolidColorBrush(System.Windows.Media.Color.FromRgb(254,243,199)),
            _         => new SolidColorBrush(System.Windows.Media.Color.FromRgb(243,244,246)),
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class PowerStateForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "").ToLowerInvariant() switch
        {
            "running" => new SolidColorBrush(System.Windows.Media.Color.FromRgb( 22,101, 52)),
            "paused"  => new SolidColorBrush(System.Windows.Media.Color.FromRgb(120, 53, 15)),
            "saved"   => new SolidColorBrush(System.Windows.Media.Color.FromRgb( 30, 64,175)),
            _         => new SolidColorBrush(System.Windows.Media.Color.FromRgb( 55, 65, 81)),
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class BoolToYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "Yes" : "No";
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v?.ToString() == "Yes";
}

public sealed class InvertBoolConverter : IValueConverter
{
    public static readonly InvertBoolConverter Instance = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is bool b ? !b : v;
}

/// <summary>Maps UpdateReadiness to a badge background colour.</summary>
public sealed class UpdateReadinessToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is UpdateReadiness r ? r switch
        {
            UpdateReadiness.UpToDate       => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220,252,231)),
            UpdateReadiness.PendingUpdates => new SolidColorBrush(System.Windows.Media.Color.FromRgb(219,234,254)),
            UpdateReadiness.RequiresReboot => new SolidColorBrush(System.Windows.Media.Color.FromRgb(254,243,199)),
            _                              => new SolidColorBrush(System.Windows.Media.Color.FromRgb(243,244,246)),
        } : System.Windows.Media.Brushes.LightGray;
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Returns "☀" or "🌙" based on isDarkMode bool.</summary>
public sealed class ThemeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? "☀" : "🌙";
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class ArcStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value?.ToString() ?? "").ToLowerInvariant() switch
        {
            "connected"    => new SolidColorBrush(System.Windows.Media.Color.FromRgb(220,252,231)),
            "disconnected" => new SolidColorBrush(System.Windows.Media.Color.FromRgb(254,226,226)),
            "expired"      => new SolidColorBrush(System.Windows.Media.Color.FromRgb(254,226,226)),
            _              => new SolidColorBrush(System.Windows.Media.Color.FromRgb(243,244,246)),
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
