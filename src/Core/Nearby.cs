namespace PetTracker;

public sealed record NearbyPet(int PetId, string CreatureName, DateTimeOffset SeenUtc, PetDirection? Direction = null);

// Appearance means the client received this NPC, not a measured distance.
// Never persisted and never used to infer souls, ranks or kill participation.
public sealed class NearbyWorld
{
    public const int Capacity = 2048;
    readonly CreaturePetMappings mappings;
    readonly SkillNavigation navigation;
    readonly Dictionary<int, (int Pet, string Name, DateTimeOffset Seen)> entities = new();
    public NearbyWorld(CreaturePetMappings mappings)
    {
        this.mappings = mappings;
        navigation = new(mappings);
    }
    public void Clear()
    {
        entities.Clear();
        navigation.Clear();
    }
    public void NavigationFrame(bool enabled, int local, byte op, byte family, byte[] body, DateTimeOffset now)
    {
        // Movement confirms a previously identified mob is still in view even if it
        // hasn't been hit or spawned again. This does not depend on arrow opt-in.
        if (family == 0x37)
        {
            if (op == 0x3c && NavigationProtocol.ActionPose(body) is { } pose)
                Refresh(pose.Entity, now);
            else if (NavigationProtocol.Movement(op, body) is { Point: not null } movement)
                Refresh(movement.Entity, now);
        }
        if (enabled)
            navigation.Frame(local, op, family, body, now);
        else
            navigation.Clear();
    }
    public NavigationHealth NavigationHealth(DateTimeOffset now) => navigation.Health(now);
    public void Remove(int entity) => entities.Remove(entity);
    public void Observe(NpcSpawn spawn, DateTimeOffset now)
    {
        var match = mappings.Find(spawn.Npc);
        if (match == null)
        {
            Remove(spawn.Entity);
            return;
        }
        if (!entities.ContainsKey(spawn.Entity) && entities.Count >= Capacity)
            entities.Remove(entities.MinBy(p => p.Value.Seen).Key);
        entities[spawn.Entity] = (match.PetId, mappings.CreatureName(spawn.Npc), now);
    }
    public void Refresh(int entity, DateTimeOffset now)
    {
        if (entities.TryGetValue(entity, out var seen))
            entities[entity] = (seen.Pet, seen.Name, now);
    }
    public IReadOnlyList<NearbyPet> View(DateTimeOffset now, int seconds, bool directions = false)
    {
        var guidance = directions ? navigation.View(now) : null;
        // Keep bounded entity identity metadata after hiding a row, so a later valid
        // movement can bring it back. Explicit removal and Clear still retire it.
        return entities.Values.Where(p => p.Seen <= now && now - p.Seen < TimeSpan.FromSeconds(seconds)).GroupBy(p => p.Pet).Select(g => new NearbyPet(g.Key, string.Join(", ", g.Select(p => p.Name).Distinct().Order()), g.Max(p => p.Seen), guidance?.GetValueOrDefault(g.Key))).OrderByDescending(p => p.SeenUtc).ToArray();
    }
}
