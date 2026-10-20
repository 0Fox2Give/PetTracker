namespace PetTracker;

// Reads rotation of the actual character glyph relative to a reference mask.
// Absolute facing requires a separately calibrated reference angle.
public sealed record MarkerRotation(double ClockwiseDegrees, double Score, double RivalScore);
public static class MinimapMarker
{
    public const int Size = 31;
    public static bool[]? Isolate(IReadOnlyList<bool> mask)
    {
        if (mask.Count != Size * Size)
            return null;
        var visited = new bool[mask.Count];
        var candidates = new List<List<int>>();
        for (int start = 0;
start < mask.Count;
start++)
        {
            if (!mask[start] || visited[start])
                continue;
            var component = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            visited[start] = true;
            int minX = Size, minY = Size, maxX = 0, maxY = 0;
            double sumX = 0, sumY = 0;
            while (queue.TryDequeue(out int p))
            {
                int x = p % Size, y = p / Size;
                component.Add(p);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
                sumX += x;
                sumY += y;
                for (int dy = -1;
dy <= 1;
dy++)
                    for (int dx = -1;
    dx <= 1;
    dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= Size || ny >= Size)
                            continue;
                        int n = ny * Size + nx;
                        if (visited[n] || !mask[n])
                            continue;
                        visited[n] = true;
                        queue.Enqueue(n);
                    }
            }
            int width = maxX - minX + 1, height = maxY - minY + 1;
            double cx = sumX / component.Count - 15, cy = sumY / component.Count - 15;
            if (component.Count is >= 60 and <= 220 && width is >= 12 and <= 29 && height is >= 12 and <= 29 && cx * cx + cy * cy < 25)
                candidates.Add(component);
        }
        if (candidates.Count != 1)
            return null;
        var result = new bool[Size * Size];
        foreach (int p in candidates[0])
            result[p] = true;
        return result;
    }
    public static MarkerRotation? Compare(IReadOnlyList<bool> reference, IReadOnlyList<bool> sample)
    {
        var a = Isolate(reference);
        var b = Isolate(sample);
        if (a == null || b == null)
            return null;
        int bCount = b.Count(v => v);
        var scores = new double[180];
        var rotated = new bool[Size * Size];
        for (int step = 0;
step < 180;
step++)
        {
            double rad = step * 2 * Math.PI / 180, c = Math.Cos(rad), s = Math.Sin(rad);
            Array.Clear(rotated);
            // Inverse nearest-neighbour sampling, matching the retained research images.
            for (int y = 0;
y < Size;
y++)
                for (int x = 0;
    x < Size;
    x++)
                {
                    int rx = (int)Math.Round(c * (x - 15) + s * (y - 15) + 15), ry = (int)Math.Round(-s * (x - 15) + c * (y - 15) + 15);
                    if (rx >= 0 && ry >= 0 && rx < Size && ry < Size)
                        rotated[y * Size + x] = a[ry * Size + rx];
                }
            for (int dy = -1;
dy <= 1;
dy++)
                for (int dx = -1;
    dx <= 1;
    dx++)
                {
                    int count = 0, intersection = 0;
                    for (int y = 0;
    y < Size;
    y++)
                        for (int x = 0;
        x < Size;
        x++)
                        {
                            int sx = x - dx, sy = y - dy;
                            if (sx < 0 || sy < 0 || sx >= Size || sy >= Size || !rotated[sy * Size + sx])
                                continue;
                            count++;
                            if (b[y * Size + x])
                                intersection++;
                        }
                    scores[step] = Math.Max(scores[step], 2.0 * intersection / (count + bCount));
                }
        }
        int best = Array.IndexOf(scores, scores.Max());
        double rival = 0;
        for (int i = 0;
i < 180;
i++)
            if (Math.Abs(Math.IEEERemainder((i - best) * 2, 360)) >= 30)
                rival = Math.Max(rival, scores[i]);
        return scores[best] >= .72 && scores[best] - rival >= .08 ? new(best * 2, scores[best], rival) : null;
    }
}
