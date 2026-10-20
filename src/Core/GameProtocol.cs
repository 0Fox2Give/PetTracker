using System.Buffers.Binary;
using System.Text;

namespace PetTracker;

public sealed record LocalCharacter(int Entity, string Name, int ServerToken);
public sealed record NpcSpawn(int Entity, int Npc);
public sealed record OwnHit(int Actor, int Target, int Damage);
public static class GameProtocol
{
    static readonly UTF8Encoding Utf8 = new(false, true);
    public static bool Var(ReadOnlySpan<byte> b, ref int p, out int n)
    {
        n = 0;
        int start = p;
        for (int i = 0; i < 5; i++)
        {
            if (p >= b.Length)
                return false;
            byte v = b[p++];
            if (i == 4 && (v & 0xf8) != 0)
                return false;
            n |= (v & 127) << (7 * i);
            if (v < 128)
                return n >= 0 && (i == 0 || n >= (1 << (7 * i)));
        }

        p = start;
        return false;
    }

    public static LocalCharacter? Local(byte opcode, byte family, ReadOnlySpan<byte> body)
    {
        if (opcode != 0x33 || family != 0x36)
            return null;
        int p = 0;
        if (!Var(body, ref p, out int entity) || entity is < 100 or > 999999 || p + 6 > body.Length)
            return null;
        p += 4;
        if (body[p++] is not (7 or 55 or 63))
            return null;
        int length = body[p++];
        if (length is < 1 or > 30 || p + length + 2 > body.Length)
            return null;
        string name;
        try
        {
            name = Utf8.GetString(body.Slice(p, length));
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        if (!name.Any(char.IsLetter) || name.Any(c => !(char.IsLetterOrDigit(c) || c is '\'' or '-' or '_')))
            return null;
        p += length;
        return new(entity, name, BinaryPrimitives.ReadUInt16LittleEndian(body[p..]));
    }

    public static NpcSpawn? Spawn(byte opcode, byte family, ReadOnlySpan<byte> b, IReadOnlySet<int> allowed)
    {
        var spawn = Appearance(opcode, family, b);
        return spawn != null && allowed.Contains(spawn.Npc) ? spawn : null;
    }

    public static NpcSpawn? Appearance(byte opcode, byte family, ReadOnlySpan<byte> b)
    {
        if (family != 0x36 || opcode is not (0x40 or 0x41 or 0x42))
            return null;
        int p = 0;
        if (!Var(b, ref p, out int entity) || entity <= 0 || p + 3 > b.Length)
            return null;
        // The low five bits identify summons (31). Ordinary NPCs also use 12/13,
        // captured on Global; requiring zero silently discarded their appearances.
        if ((b[p] & 0x1f) == 0x1f)
            return null;
        p += 3;
        foreach (int offset in new[] { p + 2, p })
            if (offset + 6 <= b.Length)
            {
                int npc = BinaryPrimitives.ReadInt32LittleEndian(b[offset..]);
                if (npc is >= 1000000 and <= 10000000 && b[offset + 5] == 2)
                    return new(entity, npc);
            }

        return null;
    }

    public static (int Entity, int Reason)? Removal(byte opcode, byte family, ReadOnlySpan<byte> b)
    {
        int p = 0;
        if (opcode != 0x42 || family != 0x36 || !Var(b, ref p, out int entity) || entity <= 0 || b.Length != p + 2 || b[p] != 0)
            return null;
        return (entity, b[p + 1]);
    }

    public static OwnHit? Hit(byte opcode, byte family, ReadOnlySpan<byte> b) => Hit(opcode, family, b, out _);
    public static OwnHit? Hit(byte opcode, byte family, ReadOnlySpan<byte> b, out string result)
    {
        result = "Different message";
        if (opcode != 4 || family != 0x38)
            return null;
        int p = 0;
        result = "Unsupported hit header";
        if (!Var(b, ref p, out int target) || !Var(b, ref p, out int variant) || (variant & 15) < 4 || !Var(b, ref p, out _) || !Var(b, ref p, out int actor) || p + 5 > b.Length)
            return null;
        uint skill = BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
        p += 4;
        // Restrict this first decoder to player skill codes and proven flag widths.
        result = "Unsupported skill code";
        if (skill is < 11000000 or >= 20000000)
            return null;
        result = "Unsupported hit type";
        if (!Var(b, ref p, out int type) || type is < 0 or > 72 or 36 or 37 or 39 or 40 or 41 or 42)
            return null;
        result = "Unsupported hit flags";
        int flags = (variant & 3) switch
        {
            0 => 8,
            2 => -1,
            _ => 0
        };
        if (flags == 0)
            return null;
        if (flags < 0)
        {
            int q = p + 1;
            if (!Var(b, ref q, out _))
                return null;
            flags = 10 + q - (p + 1);
        }

        if (p + flags > b.Length)
            return null;
        p += flags;
        result = "Unsupported damage fields";
        if (!Var(b, ref p, out _) || !Var(b, ref p, out int damage) || damage is <= 0 or > 10000000)
            return null;
        result = "Decoded hit";
        return new(actor, target, damage);
    }
}
