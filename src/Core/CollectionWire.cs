using System.Buffers.Binary;

namespace PetTracker;

public sealed record CollectionSnapshot(int EquippedPet, IReadOnlyDictionary<int, int> Ranks, IReadOnlyDictionary<int, int> Souls);
public sealed record CollectionWireEvent(CollectionSnapshot? Snapshot, int PetId = 0, int Delta = 0, int? Rank = null, int? Spent = null);
public sealed record PetWireFrame(byte Opcode, int BodyLength, byte[] BodyPrefix);
public sealed record DecodeFault(string Reason, byte? Opcode, byte? Family, string Prefix);

// Verified US login shape: framed 0090, equipped pet, varint rank count,
// repeated-ID/rank triples, then varint soul count and ID/current pairs.
// Framing includes the physical varint width. Opaque frame bodies are skipped.
public sealed class CollectionWire
{
    public const int MaxBuffer = 1048576;
    public event Action<byte, byte, byte[]>? Frame;
    public event Action<CollectionWireEvent>? Decoded;
    public event Action<DecodeFault>? Faulted;
    readonly HashSet<int> catalog;
    readonly Action<PetWireFrame>? observeFrame;
    readonly List<byte> pending = new();
    public long Faults
    {
        get; private set;
    }
    void Fault(string reason, byte? op = null, byte? family = null, ReadOnlySpan<byte> body = default)
    {
        Faults++;
        Faulted?.Invoke(new(reason, op, family, Convert.ToHexString(body[..Math.Min(64, body.Length)])));
    }
    public CollectionWire(IEnumerable<int> ids, Action<PetWireFrame>? observeFrame = null)
    {
        catalog = ids.ToHashSet();
        this.observeFrame = observeFrame;
    }
    public void Reset() => pending.Clear();
    enum DecodeState
    {
        Complete, Incomplete, Invalid
    }
    public IReadOnlyList<CollectionWireEvent> Feed(ReadOnlySpan<byte> bytes)
    {
        var events = new List<CollectionWireEvent>();
        if (bytes.Length > MaxBuffer || pending.Count + bytes.Length > MaxBuffer)
        {
            Reset();
            Fault("Decoder buffer limit exceeded");
            return events;
        }
        foreach (byte b in bytes)
            pending.Add(b);
        byte[] data = pending.ToArray();
        int cursor = 0;
        while (cursor < data.Length)
        {
            if (data[cursor] == 255)
            {
                if (data.Length - cursor < 2)
                    break;
                if (data[cursor + 1] == 255)
                {
                    if (data.Length - cursor < 6)
                        break;
                    int size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(cursor + 2, 4));
                    if (size is <= 0 or > 1000000)
                    {
                        cursor++;
                        continue;
                    }
                    var body = new byte[size];
                    var status = DecodeBlock(data.AsSpan(cursor + 6), body, out int used);
                    if (status == DecodeState.Incomplete)
                        break;
                    if (status == DecodeState.Invalid)
                    {
                        Reset();
                        Fault("Invalid compressed stream");
                        return events;
                    }
                    ReadFrames(body, events, 1);
                    cursor += 6 + used;
                    continue;
                }
            }
            int start = cursor;
            if (!Varint(data, ref cursor, out int declared))
            {
                if (declared < 0)
                {
                    cursor = start + 1;
                    continue;
                }
                cursor = start;
                break;
            }
            int width = cursor - start, actual = declared + width - 4;
            // Empty-body frames (captured 8D:3F) still contain a complete opcode/family.
            // Skipping their length byte misaligns everything that follows.
            if (actual < width + 2 || actual > MaxBuffer)
            {
                cursor = start + 1;
                continue;
            }
            if (data.Length - start < actual)
            {
                cursor = start;
                break;
            }
            ReadFrame(data.AsSpan(start, actual), width, events);
            cursor = start + actual;
        }
        if (cursor > 0)
            pending.RemoveRange(0, cursor);
        return events;
    }
    void ReadFrames(ReadOnlySpan<byte> body, List<CollectionWireEvent> events, int depth = 0)
    {
        int cursor = 0;
        while (cursor < body.Length)
        {
            // This is a complete decompressed block, not a partial TCP buffer. Silently
            // dropping its remainder could lose a soul reward while totals stay trusted.
            int start = cursor;
            if (!Varint(body, ref cursor, out int declared))
            {
                Fault("Invalid frame length inside compressed block");
                return;
            }
            int width = cursor - start, actual = declared + width - 4;
            if (actual < width + 2 || actual > body.Length - start)
            {
                Fault("Invalid frame boundary inside compressed block");
                return;
            }
            ReadFrame(body.Slice(start, actual), width, events, depth);
            cursor = start + actual;
        }
    }
    void ReadFrame(ReadOnlySpan<byte> frame, int header, List<CollectionWireEvent> events, int depth = 0)
    {
        if (frame[header] == 255 && frame[header + 1] == 255)
        {
            var compressed = frame[(header + 2)..];
            if (depth >= 3 || compressed.Length < 4)
            {
                Fault("Invalid nested compression header");
                return;
            }
            int size = BinaryPrimitives.ReadInt32LittleEndian(compressed);
            if (size is <= 0 or > 1000000)
            {
                Fault("Invalid nested compression size");
                return;
            }
            var bodyBytes = new byte[size];
            if (DecodeBlock(compressed[4..], bodyBytes, out _) == DecodeState.Complete)
                ReadFrames(bodyBytes, events, depth + 1);
            else
                Fault("Invalid nested compressed block");
            return;
        }
        Frame?.Invoke(frame[header], frame[header + 1], frame[(header + 2)..].ToArray());
        if (frame[header + 1] != 0x90)
            return;
        var body = frame[(header + 2)..];
        observeFrame?.Invoke(new(frame[header], body.Length, body[..Math.Min(512, body.Length)].ToArray()));
        int first = events.Count;
        if (frame[header] is 0x0d or 0x0e)
        {
            // The leading varint is an entry count, not a subtype. Captured packets
            // include multiple pet IDs as well as rewards worth more than one point.
            int cursor = 0;
            if (!Varint(body, ref cursor, out int count) || count <= 0 || count > catalog.Count || body.Length - cursor != count * 8)
            {
                Fault("Unexpected soul batch layout", frame[header], 0x90, body);
                return;
            }
            var decoded = new List<CollectionWireEvent>(count);
            var ids = new HashSet<int>();
            for (int n = 0;
n < count;
n++)
            {
                int id = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor, 4)), value = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor + 4, 4));
                cursor += 8;
                if (!catalog.Contains(id) || !ids.Add(id) || value is <= 0 or > 75)
                {
                    Fault("Unsupported soul batch entry", frame[header], 0x90, body);
                    return;
                }
                decoded.Add(frame[header] == 0x0d ? new(null, id, value) : new(null, id, Spent: value));
            }
            events.AddRange(decoded);
        }
        else if (frame[header] == 0x14 && body.Length == 23 && body[0] == 0 && body[1] == 0)
        {
            // Captured rank-2 and rank-3 successes: status u16, pet ID, owned rank.
            int id = BinaryPrimitives.ReadInt32LittleEndian(body[2..6]), rank = BinaryPrimitives.ReadInt32LittleEndian(body[6..10]);
            if (catalog.Contains(id) && rank is >= 1 and <= 3)
                events.Add(new(null, id, Rank: rank));
        }
        else if (frame[header] == 0)
        {
            if (TrySnapshot(body, out var snapshot))
                events.Add(new(snapshot));
            else
                Fault("Unexpected collection snapshot", 0, 0x90, body);
        }
        for (int index = first;
index < events.Count;
index++)
            Decoded?.Invoke(events[index]);
    }
    bool TrySnapshot(ReadOnlySpan<byte> body, out CollectionSnapshot? snapshot)
    {
        snapshot = null;
        if (body.Length < 6)
            return false;
        int equipped = BinaryPrimitives.ReadInt32LittleEndian(body);
        if (equipped != 0 && !catalog.Contains(equipped))
            return false;
        int cursor = 4;
        if (!Varint(body, ref cursor, out int count) || count > catalog.Count || count < 0 || count > (body.Length - cursor) / 12)
            return false;
        var ranks = new Dictionary<int, int>();
        for (int n = 0;
n < count;
n++)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor, 4)), duplicate = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor + 4, 4)), rank = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor + 8, 4));
            cursor += 12;
            if (!catalog.Contains(id) || duplicate != id || rank is < 1 or > 3 || !ranks.TryAdd(id, rank))
                return false;
        }
        if (equipped != 0 && !ranks.ContainsKey(equipped))
            return false;
        if (!Varint(body, ref cursor, out count) || count > catalog.Count || count < 0 || count > (body.Length - cursor) / 8)
            return false;
        var souls = new Dictionary<int, int>();
        for (int n = 0;
n < count;
n++)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor, 4)), value = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(cursor + 4, 4));
            cursor += 8;
            int rank = ranks.GetValueOrDefault(id), cap = rank is 1 or 2 ? PetStages.CounterGoal(rank)!.Value : int.MaxValue;
            if (!catalog.Contains(id) || value < 0 || value > cap || !souls.TryAdd(id, value))
                return false;
        }
        snapshot = new(equipped, ranks, souls);
        return true;
    }
    static bool Varint(ReadOnlySpan<byte> data, ref int cursor, out int value)
    {
        int start = cursor;
        value = 0;
        for (int n = 0;
n < 3;
n++)
        {
            if (cursor >= data.Length)
                return false;
            byte b = data[cursor++];
            value |= (b & 127) << (n * 7);
            if (b < 128)
            {
                if (n > 0 && value < (1 << (7 * n)))
                {
                    value = -1;
                    return false;
                }
                return true;
            }
        }
        cursor = start + 1;
        value = -1;
        return false;
    }
    // Stop at the declared output length, before consuming the following raw
    // frame as another LZ4 back-reference. The native lenient decoder overruns it.
    static DecodeState DecodeBlock(ReadOnlySpan<byte> input, Span<byte> output, out int consumed)
    {
        consumed = 0;
        int source = 0, target = 0;
        while (target < output.Length)
        {
            if (source >= input.Length)
                return DecodeState.Incomplete;
            byte token = input[source++];
            int literal = token >> 4;
            if (literal == 15)
            {
                byte b;
                do
                {
                    if (source >= input.Length)
                        return DecodeState.Incomplete;
                    b = input[source++];
                    literal += b;
                    if (literal > MaxBuffer)
                        return DecodeState.Invalid;
                } while (b == 255);
            }
            if (literal > output.Length - target)
                return DecodeState.Invalid;
            if (literal > input.Length - source)
                return DecodeState.Incomplete;
            input.Slice(source, literal).CopyTo(output[target..]);
            source += literal;
            target += literal;
            if (target == output.Length)
            {
                consumed = source;
                return DecodeState.Complete;
            }
            if (input.Length - source < 2)
                return DecodeState.Incomplete;
            int offset = input[source] | input[source + 1] << 8;
            source += 2;
            if (offset == 0 || offset > target)
                return DecodeState.Invalid;
            int match = token & 15;
            if (match == 15)
            {
                byte b;
                do
                {
                    if (source >= input.Length)
                        return DecodeState.Incomplete;
                    b = input[source++];
                    match += b;
                    if (match > MaxBuffer)
                        return DecodeState.Invalid;
                } while (b == 255);
            }
            match += 4;
            if (match > output.Length - target)
                return DecodeState.Invalid;
            for (int n = 0;
n < match;
n++)
            {
                output[target] = output[target - offset];
                target++;
            }
        }
        consumed = source;
        return DecodeState.Complete;
    }
}
