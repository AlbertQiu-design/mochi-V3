using System.Globalization;
using Mochi.Core;

namespace MochiIsland;

/// <summary>Small English/Chinese switch for the Settings window.</summary>
internal static class L
{
    public static bool Chinese(IslandPrefs prefs) => prefs.Language switch
    {
        "zh" => true,
        "en" => false,
        _ => CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
    };

    public static string T(IslandPrefs prefs, string en, string zh) => Chinese(prefs) ? zh : en;
}
