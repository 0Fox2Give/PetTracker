using System.Text.Json;

namespace PetTracker;

public sealed record FarmingMap(string Region, string Url)
{
    public string Edition => Region is "Verteron" or "Altgard" or "Chaotic Lower Reshanta" ? "Global launch data" : "Asian-release data; Global availability unverified";
    public bool Global => Region is "Verteron" or "Altgard" or "Chaotic Lower Reshanta";
}

public sealed record FarmingMob(int NpcId, string Name, string Region);
public sealed record FarmingLink(int PetId, string? SourceUrl, List<FarmingMap> Maps, List<FarmingMob>? SoulSources = null);
public sealed record FarmingDirectory(string VerifiedUtc, string Provider, List<FarmingLink> Rows);
public sealed record PetSearchResult(CatalogEntry Pet, IReadOnlyList<string> Creatures, FarmingLink? Location)
{
    public string Label => Pet.Name;
    public PetAcquisition? Acquisition => PetAcquisitions.Find(Pet.Id);
    public IReadOnlyList<FarmingMap> AvailableMaps => Location?.Maps.Where(m => m.Global).ToArray() ?? Array.Empty<FarmingMap>();

    // Presentation gate only. Missing source coverage is not proof of unreleased
    // content. Keep decoder/progress records, but paid-only pets are not farming
    // targets even when the selected character owns one.
    public bool VisibleFor(Profile? profile) => !PetAcquisitions.IsRealMoneyOnly(Pet.Id) &&
        (AvailableMaps.Count > 0 || Acquisition != null || (profile?.Pets.TryGetValue(Pet.Id, out var progress) == true && (progress.Level > 0 || progress.CurrentSouls > 0)));
}

public static class PetFinder
{
    public static FarmingDirectory Directory { get; } = Load();
    public static IReadOnlyList<PetSearchResult> Entries { get; } = Build();

    static FarmingDirectory Load()
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.FarmingLinks.json")!;
        return JsonSerializer.Deserialize<FarmingDirectory>(stream)!;
    }

    static IReadOnlyList<PetSearchResult> Build()
    {
        var catalog = Catalog.Create();
        var mapping = new CreaturePetMappings("", catalog);
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.PetSources.json")!;
        using var doc = JsonDocument.Parse(stream);
        var creatures = new Dictionary<int, HashSet<string>>();
        foreach (var row in doc.RootElement.GetProperty("Rows").EnumerateArray())
        {
            int npc = row.GetProperty("NpcId").GetInt32();
            var match = mapping.Find(npc);
            if (match == null)
                continue;
            if (!creatures.TryGetValue(match.PetId, out var names))
                creatures[match.PetId] = names = new(StringComparer.OrdinalIgnoreCase);
            names.Add(mapping.CreatureName(npc));
        }

        return Catalog.Entries.Where(p => !p.Name.StartsWith("Pet #", StringComparison.Ordinal) && p.CatalogName != "TestDummy").OrderBy(p => p.Name).Select(p =>
        {
            var location = Directory.Rows.FirstOrDefault(r => r.PetId == p.Id);
            // Discovery uses audited release-specific sources, not every NPC the decoder
            // knows. An empty audited list deliberately suppresses closed-region names.
            var names = location?.SoulSources?.Select(s => s.Name).Distinct().OrderBy(n => n).ToArray() ?? creatures.GetValueOrDefault(p.Id)?.OrderBy(n => n).ToArray() ?? Array.Empty<string>();
            return new PetSearchResult(p, names, location);
        }).ToArray();
    }

    public static IEnumerable<PetSearchResult> Search(string query, Profile? profile = null)
    {
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Entries.Where(p => p.VisibleFor(profile) && words.All(w => p.Pet.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || p.Creatures.Any(n => n.Contains(w, StringComparison.OrdinalIgnoreCase)) || (p.Location?.SoulSources?.Any(s => s.Region.Contains(w, StringComparison.OrdinalIgnoreCase)) ?? false) || (p.Acquisition?.Aliases.Any(n => n.Contains(w, StringComparison.OrdinalIgnoreCase)) ?? false)));
    }

    public static bool AllowedUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "aion2maps.com" && uri.IsDefaultPort && uri.UserInfo.Length == 0;
    public static bool AllowedSourceUrl(string url) => AllowedUrl(url) || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && ((uri.Host == "lounge.plaync.com" && uri.AbsolutePath.StartsWith("/feed/", StringComparison.Ordinal)) || (uri.Host == "corpus.gg" && uri.AbsolutePath.StartsWith("/games/aion-2/collections/", StringComparison.Ordinal))));
}
