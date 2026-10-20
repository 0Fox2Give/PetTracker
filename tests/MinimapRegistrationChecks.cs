using System.Text.Json;
using PetTracker;

static class MinimapRegistrationChecks
{
    sealed record Sample(string Name, double[][] World, double[][] Dots, double[] Center, int Matches, double Rms, double[] Player);
    sealed record MarkerSample(string Name, int Angle, bool[] Mask);
    static MapPoint[] Points(double[][] p) => p.Select(v => new MapPoint(v[0], v[1])).ToArray();
    public static void Run(Action<bool, string> check)
    {
        using var stream = typeof(MinimapRegistrationChecks).Assembly.GetManifestResourceStream("MinimapRegistration.json")!;
        var samples = JsonSerializer.Deserialize<Sample[]>(stream)!;
        foreach (var s in samples)
        {
            var world = Points(s.World);
            var dots = Points(s.Dots);
            var fit = MinimapRegistration.Locate(world, dots);
            check(fit != null && fit.Matches.Count == s.Matches && Math.Abs(fit.Rms - s.Rms) < 1e-6, s.Name + ": recorded registration matches independent research result");
            var player = fit!.Unproject(new(s.Center[0], s.Center[1]));
            check(Math.Abs(player.X - s.Player[0]) < 1e-5 && Math.Abs(player.Y - s.Player[1]) < 1e-5, s.Name + ": inverse transform reproduces recorded player position");
            check(MinimapRegistration.Locate(world, dots.Take(5).ToArray()) == null, s.Name + ": fewer than six dots cannot produce guidance");
        }
        var basePoints = Points(samples[3].World);
        var screenPoints = Points(samples[3].Dots);
        var duplicatePattern = basePoints.Concat(basePoints.Select(p => new MapPoint(p.X + 40000, p.Y + 50000))).ToArray();
        check(MinimapRegistration.Locate(duplicatePattern, screenPoints) == null, "Two equally supported mob patterns reject the entire match");
        check(MinimapRegistration.Locate(basePoints, screenPoints.Select(p => new MapPoint(p.X, 0)).ToArray()) == null, "Collinear dots cannot determine a stable map transform");
        check(MinimapRegistration.Locate(basePoints.Append(new(double.NaN, 0)).ToArray(), screenPoints) == null, "Invalid coordinates reject registration");
        check(MinimapRegistration.Locate(Enumerable.Repeat(new MapPoint(1, 2), 46).ToArray(), screenPoints) == null, "Excessive point sets are bounded before hypothesis work");
        using var markers = typeof(MinimapRegistrationChecks).Assembly.GetManifestResourceStream("MinimapMarker.json")!;
        var glyphs = JsonSerializer.Deserialize<MarkerSample[]>(markers)!;
        foreach (var glyph in glyphs)
        {
            var rotation = MinimapMarker.Compare(glyphs[0].Mask, glyph.Mask);
            check(rotation != null && Math.Abs(Math.IEEERemainder(rotation.ClockwiseDegrees - glyph.Angle, 360)) <= 4, glyph.Name + ": real character marker rotation survives nearby-dot interference");
        }
        check(MinimapMarker.Compare(glyphs[0].Mask, new bool[961]) == null, "Missing marker cannot show direction");
        check(MinimapMarker.Compare(glyphs[0].Mask, Enumerable.Repeat(true, 961).ToArray()) == null, "Obscured marker cannot show direction");
        var circle = new bool[961];
        for (int y = 0; y < 31; y++)
            for (int x = 0; x < 31; x++)
                circle[y * 31 + x] = (x - 15) * (x - 15) + (y - 15) * (y - 15) <= 49;
        check(MinimapMarker.Compare(glyphs[0].Mask, circle) == null, "Symmetric round marker is not a heading");
        GuidanceChecks(check, samples[3]);
    }
    static void GuidanceChecks(Action<bool, string> check, Sample sample)
    {
        var at = DateTimeOffset.UtcNow;
        var center = new MapPoint(sample.Center[0], sample.Center[1]);
        var world = Points(sample.World);
        var dots = Points(sample.Dots);
        var fit = MinimapRegistration.Locate(world, dots)!;
        var ordered = fit.Matches.OrderBy(m => Math.Pow(dots[m.Dot].X - center.X, 2) + Math.Pow(dots[m.Dot].Y - center.Y, 2)).ToArray();
        var closest = ordered[0];
        var next = ordered[1];
        double expectedBearing = Math.Atan2(dots[closest.Dot].X - center.X, center.Y - dots[closest.Dot].Y) * 180 / Math.PI;
        NavigationScene Scene(DateTimeOffset time, string scope = "test/zone/1") => new(scope, time, world.Select((p, i) => new NavigationMob(i + 1, i == closest.World || i == next.World ? 1050 : null, p, time)).ToArray());
        MinimapObservation Image(DateTimeOffset time) => new(time, center, dots, expectedBearing - 90);
        var gate = new MinimapGuidance();
        var first = gate.Observe(Scene(at), Image(at), at);
        check(first.Count == 0, "One plausible minimap fit cannot produce a live arrow");
        check(gate.Observe(Scene(at.AddMilliseconds(250)), Image(at.AddMilliseconds(250)), at.AddMilliseconds(250)).Count == 0, "Two fits still require a third confirmation");
        var time = at.AddMilliseconds(500);
        var guidance = gate.Observe(Scene(time), Image(time), time);
        check(guidance.Count == 1 && guidance.TryGetValue(1050, out var cue) && Math.Abs(cue.Bearing(time)!.Value - 90) < 1e-7 && !cue.HasDistance(time), "Three consistent fits select the closest matched pet and use supplied character facing without inventing metres");
        check(guidance[1050].Bearing(time.AddSeconds(2)) == null, "Admitted guidance expires even without another scene");
        check(gate.Observe(Scene(time), Image(time), time).Count == 0, "Repeated capture timestamps cannot refresh guidance");
        check(gate.Observe(Scene(time.AddMilliseconds(250)), Image(time.AddMilliseconds(250)), time.AddMilliseconds(250)).Count == 0, "Repeated frame rejection clears earlier confirmations");
        void Prime()
        {
            gate.Clear();
            for (int i = 0; i < 3; i++)
            {
                var t = at.AddMilliseconds(250 * i);
                gate.Observe(Scene(t), Image(t), t);
            }
        }
        var fresh = at.AddMilliseconds(750);
        Prime();
        var hiddenNearest = Scene(fresh) with
        {
            Mobs = Scene(fresh).Mobs.Append(new NavigationMob(9999, 1050, fit.Unproject(center), fresh)).ToArray()
        };
        check(MinimapRegistration.Locate(hiddenNearest.Mobs.Select(m => m.Position).ToArray(), dots) != null, "An unmatched near pet leaves enough independent geometry for registration");
        check(gate.Observe(hiddenNearest, Image(fresh), fresh).Count == 0, "A closer pet hidden under the player marker prevents an arrow to a farther matched pet");
        Prime();
        var centered = Image(fresh) with
        {
            Center = dots[closest.Dot]
        };
        check(gate.Observe(Scene(fresh), centered, fresh).Count == 0, "A matched nearest pet inside the marker exclusion area cannot silently fall back to a farther pet");
        Prime();
        var unrelatedHidden = hiddenNearest with
        {
            Mobs = hiddenNearest.Mobs.Select(m => m.Entity == 9999 ? m with { PetId = 1051 } : m).ToArray()
        };
        check(gate.Observe(unrelatedHidden, Image(fresh), fresh).ContainsKey(1050), "An unmatched pet of another type does not suppress a validated target");
        Prime();
        check(gate.Observe(Scene(fresh, "other/zone/2"), Image(fresh), fresh).Count == 0, "Connection or zone changes require new confirmations");
        Prime();
        check(gate.Observe(Scene(fresh), Image(fresh) with
        {
            CharacterClockwiseDegrees = null
        }, fresh).Count == 0, "Missing facing never falls back to a minimap-relative arrow");
        Prime();
        check(gate.Observe(Scene(fresh), Image(fresh) with
        {
            CharacterClockwiseDegrees = double.NaN
        }, fresh).Count == 0, "Nonfinite facing rejects guidance");
        Prime();
        check(gate.Observe(Scene(fresh), Image(fresh.AddSeconds(-2)), fresh).Count == 0, "Stale screenshot rejects otherwise fresh scene");
        Prime();
        check(gate.Observe(Scene(fresh.AddSeconds(-2)), Image(fresh), fresh).Count == 0, "Stale scene rejects otherwise fresh screenshot");
        Prime();
        check(gate.Observe(Scene(fresh), Image(fresh.AddMilliseconds(1)), fresh).Count == 0, "Future screenshot rejects guidance");
        // Keep the target current but age all supporting geometry. This must not admit
        // an arrow using yesterday's cluster with a newly observed target.
        Prime();
        var staleAnchors = Scene(fresh) with
        {
            Mobs = Scene(fresh).Mobs.Select(m => m.Entity == closest.World + 1 ? m : m with { ObservedUtc = fresh.AddSeconds(-2) }).ToArray()
        };
        check(gate.Observe(staleAnchors, Image(fresh), fresh).Count == 0, "Stale registration anchors cannot validate a fresh target");
        Prime();
        var removed = Scene(fresh) with
        {
            Mobs = Scene(fresh).Mobs.Where(m => m.PetId == null).ToArray()
        };
        check(gate.Observe(removed, Image(fresh), fresh).Count == 0, "Removed pet entities cannot survive through previous matches");
        Prime();
        var reused = Scene(fresh) with
        {
            Mobs = Scene(fresh).Mobs.Select(m => m with { PetId = null }).ToArray()
        };
        check(gate.Observe(reused, Image(fresh), fresh).Count == 0, "Reused or unmapped entity identities cannot reuse old pet links");
        Prime();
        var duplicate = Scene(fresh) with
        {
            Mobs = Scene(fresh).Mobs.Append(Scene(fresh).Mobs[0]).ToArray()
        };
        check(gate.Observe(duplicate, Image(fresh), fresh).Count == 0, "Duplicate entity identities reject scene registration");
        Prime();
        var zoomed = Image(fresh) with
        {
            Dots = dots.Select(p => new MapPoint(center.X + (p.X - center.X) * 1.1, center.Y + (p.Y - center.Y) * 1.1)).ToArray()
        };
        check(gate.Observe(Scene(fresh), zoomed, fresh).Count == 0, "Minimap zoom changes require fresh confirmation");
    }
}
