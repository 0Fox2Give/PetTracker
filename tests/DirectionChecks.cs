using PetTracker;

static class DirectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var sample = new PetDirection(12, now, 90, now);
        check(sample.HasDistance(now) && sample.Bearing(now) == 90, "Fresh presentation input preserves right-facing bearing and distance");
        check((sample with
        {
            BearingDegrees = -90
        }).Bearing(now) == 270 && (sample with
        {
            BearingDegrees = 450
        }).Bearing(now) == 90, "Arrow bearings wrap correctly at north");
        check(!sample.HasDistance(now.AddSeconds(2)) && sample.Bearing(now.AddSeconds(2)) == null, "Expired positions remove both distance and arrow");
        check(!sample.HasDistance(now.AddTicks(-1)), "Future positions never appear as live guidance");
        check((sample with
        {
            FacingUtc = now.AddSeconds(-2)
        }).HasDistance(now) && (sample with
        {
            FacingUtc = now.AddSeconds(-2)
        }).Bearing(now) == null, "Stale facing removes only the arrow");
        check((sample with
        {
            FacingUtc = now.AddSeconds(1)
        }).Bearing(now) == null && (sample with
        {
            FacingUtc = null
        }).Bearing(now) == null, "Missing or future facing cannot point an arrow");
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            check(!(sample with
            {
                DistanceMetres = invalid
            }).HasDistance(now), "Invalid distance is hidden");
        check((sample with
        {
            DistanceMetres = 0
        }).HasDistance(now) && (sample with
        {
            DistanceMetres = 0
        }).Bearing(now) == null, "Coincident position has no invented direction");
        check((sample with
        {
            BearingDegrees = double.NaN
        }).Bearing(now) == null && (sample with
        {
            BearingDegrees = double.PositiveInfinity
        }).Bearing(now) == null, "Nonfinite facing is hidden");
        check(new NearbyPet(1050, "Gravi", now).Direction == null, "Existing observations do not manufacture measured navigation");
        var compass = new PetDirection(null, now, 90, NorthUp: true);
        check(compass.Bearing(now) == 90 && !compass.HasDistance(now), "Map arrows require no facing and invent no distance");
        check(compass.Bearing(now.AddSeconds(2)) == null && compass.Bearing(now.AddTicks(-1)) == null, "Map arrows reject expired and future position evidence");
        check((compass with
        {
            BearingDegrees = double.NaN
        }).Bearing(now) == null, "Map arrows reject nonfinite bearings");
        check(System.Text.Json.JsonSerializer.Deserialize<PetDirection>(System.Text.Json.JsonSerializer.Serialize(compass))!.Bearing(now) == 90, "Map orientation survives worker serialization without a facing timestamp");
        var arrowOnly = new PetDirection(null, now, 90, now);
        check(!arrowOnly.HasDistance(now) && arrowOnly.Bearing(now) == 90, "Validated direction can display without inventing metres");
        check(arrowOnly.Bearing(now.AddSeconds(2)) == null && (arrowOnly with
        {
            FacingUtc = now.AddSeconds(-2)
        }).Bearing(now) == null, "Direction-only guidance still requires fresh position and heading");
    }
}
