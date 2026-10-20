using System.Numerics;

namespace PetTracker;

public readonly record struct MapPoint(double X, double Y)
{
    public Complex Complex => new(X, Y);
    public bool Finite => double.IsFinite(X) && double.IsFinite(Y);
}

public sealed record MinimapFit(Complex Multiplier, Complex Offset, bool Reflected, IReadOnlyList<(int World, int Dot)> Matches, double Rms)
{
    public Complex Project(MapPoint point) => Multiplier * (Reflected ? Complex.Conjugate(point.Complex) : point.Complex) + Offset;
    public MapPoint Unproject(MapPoint screen)
    {
        var p = (screen.Complex - Offset) / Multiplier;
        if (Reflected)
            p = Complex.Conjugate(p);
        return new(p.Real, p.Imaginary);
    }
}

// Geometric registration only. This cannot establish NPC identity, metres,
// character facing or freshness; callers must independently validate those.
public static class MinimapRegistration
{
    const double Tolerance = 2.5;
    readonly record struct Triangle(int A, int B, int C, int K1, int K2);
    static IEnumerable<Triangle> Triangles(Complex[] points)
    {
        for (int i = 0; i < points.Length - 2; i++)
            for (int j = i + 1; j < points.Length - 1; j++)
                for (int k = j + 1; k < points.Length; k++)
                {
                    int[] ids = { i, j, k };
                    double[] sides =
                    {
                        Complex.Abs(points[j] - points[k]),
                        Complex.Abs(points[i] - points[k]),
                        Complex.Abs(points[i] - points[j])
                    };
                    Array.Sort(sides, ids);
                    double longest = sides[2];
                    if (longest < 1e-8 || Math.Abs(((points[j] - points[i]) * Complex.Conjugate(points[k] - points[i])).Imaginary) / (longest * longest) < .10 || Math.Min(sides[1] - sides[0], sides[2] - sides[1]) / longest < .03)
                        continue;
                    yield return new(ids[0], ids[1], ids[2], (int)(sides[0] / longest / .02), (int)(sides[1] / longest / .02));
                }
    }

    static (Complex A, Complex B)? Fit(Complex[] world, Complex[] dots, IReadOnlyList<(int World, int Dot)> pairs, bool mirror)
    {
        Complex w = 0, s = 0;
        foreach (var p in pairs)
        {
            w += mirror ? Complex.Conjugate(world[p.World]) : world[p.World];
            s += dots[p.Dot];
        }

        w /= pairs.Count;
        s /= pairs.Count;
        Complex numerator = 0;
        double denominator = 0;
        foreach (var p in pairs)
        {
            var dw = (mirror ? Complex.Conjugate(world[p.World]) : world[p.World]) - w;
            numerator += Complex.Conjugate(dw) * (dots[p.Dot] - s);
            denominator += dw.Magnitude * dw.Magnitude;
        }

        if (denominator < 1e-10)
            return null;
        var a = numerator / denominator;
        return (a, s - a * w);
    }

    static List<(int World, int Dot)> Match(Complex[] world, Complex[] dots, Complex a, Complex b, bool mirror)
    {
        var candidates = new List<(double Distance, int W, int D)>();
        for (int w = 0; w < world.Length; w++)
        {
            var projected = a * (mirror ? Complex.Conjugate(world[w]) : world[w]) + b;
            for (int d = 0; d < dots.Length; d++)
            {
                double distance = Complex.Abs(projected - dots[d]);
                if (distance <= Tolerance)
                    candidates.Add((distance, w, d));
            }
        }

        candidates.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        var usedWorld = new HashSet<int>();
        var usedDots = new HashSet<int>();
        var result = new List<(int, int)>();
        foreach (var c in candidates)
            if (!usedWorld.Contains(c.W) && !usedDots.Contains(c.D))
            {
                usedWorld.Add(c.W);
                usedDots.Add(c.D);
                result.Add((c.W, c.D));
            }

        return result;
    }

    public static MinimapFit? Locate(IReadOnlyList<MapPoint> worldPoints, IReadOnlyList<MapPoint> dotPoints)
    {
        if (worldPoints.Count is < 6 or > 45 || dotPoints.Count is < 6 or > 30 || worldPoints.Any(p => !p.Finite) || dotPoints.Any(p => !p.Finite))
            return null;
        var world = worldPoints.Select(p => p.Complex).ToArray();
        var dots = dotPoints.Select(p => p.Complex).ToArray();
        var index = Triangles(world).GroupBy(t => (t.K1, t.K2)).ToDictionary(g => g.Key, g => g.ToArray());
        var solutions = new List<MinimapFit>();
        int hypotheses = 0;
        foreach (var screen in Triangles(dots))
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (!index.TryGetValue((screen.K1 + dx, screen.K2 + dy), out var options))
                        continue;
                    foreach (var tri in options)
                        foreach (bool mirror in new[] { false, true })
                        {
                            if (++hypotheses > 60000)
                                return null;
                            var fit = Fit(world, dots, new[] { (tri.A, screen.A), (tri.B, screen.B), (tri.C, screen.C) }, mirror);
                            if (fit == null || fit.Value.A.Magnitude < 1e-8)
                                continue;
                            var(a, b) = fit.Value;
                            var matches = Match(world, dots, a, b, mirror);
                            if (matches.Count < 6)
                                continue;
                            for (int step = 0; step < 2; step++)
                            {
                                fit = Fit(world, dots, matches, mirror);
                                if (fit == null)
                                    break;
                                (a, b) = fit.Value;
                                matches = Match(world, dots, a, b, mirror);
                                if (matches.Count < 6)
                                    break;
                            }

                            if (fit == null || matches.Count < 6)
                                continue;
                            double mx = matches.Average(p => dots[p.Dot].Real), my = matches.Average(p => dots[p.Dot].Imaginary), xx = 0, xy = 0, yy = 0, sum = 0;
                            foreach (var p in matches)
                            {
                                double x = dots[p.Dot].Real - mx, y = dots[p.Dot].Imaginary - my;
                                xx += x * x;
                                xy += x * y;
                                yy += y * y;
                                double distance = Complex.Abs(a * (mirror ? Complex.Conjugate(world[p.World]) : world[p.World]) + b - dots[p.Dot]);
                                sum += distance * distance;
                            }

                            double smallEigenvalue = (xx + yy - Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy)) / 2;
                            if (smallEigenvalue < 144)
                                continue;
                            var candidate = new MinimapFit(a, b, mirror, matches, Math.Sqrt(sum / matches.Count));
                            int same = solutions.FindIndex(old => worldPoints.All(p => Complex.Abs(old.Project(p) - candidate.Project(p)) < Tolerance));
                            if (same < 0)
                                solutions.Add(candidate);
                            else if (candidate.Matches.Count > solutions[same].Matches.Count || candidate.Matches.Count == solutions[same].Matches.Count && candidate.Rms < solutions[same].Rms)
                                solutions[same] = candidate;
                        }
                }

        var ranked = solutions.OrderByDescending(s => s.Matches.Count).ThenBy(s => s.Rms).ToArray();
        if (ranked.Length == 0 || ranked[0].Rms > 1 || ranked.Length > 1 && ranked[1].Matches.Count >= ranked[0].Matches.Count - 1)
            return null;
        return ranked[0];
    }
}
