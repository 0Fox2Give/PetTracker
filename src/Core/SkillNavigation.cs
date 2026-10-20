using System.Buffers.Binary;

namespace PetTracker;

public sealed record WorldPoint(double X, double Y, double Z);
public sealed record SkillFacing(int Actor, int Target, uint Skill, double Degrees, WorldPoint? TargetPoint);
public sealed record EntityPose(int Entity, WorldPoint Point, double Degrees);
public sealed record NavigationHealth(double? PositionAgeMs, double? FacingAgeMs, int Mobs, int FreshMobs, int Arrows);
// Narrow Global forms corroborated against private captures. Cast coordinates
// belong to the target; dodge forms can contain a displacement vector instead.
// Never use them as the player's location. No coordinate-to-metre conversion.
public static class NavigationProtocol
{
    public static WorldPoint? Point(ReadOnlySpan<byte> b, int p)
    {
        if (p < 0 || p + 12 > b.Length)
            return null;
        double x = BinaryPrimitives.ReadSingleLittleEndian(b[p..]), y = BinaryPrimitives.ReadSingleLittleEndian(b[(p + 4)..]), z = BinaryPrimitives.ReadSingleLittleEndian(b[(p + 8)..]);
        return double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z) && Math.Abs(x) < 1e7 && Math.Abs(y) < 1e7 && Math.Abs(z) < 1e7 ? new(x, y, z) : null;
    }

    public static SkillFacing? Cast(ReadOnlySpan<byte> b)
    {
        int p = 0;
        if (!GameProtocol.Var(b, ref p, out int actor) || actor <= 0 || p + 7 > b.Length || b[p] != 0)
            return null;
        uint skill = BinaryPrimitives.ReadUInt32LittleEndian(b[(p + 1)..]);
        byte flags = b[p + 6];
        p += 7;
        if (skill is < 11000000 or >= 20000000 || flags is not (0 or 2) || !GameProtocol.Var(b, ref p, out int target) || target <= 0 || p + 4 > b.Length)
            return null;
        double yaw = BinaryPrimitives.ReadSingleLittleEndian(b[p..]);
        p += 4;
        if (!double.IsFinite(yaw) || yaw is < -180 or > 360)
            return null;
        WorldPoint? point = null;
        if (flags == 2)
        {
            point = Point(b, p);
            if (point == null)
                return null;
            p += 12;
        }

        // All observed forms end with three canonical varints. Reject shifted layouts.
        for (int i = 0; i < 3; i++)
            if (!GameProtocol.Var(b, ref p, out _))
                return null;
        if (p != b.Length)
            return null;
        return new(actor, target, skill, yaw, point);
    }

    public static (int Entity, WorldPoint Point)? SelfBuff(ReadOnlySpan<byte> b, bool update = false)
    {
        int p = 0;
        if (!GameProtocol.Var(b, ref p, out int entity) || entity <= 0)
            return null;
        if (!update && (!GameProtocol.Var(b, ref p, out int count) || count != 1))
            return null;
        if (p >= b.Length)
            return null;
        byte flags = b[p++];
        if (flags is not (0x11 or 0x13) || !GameProtocol.Var(b, ref p, out _) || p + 20 > b.Length)
            return null;
        p += 20;
        if (!GameProtocol.Var(b, ref p, out int source) || source != entity || p >= b.Length)
            return null;
        p++;
        if (flags == 0x13)
            p += 4;
        if (p + 13 != b.Length || b[p] != (update ? 2 : 0) || Point(b, p + 1) is not { } point)
            return null;
        return (entity, point);
    }

    public static WorldPoint? Spawn(ReadOnlySpan<byte> b, NpcSpawn spawn)
    {
        int p = 0;
        if (!GameProtocol.Var(b, ref p, out int entity) || entity != spawn.Entity)
            return null;
        p += 3;
        if (p + 6 > b.Length || BinaryPrimitives.ReadInt32LittleEndian(b[p..]) != spawn.Npc || b[p + 4] != 0 || b[p + 5] != 2)
            return null;
        return Point(b, p + 6);
    }

    public static EntityPose? ActionPose(ReadOnlySpan<byte> b)
    {
        int p = 0;
        if (!GameProtocol.Var(b, ref p, out int entity) || entity <= 0 || p + 29 > b.Length || b[p++] != 3)
            return null;
        var point = Point(b, p);
        if (point == null)
            return null;
        p += 12;
        double yaw = BinaryPrimitives.ReadSingleLittleEndian(b[p..]);
        p += 4;
        if (!double.IsFinite(yaw) || yaw is < -180 or > 360)
            return null;
        // Recorded 37:3C forms: pose, two action flags, one skill/effect pair,
        // then a canonical varint. The shorter flag-0 form has no usable pose.
        if (!((b[p] == 0 && b[p + 1] == 0) || (b[p] == 7 && b[p + 1] == 2)) || b[p + 2] != 1)
            return null;
        p += 3;
        uint skill = BinaryPrimitives.ReadUInt32LittleEndian(b[p..]);
        p += 8;
        if (skill is < 11000000 or >= 20000000 || !GameProtocol.Var(b, ref p, out _) || p != b.Length)
            return null;
        return new(entity, point, yaw);
    }

    public static (int Entity, WorldPoint? Point)? Movement(byte op, ReadOnlySpan<byte> b)
    {
        if (op is not (0x1a or 0x1b or 0x1c or 0x1d or 0x1e or 0x3c))
            return null;
        int p = 0;
        if (!GameProtocol.Var(b, ref p, out int entity) || entity <= 0 || p >= b.Length)
            return null;
        return (entity, op is 0x1d or 0x1e or 0x3c ? null : Point(b, op == 0x1a ? p + 2 : p + 1 + ((b[p] & 1) != 0 ? 1 : 0)));
    }

    public static bool NpcWalk(byte op, ReadOnlySpan<byte> b)
    {
        int p = 0;
        return op is 0x1b or 0x1c && GameProtocol.Var(b, ref p, out _) && b.Length == p + 18 && b[p] == 4 && b[^1] == 1 && Point(b, p + 1) != null;
    }

    public static bool NpcStop(byte op, ReadOnlySpan<byte> b)
    {
        int p = 0;
        return op == 0x1a && GameProtocol.Var(b, ref p, out _) && b.Length == p + 16 && b[p] == 0 && b[p + 1] == 0 && Point(b, p + 2) != null;
    }
}

