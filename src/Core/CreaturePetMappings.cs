using System.Text.Json;

namespace PetTracker;

public sealed record CreaturePetMatch(int PetId, string CatalogName, string InternalName, string Source = "Source data");
public sealed class CreaturePetMappings
{
    readonly Dictionary<int, CreaturePetMatch> matches = new();
    readonly Dictionary<int, string> names = new();
    public CreaturePetMappings(string root, IReadOnlyDictionary<int, PetProgress> catalog)
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.PetSources.json")!;
        using var doc = JsonDocument.Parse(stream);
        foreach (var group in doc.RootElement.GetProperty("Rows").EnumerateArray().GroupBy(r => r.GetProperty("NpcId").GetInt32()))
        {
            names[group.Key] = group.First().GetProperty("NpcName").GetString() ?? ("NPC #" + group.Key);
            var claims = group.Select(r => (Id: r.GetProperty("PetId").GetInt32(), Name: r.GetProperty("CatalogName").GetString()!, Asset: r.GetProperty("InternalName").GetString()!)).Distinct().ToArray();
            if (claims.Length == 1 && catalog.TryGetValue(claims[0].Id, out var pet) && pet.CatalogName == claims[0].Name)
                matches[group.Key] = new(pet.Id, pet.CatalogName, claims[0].Asset);
        }

        Add(2100034, 1008, "OdSpider_01");
        Add(2100231, 1168, "WaterEle_01");
        Add(2400240, 1163, "Basilisk_01");
        void Add(int npc, int id, string name)
        {
            if (catalog.TryGetValue(id, out var pet) && pet.CatalogName == name)
                matches[npc] = new(id, name, "", "Gameplay/source corroborated");
        }
    }

    public CreaturePetMatch? Find(int npc) => matches.GetValueOrDefault(npc);
    public string CreatureName(int npc) => names.GetValueOrDefault(npc) ?? ("NPC #" + npc);
}
