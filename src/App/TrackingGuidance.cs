namespace PetTracker;

// Presentation only: never infer a rank or fill an unknown collection balance.
public static class TrackingGuidance
{
    public const string Sync = "Collection sync is automatic at character login. Leave Pet Tracker running, return to character selection, then log back in. There is no separate sync button. If totals are still missing, check Settings → Help.";
    public static string Remaining(PetProgress? pet)
    {
        if (pet == null || pet.Max || pet.ProgressNeedsSync || !pet.LevelVerified || pet.CurrentSouls is not int souls || souls < 0 || pet.Level is not (0 or 1 or 2) || pet.Goal != PetStages.CounterGoal(pet.Level.Value))
            return "";
        int remaining = Math.Max(0, pet.Goal!.Value - souls);
        return (pet.EstimatedSouls.HasValue ? "Approximately " : "") + remaining + " soul" + (remaining == 1 ? "" : "s") + " remaining " + (pet.Level == 0 ? "to unlock Rank 1." : "to reach Rank " + (pet.Level + 1) + ".");
    }

    public static string Merchant(WorkerView? view, DateTimeOffset now)
    {
        if (view == null || view.Devices == 0)
            return "Merchant tracking needs an active capture connection. Check Settings → Help.";
        if (!view.Settings.BlackMarketAlerts)
            return "Merchant shopping list is turned off.";
        if (!view.CollectionSynced)
            return "Waiting for collection sync. See Settings → Account for login steps.";
        if (!view.Ready)
            return "Waiting for the active character connection. Check Settings → Help.";
        var stocks = view.Market ?? Array.Empty<MarketStock>();
        if (!stocks.Any(s => s.Fresh(now)))
            return "No fresh merchant stock received. Open the merchant’s Special and Pets tabs while tracking. Offers expire after two minutes, at refresh, or when the connection changes.";
        int count = MarketOverlay.Select(view, now).Count;
        return count > 0 ? $"Fresh merchant stock received · {count} eligible pet{(count == 1 ? "" : "s")}. Recently updated pets leave the overlay after 20 seconds." : "Fresh merchant stock received, but no current recommendations. Maxed pets, ordinary world drops and pets without verified progress are excluded. If an expected pet is missing, save a support report in Settings → Help and include its name and your region.";
    }
}
