using System.Text.Json;

namespace PetTracker;

static class PetCollectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var entries = PetFinder.Search("").Where(e => PetFamily.Label(e.Pet.Id) == "Fera" && e.AvailableMaps.Any(m => m.Region == "Verteron")).Take(6).ToArray();
        var profile = new Profile { Pets = Catalog.Create() };
        void Set(int index, int rank, int souls) => profile.Pets[entries[index].Pet.Id] = new()
        {
            Id = entries[index].Pet.Id, Level = rank, LevelVerified = true, Souls = souls,
            Goal = PetStages.CounterGoal(rank), CounterVerified = true
        };
        Set(0, 0, 3);
        Set(1, 1, 24);
        Set(2, 2, 25);
        Set(3, 3, 0);
        Set(5, 2, 74);
        profile.Pets[entries[5].Pet.Id].LevelVerified = false;
        var before = JsonSerializer.Serialize(profile);
        var summary = PetCollection.Summarize(entries, profile);
        check(summary == new CollectionSummary(6, 3, 1, 2), "Collection counts distinguish uncollected, owned, maxed, manual and unknown ranks");
        var closest = new CollectionFilters(Sort: CollectionSort.ClosestToNextRank);
        check(PetCollection.Select(entries, profile, closest, true).Take(3).Select(e => e.Pet.Id).SequenceEqual(new[] { entries[1].Pet.Id, entries[0].Pet.Id, entries[2].Pet.Id }), "Closest rank sorts actual stage balances, including unlocks, ahead of maxed and unknown pets");
        check(PetCollection.Select(entries, profile, closest, false).SequenceEqual(entries), "An unsynced collection does not rank saved balances as current farming targets");
        check(PetCollection.Select(entries, profile, new("Fera", CollectionRankFilter.RankTwo, "Verteron"), true).Single() == entries[2], "Family, rank and region filters compose without admitting manually entered ranks");
        check(!PetCollection.Select(entries, profile, new("Cogni"), true).Any(), "Family filtering excludes other families");
        check(!PetCollection.Select(entries, profile, new(Region: "Unsupported region"), true).Any(), "Region filtering requires a supported map destination");
        check(PetCollection.Select(entries, profile, new(Rank: CollectionRankFilter.Uncollected), true).Single() == entries[0], "Missing pets are verified rank zero, not every unknown catalog row");
        check(PetCollection.Select(entries, profile, new(Rank: CollectionRankFilter.Unknown), true).Count() == 2, "Unknown and manual ranks remain discoverable in their own filter");
        check(JsonSerializer.Serialize(profile) == before, "Collection browsing never changes saved progress");
        var pet = profile.Pets[entries[1].Pet.Id];
        pet.ProgressNeedsSync = true;
        check(PetCollection.SoulsRemaining(pet) == null && PetCollection.Select(entries, profile, closest, true).First() == entries[0], "A stale near-complete pet cannot displace a verified farming target");
        pet.ProgressNeedsSync = false;
        pet.EstimatedSouls = 24;
        check(PetCollection.SoulsRemaining(pet) == null, "Estimated counters do not get an exact next-rank recommendation");
        pet.EstimatedSouls = null;
        pet.CounterVerified = false;
        check(PetCollection.SoulsRemaining(pet) == null, "Unverified counters do not get ranked");
        pet.CounterVerified = true;
        pet.Goal = 75;
        check(PetCollection.SoulsRemaining(pet) == null, "An inconsistent rank and goal cannot produce a next-rank distance");
        pet.Goal = 25;
        pet.Souls = -1;
        check(PetCollection.SoulsRemaining(pet) == null, "Negative balances are excluded instead of normalized into a recommendation");
        pet.Souls = 25;
        check(PetCollection.SoulsRemaining(pet) == 0, "A verified completed stage reports zero left without advancing the rank");
        pet.ProgressNeedsSync = true;
        check(PetCollection.SoulsRemaining(pet) == null, "A pending rank transition is not advertised as ready");
        var otherAccount = new Profile { Pets = Catalog.Create() };
        check(PetCollection.Summarize(entries, otherAccount) == new CollectionSummary(6, 0, 0, 6), "Changing accounts does not carry collection counts or rank guesses");
    }
}