// Experimental, transient, independent of collections and rewards. It uses the
// map orientation, independent of facing, and hides on unparsed local movement.
public sealed class SkillNavigation
{
    readonly CreaturePetMappings mappings;
    sealed record Mob(int Pet, WorldPoint? Point, DateTimeOffset At, bool Walking = false, bool Stopped = false);
    readonly Dictionary<int, Mob> mobs = new();
    DateTimeOffset lastFrame;
    int self;
    WorldPoint? position;
    DateTimeOffset positionAt, facingAt;
    double? facing;
    public SkillNavigation(CreaturePetMappings mappings)
    {
        this.mappings = mappings;
    }

    public void Clear()
    {
        mobs.Clear();
        self = 0;
        position = null;
        facing = null;
        positionAt = facingAt = lastFrame = default;
    }

    public void Frame(int local, byte op, byte family, ReadOnlySpan<byte> body, DateTimeOffset now)
    {
        if (local <= 0)
        {
            Clear();
            return;
        }

        if (local != self)
        {
            Clear();
            self = local;
        }

        // A stationary state requires an uninterrupted active stream. Reappearance
        // of traffic cannot revive a state whose observation continuity was lost.
        if (!Fresh(lastFrame, now))
            foreach (var id in mobs.Keys.ToArray())
                mobs[id] = mobs[id] with { Walking = false, Stopped = false };
        lastFrame = now;
        if (family == 0x36 && op == 0x23)
        {
            Clear();
            return;
        }

        if (GameProtocol.Removal(op, family, body) is { } removal)
        {
            mobs.Remove(removal.Entity);
            if (removal.Entity == self)
                Clear();
            return;
        }

        if (GameProtocol.Appearance(op, family, body) is { } spawn)
        {
            mobs.Remove(spawn.Entity);
            var match = mappings.Find(spawn.Npc);
            if (match != null)
            {
                if (mobs.Count >= NearbyWorld.Capacity)
                    mobs.Remove(mobs.MinBy(p => p.Value.At).Key);
                mobs[spawn.Entity] = new(match.PetId, op == 0x41 ? NavigationProtocol.Spawn(body, spawn) : null, now);
            }

            return;
        }

        if (family == 0x37 && op == 0x3c && NavigationProtocol.ActionPose(body) is { } pose)
        {
            if (pose.Entity == self)
            {
                position = pose.Point;
                positionAt = now;
                facing = pose.Degrees;
                facingAt = now;
            }
            else if (mobs.TryGetValue(pose.Entity, out var mob))
                mobs[pose.Entity] = new(mob.Pet, pose.Point, now);
            return;
        }

        if (family == 0x37 && NavigationProtocol.Movement(op, body) is { } move)
        {
            // Ordinary movement layouts are observed for other entities, not yet verified
            // for the local player. A compass arrow must not newly admit these as a fix
            // just because it no longer requires a later skill-facing observation.
            if (move.Entity == self)
            {
                position = null;
                positionAt = now;
                facing = null;
            }
            else if (mobs.TryGetValue(move.Entity, out var mob))
            {
                bool walk = NavigationProtocol.NpcWalk(op, body);
                bool stop = mob.Walking && NavigationProtocol.NpcStop(op, body);
                mobs[move.Entity] = new(mob.Pet, move.Point, now, walk || op == 0x1d && mob.Walking, stop);
            }

            return;
        }

        // Combat can displace a stopped NPC through messages outside ordinary walk
        // updates (recorded 38:14/15 and 37:2E/2F). Never hold its old stop position.
        if (family is 0x37 or 0x38)
        {
            int p = 0;
            if (GameProtocol.Var(body, ref p, out int actor))
            {
                if (mobs.TryGetValue(actor, out var mob))
                    mobs[actor] = mob with { Walking = false, Stopped = false, Point = null };
                if (actor == self && (family == 0x37 || op is 0x14 or 0x15))
                {
                    position = null;
                    facing = null;
                }
            }
        }

        if (family == 0x38 && op is 0x2a or 0x2b && NavigationProtocol.SelfBuff(body, op == 0x2b) is { } buff && buff.Entity == self)
        {
            position = buff.Point;
            positionAt = now;
            return;
        }

        if (family == 0x38 && op == 2)
        {
            int p = 0;
            if (!GameProtocol.Var(body, ref p, out int actor) || actor != self)
                return;
            facing = null;
            if (NavigationProtocol.Cast(body) is not { } cast)
            {
                position = null;
                return;
            }

            facing = cast.Degrees;
            facingAt = now;
            // Dodge XYZ is a displacement, but yaw is still the action's facing.
            // Wait for an independent absolute position before presenting guidance.
            if (cast.Skill % 1000000 < 10000)
            {
                position = null;
                return;
            }

            if (cast.Target != self && cast.TargetPoint is { } target && mobs.TryGetValue(cast.Target, out var mob))
                mobs[cast.Target] = new(mob.Pet, target, now);
        }
    }

