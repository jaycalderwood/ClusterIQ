// =============================================================================
//  ThemeManager
//  Swaps the active ResourceDictionary at runtime to apply light / dark theme.
// =============================================================================

using System.Windows;

namespace HVTools;

public static class ThemeManager
{
    private static readonly Uri LightUri = new("Themes/LightTheme.xaml", UriKind.Relative);
    private static readonly Uri DarkUri  = new("Themes/DarkTheme.xaml",  UriKind.Relative);

    /// <summary>Apply "LightTheme" or "DarkTheme".</summary>
    public static void Apply(string themeName)
    {
        var targetUri = themeName == "DarkTheme" ? DarkUri : LightUri;
        var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;

        // Remove old theme dict
        var old = dicts.FirstOrDefault(d => d.Source == LightUri || d.Source == DarkUri);
        if (old is not null) dicts.Remove(old);

        // Add new theme dict
        dicts.Insert(0, new ResourceDictionary { Source = targetUri });
    }
}
