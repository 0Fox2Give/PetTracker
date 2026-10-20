namespace PetTracker;

public sealed record ActivityFrameSample(DateTimeOffset Time, string Message, string Result, int Length, string Prefix);
public sealed record ProtocolActivityView(IReadOnlyDictionary<string, long> PetMessages, IReadOnlyDictionary<string, long> ActiveFrames, IReadOnlyDictionary<string, long> Outcomes, DateTimeOffset? CheckStarted, DateTimeOffset? CheckUntil, IReadOnlyList<ActivityFrameSample> Samples);

// Counters run continuously. Byte samples require a deliberate, time-limited
// activity check and only include the known numeric combat/collection lanes.
public sealed class ProtocolActivity
{
    readonly Dictionary<string, long> pets = new(), active = new(), outcomes = new();
    readonly List<ActivityFrameSample> samples = new();
    DateTimeOffset? started, until;
    static void Count(Dictionary<string, long> values, string key)
    {
        if (values.ContainsKey(key) || values.Count < 96)
            values[key] = values.GetValueOrDefault(key) + 1;
    }
    public void Start(DateTimeOffset now)
    {
        started = now;
        until = now.AddSeconds(90);
        samples.Clear();
    }
    public void Frame(byte op, byte family, bool bound)
    {
        if (family == 0x90)
            Count(pets, op.ToString("X2"));
        if (bound && family is 0x36 or 0x38 or 0x90)
            Count(active, $"{family:X2}:{op:X2}");
    }
    public void Observe(byte op, byte family, ReadOnlySpan<byte> body, string result, DateTimeOffset now)
    {
        bool allowed = family == 0x38 && op == 4 || family == 0x36 && op is 0x40 or 0x41 or 0x42 || family == 0x90 && op is 0x0d or 0x0e or 0x14;
        if (!allowed)
            return;
        Count(outcomes, result);
        if (started == null || now < started || now >= until)
            return;
        // Keep rare appearances and rewards when busy areas flood the hit lane.
        // Seven permitted message types, at most sixteen samples each (112 total).
        string message = $"{family:X2}:{op:X2}";
        if (samples.Count(s => s.Message == message) >= 16)
            samples.RemoveAt(samples.FindIndex(s => s.Message == message));
        samples.Add(new(now, $"{family:X2}:{op:X2}", result, body.Length, Convert.ToHexString(body[..Math.Min(body.Length, 192)])));
        if (samples.Count > 128)
            samples.RemoveAt(0);
    }
    public ProtocolActivityView View() => new(new Dictionary<string, long>(pets), new Dictionary<string, long>(active), new Dictionary<string, long>(outcomes), started, until, samples.ToArray());
}
