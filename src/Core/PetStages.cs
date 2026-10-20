namespace PetTracker;

// Game counter goals. Captured upgrade deductions confirm that points are
// spent at transitions; rank-2 X/75 is not a cumulative25+50 balance.
public static class PetStages
{
    public const int UnlockCost = 5, RankTwoCost = 25, MaxUpgradeCost = 75;
    public static int? CounterGoal(int ownedRank) => ownedRank switch { 0 => UnlockCost, 1 => RankTwoCost, 2 => MaxUpgradeCost, _ => null };
    public static string UpgradeHint(int? ownedRank) => ownedRank switch
    {
        0 => "Unlock Rank 1: 5 soul points.",
        1 => "Upgrade to Rank 2: 25 soul points.",
        2 => "Upgrade to Rank 3 / MAX: 75 soul points.",
        3 => "Maximum rank.",
        _ => "Rank is not known."
    };
}
