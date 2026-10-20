using PetTracker;

static class FramingChecks
{
    public static byte[] LiteralBlock(byte[] frames)
    {
        var result = new List<byte> { 255, 255 };
        result.AddRange(BitConverter.GetBytes(frames.Length));
        result.Add((byte)(Math.Min(15, frames.Length) << 4));
        if (frames.Length >= 15)
        {
            int remaining = frames.Length - 15;
            while (remaining >= 255)
            {
                result.Add(255);
                remaining -= 255;
            }
            result.Add((byte)remaining);
        }
        result.AddRange(frames);
        return result.ToArray();
    }
    public static void Run(Action<bool, string> check)
    {
        // Captured October 12: 8D:3F has an empty body. The length includes the
        // protocol's four-byte adjustment, so 06 3F 8D is a complete three-byte frame.
        byte[] empty = { 6, 0x3f, 0x8d };
        byte[] removal = { 9, 0x42, 0x36, 43, 0, 1 };
        byte[] sequence = empty.Concat(removal).ToArray();
        bool allSplits = true;
        for (int split = 0; split <= sequence.Length; split++)
        {
            var wire = new CollectionWire(Catalog.Entries.Select(p => p.Id));
            var seen = new List<(byte Op, byte Family, int Length)>();
            wire.Frame += (op, family, body) => seen.Add((op, family, body.Length));
            wire.Feed(sequence.AsSpan(0, split));
            wire.Feed(sequence.AsSpan(split));
            allSplits &= wire.Faults == 0 && seen.SequenceEqual(new[] { ((byte)0x3f, (byte)0x8d, 0), ((byte)0x42, (byte)0x36, 3) });
        }
        check(allSplits, "Empty server frames preserve following mob removal across every TCP split");
        var bytewise = new CollectionWire(Catalog.Entries.Select(p => p.Id));
        int count = 0;
        bytewise.Frame += (_, _, _) => count++;
        foreach (byte b in sequence)
            bytewise.Feed(new[] { b });
        check(count == 2 && bytewise.Faults == 0, "Bytewise empty-frame decoding never stalls the stream");
        // Same records inside a literal-only LZ4 block must have identical framing.
        var compressed = new byte[] { 255, 255 }.Concat(BitConverter.GetBytes(sequence.Length))
         .Concat(new[] { (byte)(sequence.Length << 4) }).Concat(sequence).ToArray();
        var nested = new CollectionWire(Catalog.Entries.Select(p => p.Id));
        int nestedCount = 0;
        nested.Frame += (_, _, _) => nestedCount++;
        nested.Feed(compressed);
        check(nestedCount == 2 && nested.Faults == 0, "Raw and compressed streams agree on empty frame boundaries");
        foreach (var malformed in new[] { new byte[] { 0x80 }, new byte[] { 5, 0x3f, 0x8d }, new byte[] { 9, 0x42, 0x36 } })
        {
            var damaged = new CollectionWire(Catalog.Entries.Select(p => p.Id));
            int following = 0;
            damaged.Frame += (_, _, _) => following++;
            damaged.Feed(LiteralBlock(malformed).Concat(empty).ToArray());
            check(damaged.Faults == 1 && following == 1, "Malformed decompressed frame is reported without losing the next raw frame");
        }
    }
}
