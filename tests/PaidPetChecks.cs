using PetTracker;
using System.Text.Json;

static class PaidPetChecks
{
    // Audited cash-shop, paid pack and premium-pass pets, independent of the
    // production classification so an omitted flag fails the regression.
    public static readonly int[] Ids = [1001, 1111, 1240, 1278, 1343, 1363];

    public static void Run(Action<bool, string> check)
    {
        check(!PetFinder.Search("White Bunny").Any(p => p.Pet.Id == 1240), "White Bunny is not a soul-farming search result");
        check(PetAcquisitions.Entries.Where(p => p.RealMoneyOnly).Select(p => p.PetId).Order().SequenceEqual(Ids), "All six audited paid-only pets are classified explicitly");
        check(PetAcquisitions.Entries.Where(p => p.Kind is "Cash shop" or "Ultimate Founder's Pack").All(p => p.RealMoneyOnly), "Cash-shop and paid-pack additions require the paid-only flag");
        var profile = new Profile { Pets = Catalog.Create() };
        foreach (int id in Ids)
        {
            var pet = profile.Pets[id];
            pet.Level = 1; pet.LevelVerified = true; pet.Souls = 1;
            pet.Goal = 25; pet.CounterVerified = true;
        }
        string before = JsonSerializer.Serialize(profile);
        var visible = PetFinder.Search("", profile).ToArray();
        check(Ids.All(id => visible.All(p => p.Pet.Id != id)), "Owned paid pets stay excluded after collection sync");
        check(PetCollection.Summarize(visible, profile).Collected == 0, "Paid ownership does not inflate finder collection totals");
        check(Ids.All(id => Catalog.Entries.Any(p => p.Id == id) && profile.Pets.ContainsKey(id)) && JsonSerializer.Serialize(profile) == before,
            "Filtering paid pets preserves decoder IDs and saved ownership/progress");
        foreach (int id in Ids)
        {
            var entry = PetFinder.Entries.Single(p => p.Pet.Id == id);
            check(!entry.VisibleFor(profile) && !BlackMarket.WatchPet(entry), "Paid pet is excluded from discovery and merchant recommendations: " + id);
            check(new[] { entry.Pet.Name }.Concat(entry.Acquisition!.Aliases).All(q => PetFinder.Search(q, profile).All(p => p.Pet.Id != id)),
                "Name and acquisition aliases cannot bypass the paid-pet exclusion: " + id);
            bool rejected = false;
            try { PetPins.Change(new(), id, true); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Paid pets cannot become pinned farming targets: " + id);
            check(!PetPins.Change(new() { PinnedPetId = id }, id, false).IsPinned(id), "An old paid-pet pin remains removable: " + id);
        }
        check(new[] { 1204, 1299, 1112, 1021 }.All(id => visible.Any(p => p.Pet.Id == id)),
            "Launch, Twitch, festival-token and soul-chest pets remain discoverable");

        var now = DateTimeOffset.UtcNow;
        var recent = Ids.Select(id => new RecentPetActivity(id, null, "", now)).ToList();
        var nearby = Ids.Select(id => new NearbyPet(id, "", now)).ToArray();
        var settings = new AppSettings { PinnedPetId = 1240, HideMaxed = false, ExpireInactive = false };
        check(settings.Pins().Count == 0 && settings.CanPin(1021) && !settings.CanPin(1240), "Legacy paid pins consume no visible slots and cannot be added again");
        var tracker = new Tracker("", Catalog.Create());
        tracker.ReadOnlyView(new WorkerView(3, profile, true, "Live", "", "", new(), null, recent, null, now, 1, 0, 0, 0, settings, Nearby: nearby));
        foreach (string mode in new[] { "Recent", "Nearby" })
        {
            settings.TrackerMode = mode;
            check(!Activity.Select(tracker, settings, now).Any(), "Paid activity and legacy pins stay out of the " + mode + " overlay");
        }
        check(JsonSerializer.Serialize(profile) == before, "Overlay filtering does not delete paid collection records");
    }
}