    static bool Fresh(DateTimeOffset at, DateTimeOffset now) => at <= now && now - at < PetDirection.Lifetime;
    bool Usable(Mob mob, DateTimeOffset now) => mob.Point != null && mob.At <= now && (Fresh(mob.At, now) || mob.Stopped && Fresh(lastFrame, now));
    public NavigationHealth Health(DateTimeOffset now) => new(position == null ? null : (now - positionAt).TotalMilliseconds, facing == null ? null : (now - facingAt).TotalMilliseconds, mobs.Count, mobs.Values.Count(m => Usable(m, now)), View(now).Count);
    public IReadOnlyDictionary<int, PetDirection> View(DateTimeOffset now)
    {
        var result = new Dictionary<int, PetDirection>();
        if (position == null || !Fresh(positionAt, now))
            return result;
        foreach (var group in mobs.Values.GroupBy(m => m.Pet))
        {
            // A stale instance could be closer. Do not label a fresh farther one nearest.
            if (group.Any(m => !Usable(m, now)))
                continue;
            var nearest = group.MinBy(m => Math.Pow(m.Point!.X - position.X, 2) + Math.Pow(m.Point.Y - position.Y, 2))!;
            double dx = nearest.Point!.X - position.X, dy = nearest.Point.Y - position.Y;
            if (dx * dx + dy * dy < 1)
                continue;
            // For an explicitly stopped mob the proof remains current with the ordered
            // stream, until a new action invalidates it. Player pose still expires in 2s.
            var targetAt = nearest.Stopped ? lastFrame : nearest.At;
            var at = targetAt < positionAt ? targetAt : positionAt;
            // Recorded minimap registrations place +X right and +Y down. The indicator
            // starts pointing up, hence +90 degrees. No character/camera yaw is needed.
            result[group.Key] = new(null, at, Math.Atan2(dy, dx) * 180 / Math.PI + 90, NorthUp: true);
        }

        return result;
    }
}
