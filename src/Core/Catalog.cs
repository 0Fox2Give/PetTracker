using System.Text.Json;

namespace PetTracker;
public record CatalogEntry(int Id, string CatalogName, string Name, string Category);
public static class Catalog
{
    public static IReadOnlyList<CatalogEntry> Entries { get; } = Load();

    static List<CatalogEntry> Load()
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.Catalog.json")!;
        return JsonSerializer.Deserialize<List<CatalogEntry>>(stream)!;
    }

    public static Dictionary<int, PetProgress> Create() => Entries.ToDictionary(p => p.Id, p => new PetProgress { Id = p.Id, CatalogName = p.CatalogName, DisplayName = p.Name, Category = p.Category });
}
