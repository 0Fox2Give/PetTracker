using System.Text.Json;

namespace PetTracker;
// Explicit preference-only reset lists. Account state, window coordinates,
// pinned goals and connection overrides are never reset through this action.
public static class SettingsSections
{
    public static Dictionary<string, JsonElement> Defaults(string section)
    {
        string[] fields = section switch
        {
            "Tracking" => [nameof(AppSettings.ExperimentalDirections), nameof(AppSettings.TrackerMode), nameof(AppSettings.NearbyStaleSeconds), nameof(AppSettings.RowLimit), nameof(AppSettings.PinLimit), nameof(AppSettings.Sort), nameof(AppSettings.HideMaxed), nameof(AppSettings.ExpireInactive), nameof(AppSettings.InactiveSeconds), nameof(AppSettings.BlackMarketAlerts)],
            "Overlay" => [nameof(AppSettings.Scale), nameof(AppSettings.Opacity), nameof(AppSettings.Accent), nameof(AppSettings.Compact), nameof(AppSettings.Portraits), nameof(AppSettings.ProgressBars), nameof(AppSettings.ReducedMotion), nameof(AppSettings.GameOnly), nameof(AppSettings.Hotkey), nameof(AppSettings.Locked), nameof(AppSettings.ClickThrough), nameof(AppSettings.HideWhenEmpty), nameof(AppSettings.Sounds), nameof(AppSettings.Toasts)],
            "Appearance" => [nameof(AppSettings.Accent), nameof(AppSettings.Scale), nameof(AppSettings.Opacity), nameof(AppSettings.Compact), nameof(AppSettings.Portraits), nameof(AppSettings.ProgressBars), nameof(AppSettings.ReducedMotion)],
            "General" => [nameof(AppSettings.StartWithWindows), nameof(AppSettings.StartMinimized), nameof(AppSettings.GameOnly), nameof(AppSettings.Hotkey), nameof(AppSettings.Locked), nameof(AppSettings.ClickThrough), nameof(AppSettings.Sounds), nameof(AppSettings.Toasts), nameof(AppSettings.BlackMarketAlerts)],
            _ => throw new ArgumentException("This section has no preference reset.")};
        var defaults = new AppSettings();
        return fields.ToDictionary(f => f, f => JsonSerializer.SerializeToElement(typeof(AppSettings).GetProperty(f)!.GetValue(defaults)));
    }
}
