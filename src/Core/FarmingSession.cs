namespace PetTracker;

public sealed record FarmingSessionRow(int PetId, long Souls, int RankUps);
public sealed record FarmingSessionView(DateTimeOffset? StartedUtc, bool Incomplete, IReadOnlyList<FarmingSessionRow> Pets)
{
    public long Souls => Pets.Sum(p => p.Souls);
    public int RankedPets => Pets.Count(p => p.RankUps > 0);
}

// A session measures observed rewards, never kills or differences between login snapshots.
// It is intentionally transient and belongs to one connected character.
public sealed class FarmingSession
{
    readonly Dictionary<int, long> souls = new();
    readonly Dictionary<int, HashSet<int>> ranks = new();
    DateTimeOffset? started;
    bool incomplete;

    public void Reward(int id, int amount, DateTimeOffset at, bool reliable)
    {
        if (amount is <= 0 or > 75)
            return;
        started ??= at;
        incomplete |= !reliable;
        souls[id] = checked(souls.GetValueOrDefault(id) + amount);
    }

    public void Rank(int id, int rank)
    {
        if (!souls.ContainsKey(id) || rank is < 1 or > 3)
            return;
        if (!ranks.TryGetValue(id, out var seen))
            ranks[id] = seen = new();
        seen.Add(rank);
    }

    public void Interrupt() { if (started != null) incomplete = true; }
    public void Reset() { souls.Clear(); ranks.Clear(); started = null; incomplete = false; }
    public FarmingSessionView View() => new(started, incomplete,
        souls.OrderByDescending(p => p.Value).ThenBy(p => p.Key)
            .Select(p => new FarmingSessionRow(p.Key, p.Value, ranks.GetValueOrDefault(p.Key)?.Count ?? 0)).ToArray());
}

public static class FarmingGoals
{
    public static bool Complete(PetProgress pet, bool synced) => synced && pet.TargetRank is int target && PetCollection.KnownRank(pet) >= target;

    public static int? Remaining(PetProgress pet, bool synced)
    {
        if (!synced || pet.TargetRank is not int target || PetCollection.KnownRank(pet) is not int rank)
            return null;
        if (rank >= target)
            return 0;
        if (pet.ProgressNeedsSync || (!pet.CounterVerified && !(pet.EstimatedSouls.HasValue && pet.CollectionImportedUtc.HasValue)) || pet.CurrentSouls is not int count || pet.Goal is not int goal || goal != PetStages.CounterGoal(rank))
            return null;
        int remaining = Math.Max(0, goal - count);
        for (int stage = rank + 1; stage < target; stage++)
            remaining += PetStages.CounterGoal(stage) ?? 0;
        return remaining;
    }

    public static string Describe(PetProgress pet, bool synced)
    {
        if (pet.TargetRank is not int target)
            return "No farming goal";
        if (Complete(pet, synced))
            return $"Rank {target} goal reached";
        if (Remaining(pet, synced) is not int left)
            return $"Goal: Rank {target} · waiting for collection sync";
        string estimate = pet.EstimatedSouls.HasValue ? "About " : "";
        return $"{estimate}{left} soul{(left == 1 ? "" : "s")} to Rank {target}";
    }
}
