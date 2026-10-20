namespace PetTracker;

public sealed record TcpAssemblyResult(IReadOnlyList<byte[]> Chunks, long LostBytes = 0, bool Buffered = false, bool Duplicate = false);
// A gap is provisional until the bounded reorder window expires. Sequence
// arithmetic is signed modulo 2^32; late and overlapping bytes are trimmed.
public sealed class TcpReassembler
{
    public const int MaxBufferedBytes = 1048576, MaxSegments = 256;
    public static readonly TimeSpan ReorderWindow = TimeSpan.FromSeconds(1);
    readonly Dictionary<uint, byte[]> pending = new();
    uint? expected;
    TimeSpan? gapSince;
    int bufferedBytes;
    public void Reset(uint? next = null)
    {
        expected = next;
        pending.Clear();
        bufferedBytes = 0;
        gapSince = null;
    }

    public TcpAssemblyResult Feed(uint sequence, byte[] bytes, TimeSpan now)
    {
        var chunks = new List<byte[]>();
        long loss = 0;
        bool buffered = false, duplicate = false;
        if (bytes.Length == 0)
            return new(chunks);
        expected ??= sequence;
        int delta = unchecked((int)(sequence - expected.Value));
        if (delta < 0)
        {
            int overlap = -(long)delta >= bytes.Length ? bytes.Length : -delta;
            duplicate = true;
            if (overlap == bytes.Length)
                return new(chunks, Duplicate: true);
            bytes = bytes[overlap..];
            sequence = expected.Value;
            delta = 0;
        }

        if (delta == 0)
        {
            chunks.Add(bytes);
            expected = unchecked(expected.Value + (uint)bytes.Length);
            Drain(chunks);
            gapSince = pending.Count == 0 ? null : now;
        }
        else
        {
            if (pending.TryGetValue(sequence, out var existing))
            {
                if (existing.Length >= bytes.Length)
                    return new(chunks, Duplicate: true);
                bufferedBytes -= existing.Length;
            }

            pending[sequence] = (byte[])bytes.Clone();
            bufferedBytes += bytes.Length;
            buffered = true;
            gapSince ??= now;
            while (bufferedBytes > MaxBufferedBytes || pending.Count > MaxSegments)
                loss += SkipGap(chunks, now);
        }

        return new(chunks, loss, buffered, duplicate);
    }

    public TcpAssemblyResult Poll(TimeSpan now, bool inputPending = false)
    {
        var chunks = new List<byte[]>();
        // A delayed consumer must drain captured packets before declaring a wire gap.
        // Feed still enforces the memory/segment limits while the input queue is busy.
        return new(chunks, !inputPending && gapSince is { } since && now - since >= ReorderWindow ? SkipGap(chunks, now) : 0);
    }

    long SkipGap(List<byte[]> chunks, TimeSpan now)
    {
        if (expected == null || pending.Count == 0)
            return 0;
        var first = pending.OrderBy(p => unchecked((int)(p.Key - expected.Value))).First();
        long lost = Math.Max(0, unchecked((int)(first.Key - expected.Value)));
        expected = first.Key;
        Drain(chunks);
        gapSince = pending.Count == 0 ? null : now;
        return lost;
    }

    void Drain(List<byte[]> chunks)
    {
        while (pending.Count > 0 && expected.HasValue)
        {
            var available = pending.Where(p => unchecked((int)(p.Key - expected.Value)) <= 0).OrderBy(p => unchecked((int)(p.Key - expected.Value))).ToArray();
            if (available.Length == 0)
                break;
            foreach (var part in available)
            {
                pending.Remove(part.Key);
                bufferedBytes -= part.Value.Length;
                long overlap = -(long)unchecked((int)(part.Key - expected.Value));
                if (overlap >= part.Value.Length)
                    continue;
                byte[] tail = part.Value[(int)overlap..];
                chunks.Add(tail);
                expected = unchecked(expected.Value + (uint)tail.Length);
            }
        }
    }
}
