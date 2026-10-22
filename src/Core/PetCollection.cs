namespace PetTracker;

public enum CollectionRankFilter { All, Uncollected, RankOne, RankTwo, Maxed, Unknown, HideMaxed }
public enum CollectionSort { Name, ClosestToNextRank }
public sealed record CollectionFilters(string Family = "", CollectionRankFilter Rank = CollectionRankFilter.All, string Region = "", CollectionSort Sort = CollectionSort.Name);
public sealed record CollectionSummary(int Total, int Collected, int Maxed, int Unknown);

// Read-only views of the selected collection. Catalog entries without a known
// rank are unknown, never assumed to be uncollected.
public static class PetCollection
{
    public static readonly IReadOnlyList<string> Families = new[] { "Cogni", "Fera", "Natura", "Varian", "Special" };

    public static int? KnownRank(PetProgress? pet) => pet != null &&
        (pet.LevelVerified || pet.LevelDerivedFromSouls && pet.Max) && pet.Level is >= 0 and <= 3 ? pet.Level : null;

    public static int? SoulsRemaining(PetProgress? pet)
    {
        if (pet == null || KnownRank(pet) is not (0 or 1 or 2) || pet.ProgressNeedsSync ||
            !pet.CounterVerified || pet.EstimatedSouls.HasValue || pet.Souls is not >= 0 ||
            pet.Goal != PetStages.CounterGoal(pet.Level!.Value))
            return null;
        return Math.Max(0, pet.Goal!.Value - pet.Souls.Value);
    }

    public static CollectionSummary Summarize(IEnumerable<PetSearchResult> entries, Profile profile)
    {
        var ranks = entries.Select(e => KnownRank(profile.Pets.GetValueOrDefault(e.Pet.Id))).ToArray();
        return new(ranks.Length, ranks.Count(r => r > 0), ranks.Count(r => r == 3), ranks.Count(r => r == null));
    }

    public static IEnumerable<PetSearchResult> Select(IEnumerable<PetSearchResult> entries, Profile profile, CollectionFilters filters, bool collectionSynced)
    {
        var matches = entries.Where(e => (filters.Family.Length == 0 || PetFamily.Label(e.Pet.Id) == filters.Family) &&
            (filters.Region.Length == 0 || e.AvailableMaps.Any(m => m.Region == filters.Region)) &&
            MatchesRank(KnownRank(profile.Pets.GetValueOrDefault(e.Pet.Id)), filters.Rank));
        return filters.Sort == CollectionSort.ClosestToNextRank
            ? matches.OrderBy(e => collectionSynced ? SoulsRemaining(profile.Pets.GetValueOrDefault(e.Pet.Id)) ?? int.MaxValue : int.MaxValue)
                .ThenBy(e => e.Pet.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Pet.Id)
            : matches.OrderBy(e => e.Pet.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Pet.Id);
    }

    static bool MatchesRank(int? rank, CollectionRankFilter filter) => filter switch
    {
        CollectionRankFilter.Uncollected => rank == 0,
        CollectionRankFilter.RankOne => rank == 1,
        CollectionRankFilter.RankTwo => rank == 2,
        CollectionRankFilter.Maxed => rank == 3,
        CollectionRankFilter.Unknown => rank == null,
        CollectionRankFilter.HideMaxed => rank != 3,
        _ => true
    };
}
