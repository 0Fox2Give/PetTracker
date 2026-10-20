namespace PetTracker;
public sealed class PetProgress
{
    public int Id { get; set; }
    public string CatalogName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Category { get; set; } = "";
    public int? Level { get; set; }
    public bool LevelVerified { get; set; }
    public bool LevelDerivedFromSouls { get; set; }
    public string LevelSourceDescription => LevelVerified ? "Rank synchronized from game." : LevelDerivedFromSouls ? "Rank 3 / MAX reached from uninterrupted tracked soul progress." : "Rank entered manually.";
    public int? Souls { get; set; }
    public int? Goal { get; set; }
    public int? EstimatedSouls { get; set; }
    public bool ProgressNeedsSync { get; set; }
    public DateTimeOffset? SyncedUtc { get; set; }
    public DateTimeOffset? CollectionImportedUtc { get; set; }
    public bool CollectionCounterPresent { get; set; }
    public bool CounterVerified { get; set; }
    public int? CurrentSouls => EstimatedSouls ?? Souls;
    public string ProgressText => Max ? "MAX" : (EstimatedSouls.HasValue ? "≈" : "") + (CurrentSouls?.ToString() ?? "?") + "/" + (Goal?.ToString() ?? "?");
    public bool RankIncreasePending => ProgressNeedsSync && Level is 0 or 1 && Goal == PetStages.CounterGoal(Level.Value) && CurrentSouls is int count && count >= Goal;
    public string Notes { get; set; } = "";
    public bool Favorite { get; set; }
    public bool Max => Level >= 3;
    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? CatalogName : DisplayName;
}

public sealed class CreatureKills
{
    public int NpcId { get; set; }
    public string Name { get; set; } = "";
    public long Total { get; set; }
    public int? PetId { get; set; }
    public bool LinkManuallySet { get; set; }
    public string LinkSource { get; set; } = "";
}

public sealed class Profile
{
    public int Version { get; set; } = 1;
    public string? AccountKey { get; set; }
    public string CharacterName { get; set; } = "";
    public Dictionary<int, PetProgress> Pets { get; set; } = new();
    public Dictionary<int, CreatureKills> Creatures { get; set; } = new();
}

public sealed class EntityLife
{
    public int NpcId;
    public string Name = "";
    public bool Eligible, Participated, Ended;
    public long SpawnMs;
}

public sealed record KillObservation(int NpcId, string Name);
public sealed record RecentPetActivity(
    int? PetId,
    int? NpcId,
    string CreatureName,
    DateTimeOffset SeenUtc,
    bool IsNearby = false,
    DateTimeOffset? SoulRewardUtc = null);
public sealed record PetRankIncrease(int PetId, int Rank, DateTimeOffset SeenUtc);
