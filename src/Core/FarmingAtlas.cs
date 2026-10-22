using System.Text.Json;

namespace PetTracker;

public sealed record SpawnPoint(int PetId, int NpcId, string Name, double X, double Y);
public sealed record MapTile(int Column, int Row);
public sealed record SpawnRegion(string Key, string Name, int Grid, IReadOnlyList<MapTile> Tiles, IReadOnlyList<SpawnPoint> Points)
{
    public override string ToString() => Name;
}

public static class FarmingAtlas
{
    public static IReadOnlyList<SpawnRegion> Regions { get; } = Load();
    static IReadOnlyList<SpawnRegion> Load()
    {
        using var source = typeof(FarmingAtlas).Assembly.GetManifestResourceStream("PetTracker.FarmingSpawns.json")!;
        return JsonSerializer.Deserialize<SpawnRegion[]>(source)!;
    }

    public static IEnumerable<PetSearchResult> Pets(SpawnRegion region, Profile profile, string query, string family, bool hideMaxed)
    {
        var ids = region.Points.Select(p => p.PetId).ToHashSet();
        return PetFinder.Search(query, profile).Where(p => ids.Contains(p.Pet.Id) &&
            (family.Length == 0 || PetFamily.Label(p.Pet.Id) == family) &&
            (!hideMaxed || !profile.Pets.TryGetValue(p.Pet.Id, out var pet) || PetCollection.KnownRank(pet) != 3));
    }
}
