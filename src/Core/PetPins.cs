namespace PetTracker;
public static class PetPins
{
    // Executed against the worker's latest settings under its command lock.
    // Separate add/remove actions make repeated requests idempotent.
    public static AppSettings Change(AppSettings current, int? petId, bool pinned)
    {
        if (petId is not int id || !Catalog.Entries.Any(p => p.Id == id))
            throw new InvalidDataException("Unknown pet.");
        var next = SettingsPatch.Apply(current, SettingsPatch.Fields());
        var pins = next.PinnedPetIds!;
        if (pinned && !pins.Contains(id))
        {
            if (pins.Count >= next.PinLimit)
                throw new InvalidOperationException("Pin limit reached. Unpin a pet or raise the limit in Settings > Tracking.");
            pins.Add(id);
        }
        else if (!pinned)
            pins.Remove(id);
        next.Normalize();
        return next;
    }

    public static string Hint(AppSettings settings, int id) => settings.IsPinned(id) ? "Remove this pet from your pinned list." : settings.CanPin(id) ? $"Keep this pet at the top of the tracker ({settings.Pins().Count}/{settings.PinLimit} pinned)." : "Unpin a pet or raise the limit in Settings > Tracking.";
}
