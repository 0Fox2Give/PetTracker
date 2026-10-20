namespace PetTracker;

public static class Activity
{
    static readonly PetNames names = new();
    public static IEnumerable<RecentPetActivity> Select(Tracker t, AppSettings s, DateTimeOffset now)
    {
        var source = s.TrackerMode == "Nearby" ? t.NearbyPets.Where(p => now - p.SeenUtc < TimeSpan.FromSeconds(s.NearbyStaleSeconds) && p.SeenUtc <= now).Select(p => new RecentPetActivity(p.PetId, null, p.CreatureName, p.SeenUtc, true)) : t.RecentActivities;
        var rows = source.Where(a => a.PetId is int id && t.Profile.Pets.ContainsKey(id)).Where(a => !s.HideMaxed || !t.Profile.Pets[a.PetId!.Value].Max).Where(a => a.IsNearby || !s.ExpireInactive || now - a.SeenUtc < TimeSpan.FromSeconds(s.InactiveSeconds + .18));
        // Refresh timestamps describe freshness, not priority. Use a deterministic
        // name order for Nearby and for completed pets so ambient traffic cannot
        // shuffle rows or replace the visible set at the row limit.
        bool Maxed(RecentPetActivity a) => t.Profile.Pets[a.PetId!.Value].Max;
        string Name(RecentPetActivity a) => names.Resolve(t.Profile.Pets[a.PetId!.Value]);
        rows = s.Sort switch
        {
            "Recent only" => rows.OrderByDescending(a => a.SeenUtc).ThenBy(a => a.PetId),
            "Closest to next rank" => rows.OrderBy(Maxed).ThenByDescending(a => Maxed(a) ? 0 : Ratio(t.Profile.Pets[a.PetId!.Value])).ThenBy(Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.PetId),
            _ => rows.OrderBy(Maxed).ThenByDescending(a => s.TrackerMode == "Nearby" || Maxed(a) ? DateTimeOffset.MinValue : a.SeenUtc).ThenBy(Name, StringComparer.OrdinalIgnoreCase).ThenBy(a => a.PetId)};
        var pins = s.Pins().Where(t.Profile.Pets.ContainsKey).Distinct().ToArray();
        return pins.Select(id => source.FirstOrDefault(a => a.PetId == id) ?? new RecentPetActivity(id, null, "", DateTimeOffset.MinValue)).Concat(rows.Where(a => !pins.Contains(a.PetId!.Value))).Take(Math.Max(s.RowLimit, pins.Length + 1));
    }

    // An observation is not proof of present proximity. Pins may outlive sightings.
    public static string ObservationNotice(RecentPetActivity activity, AppSettings settings, DateTimeOffset now)
    {
        if (settings.TrackerMode != "Nearby")
            return "";
        if (!activity.IsNearby)
            return "Not currently observed";
        double age = (now - activity.SeenUtc).TotalSeconds;
        if (age < 0 || age >= settings.NearbyStaleSeconds)
            return "Not currently observed";
        return age < 15 ? "" : $"Last seen {(int)age}s ago";
    }

    public static double Ratio(PetProgress p) => p.Goal is> 0 && p.CurrentSouls is int n ? Math.Clamp((double)n / p.Goal.Value, 0, 1) : 0;
    public static string EmptyText(Tracker t, AppSettings s, DateTimeOffset now, WorkerView? view)
    {
        if (!t.Ready)
            return view?.Status ?? "Connecting to capture…";
        if (s.TrackerMode == "Nearby")
        {
            var nearby = t.NearbyPets.Where(p => t.Profile.Pets.ContainsKey(p.PetId) && p.SeenUtc <= now && now - p.SeenUtc < TimeSpan.FromSeconds(s.NearbyStaleSeconds)).ToArray();
            return nearby.Length > 0 && s.HideMaxed && nearby.All(p => t.Profile.Pets[p.PetId].Max) ? "Nearby pets are maxed. Turn off Hide maxed to see them." : "No pet mobs detected nearby. Explore to discover more.";
        }

        var recent = t.RecentActivities.Where(a => a.PetId is int id && t.Profile.Pets.ContainsKey(id)).ToArray();
        var current = recent.Where(a => a.IsNearby || !s.ExpireInactive || now - a.SeenUtc < TimeSpan.FromSeconds(s.InactiveSeconds + .18)).ToArray();
        if (current.Length > 0 && s.HideMaxed && current.All(a => t.Profile.Pets[a.PetId!.Value].Max))
            return "Recent pets are maxed. Turn off Hide maxed to see them.";
        if (recent.Length > 0 && current.Length == 0 && s.ExpireInactive)
            return "";
        if (view?.Status == "Collection sync needed")
            return "Connected. Waiting for collection totals; pet rewards will still appear here.";
        return "Recent pet rewards will appear here.";
    }
}
