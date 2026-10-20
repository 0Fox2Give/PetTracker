namespace PetTracker;

public sealed record NavigationMob(int Entity, int? PetId, MapPoint Position, DateTimeOffset ObservedUtc);
public sealed record NavigationScene(string Scope, DateTimeOffset RecordedUtc, IReadOnlyList<NavigationMob> Mobs);
public sealed record MinimapObservation(DateTimeOffset CapturedUtc, MapPoint Center, IReadOnlyList<MapPoint> Dots, double? CharacterClockwiseDegrees);

// This layer admits direction only. It never converts map units into metres.
public sealed class MinimapGuidance
{
    string? scope;
    DateTimeOffset captured, recorded;
    MinimapFit? previous;
    int consecutive;
    public void Clear()
    {
        scope = null;
        previous = null;
        consecutive = 0;
        captured = default;
        recorded = default;
    }
    static bool Fresh(DateTimeOffset at, DateTimeOffset now) => at <= now && now - at < PetDirection.Lifetime;
    public IReadOnlyDictionary<int, PetDirection> Observe(NavigationScene scene, MinimapObservation image, DateTimeOffset now)
    {
        var empty = new Dictionary<int, PetDirection>();
        if (string.IsNullOrWhiteSpace(scene.Scope) || !Fresh(scene.RecordedUtc, now) || !Fresh(image.CapturedUtc, now) ||
         !image.Center.Finite || image.CharacterClockwiseDegrees is not double heading || !double.IsFinite(heading) ||
         scene.Mobs.Select(m => m.Entity).Distinct().Count() != scene.Mobs.Count)
        {
            Clear();
            return empty;
        }
        // Freshness applies to every registration anchor, not just the chosen target.
        // Otherwise an old cluster can support an apparently current arrow.
        var mobs = scene.Mobs.Where(m => Fresh(m.ObservedUtc, now)).ToArray();
        var fit = MinimapRegistration.Locate(mobs.Select(m => m.Position).ToArray(), image.Dots);
        if (fit == null)
        {
            Clear();
            return empty;
        }
        if (previous != null)
        {
            if (image.CapturedUtc <= captured || scene.RecordedUtc <= recorded)
            {
                Clear();
                return empty;
            }
            bool consistent = scope == scene.Scope && image.CapturedUtc - captured < PetDirection.Lifetime && fit.Reflected == previous.Reflected &&
             Math.Abs(Math.Log(fit.Multiplier.Magnitude / previous.Multiplier.Magnitude)) <= Math.Log(1.02) &&
             Math.Abs(Math.IEEERemainder(fit.Multiplier.Phase - previous.Multiplier.Phase, 2 * Math.PI)) <= 5 * Math.PI / 180;
            if (!consistent)
                Clear();
        }
        scope = scene.Scope;
        captured = image.CapturedUtc;
        recorded = scene.RecordedUtc;
        previous = fit;
        consecutive = Math.Min(consecutive + 1, 3);
        if (consecutive < 3)
            return empty;
        // Choose among all fresh known entities before checking dot visibility. A
        // closer mob can be obscured by the player marker; skipping it must not turn
        // a farther visible mob into a supposedly nearest target.
        var nearest = new Dictionary<int, (double Distance, int World)>();
        for (int i = 0; i < mobs.Length; i++)
        {
            var mob = mobs[i];
            if (mob.PetId is not > 0)
                continue;
            var projected = fit.Project(mob.Position);
            double dx = projected.Real - image.Center.X, dy = projected.Imaginary - image.Center.Y, distance = dx * dx + dy * dy;
            int pet = mob.PetId.Value;
            if (!nearest.TryGetValue(pet, out var old) || distance < old.Distance)
                nearest[pet] = (distance, i);
        }
        var matched = fit.Matches.ToDictionary(m => m.World, m => m.Dot);
        foreach (var (pet, item) in nearest)
        {
            if (item.Distance < 18 * 18 || !matched.TryGetValue(item.World, out int dotIndex))
                continue;
            var mob = mobs[item.World];
            var dot = image.Dots[dotIndex];
            double dx = dot.X - image.Center.X, dy = dot.Y - image.Center.Y;
            if (dx * dx + dy * dy < 18 * 18)
                continue;
            double bearing = Math.Atan2(dx, -dy) * 180 / Math.PI - heading;
            var positionAt = mob.ObservedUtc < image.CapturedUtc ? mob.ObservedUtc : image.CapturedUtc;
            empty[pet] = new(null, positionAt, bearing, image.CapturedUtc);
        }
        return empty;
    }
}
