using System.Text.Json;

namespace PetTracker;

static class FarmingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var session = new FarmingSession();
        var time = DateTimeOffset.Parse("2026-10-22T12:00:00Z");
        session.Rank(1002, 2);
        check(session.View().RankedPets == 0 && session.View().StartedUtc == null, "Login ranks never start or fill a farming session");
        session.Reward(1002, 0, time, true);
        session.Reward(1002, 76, time, true);
        check(session.View().Souls == 0, "Invalid reward amounts cannot enter session totals");
        session.Reward(1002, 2, time, true);
        session.Reward(1002, 3, time.AddSeconds(1), true);
        session.Rank(1002, 2);
        session.Rank(1002, 2);
        check(session.View().Souls == 5 && session.View().RankedPets == 1 && session.View().Pets.Single().RankUps == 1 && session.View().StartedUtc == time, "Rewards sum without counting repeated rank announcements or moving session start");
        session.Interrupt();
        session.Reward(1004, 1, time.AddMinutes(1), true);
        check(session.View().Incomplete && session.View().Souls == 6, "Capture gap preserves observed totals and marks them incomplete");
        session.Reset();
        session.Reward(1002, 1, time, false);
        check(session.View().Incomplete && session.View().Souls == 1, "Unsynced rewards remain observed, incomplete activity");
        session.Reset();
        check(session.View().Pets.Count == 0 && !session.View().Incomplete && session.View().StartedUtc == null, "Reset removes all session state");

        var pet = new PetProgress { Id = 1002, Level = 0, LevelVerified = true, Souls = 3, Goal = 5, CounterVerified = true, TargetRank = 3 };
        check(FarmingGoals.Remaining(pet, true) == 102, "Goal crosses unlock, rank one and rank two costs without treating counters as cumulative");
        pet.Level = 1;
        pet.Souls = 20;
        pet.Goal = 25;
        check(FarmingGoals.Remaining(pet, true) == 80, "Rank-one goal includes the later 75-soul stage");
        check(FarmingGoals.Remaining(pet, false) == null, "Offline totals do not give exact goal distances");
        pet.ProgressNeedsSync = true;
        check(FarmingGoals.Remaining(pet, true) == null, "Stale pet totals require sync even on a live connection");
        pet.ProgressNeedsSync = false;
        pet.CounterVerified = false;
        check(FarmingGoals.Remaining(pet, true) == null, "Manual counter edits do not give verified goal distances");
        pet.EstimatedSouls = 22;
        pet.CollectionImportedUtc = time;
        check(FarmingGoals.Remaining(pet, true) == 78 && FarmingGoals.Describe(pet, true).StartsWith("About"), "Tracked estimates remain labelled in goals");
        pet.Level = 3;
        pet.LevelVerified = false;
        pet.LevelDerivedFromSouls = false;
        check(!FarmingGoals.Complete(pet, true), "Manual MAX does not complete goals");
        pet.LevelDerivedFromSouls = true;
        check(FarmingGoals.Complete(pet, true) && !FarmingGoals.Complete(pet, false), "Derived MAX completes only a synced goal");
        var profile = new Profile { Pets = new() { [1002] = new() { Id = 1002, TargetRank = 2 } } };
        var restored = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(profile))!;
        Tracker.Validate(restored);
        check(restored.Pets[1002].TargetRank == 2, "Goal persists in its account profile");
        restored.Pets[1002].TargetRank = 4;
        bool rejected = false;
        try
        { Tracker.Validate(restored); }
        catch (InvalidDataException) { rejected = true; }
        check(rejected, "Out-of-range goals fail validation");

        var settings = new AppSettings { FinderPresets = new() { new("Siel", "spirit", "Natura", 2, "Verteron", 1, 0), new("siel"), new("Bad", Rank: 99), new("Null", Query: null!) } };
        settings.Normalize();
        check(settings.FinderPresets.Count == 1, "Presets reject malformed fields and duplicate names");
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        loaded.Normalize();
        check(loaded.FinderPresets.Single() == settings.FinderPresets.Single(), "All preset choices survive settings reload");
        settings.FinderPresets = Enumerable.Range(0, 20).Select(i => new FinderPreset("Filter " + i)).ToList();
        settings.Normalize();
        check(settings.FinderPresets.Count == 10, "Saved filters have a bounded size");
        check(SettingsPatch.Apply(settings, SettingsSections.Defaults("Overlay")).FinderPresets.Count == 10, "Appearance reset preserves saved searches");

        check(FarmingAtlas.Regions.Count == 2 && FarmingAtlas.Regions.Sum(r => r.Points.Count) == 10363, "Atlas contains the two audited regions");
        foreach (var region in FarmingAtlas.Regions)
        {
            check(region.Points.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1), "Map coordinates stay inside normalized grid");
            check(region.Tiles.All(t => t.Column >= 0 && t.Row >= 0 && t.Column < region.Grid && t.Row < region.Grid), "Tile indexes remain within each region");
            check(region.Points.All(p => PetFinder.Directory.Rows.Any(r => r.PetId == p.PetId && r.SoulSources!.Any(s => s.NpcId == p.NpcId && s.Region == region.Name))), "Each map point has an audited regional pet-to-mob source");
        }
        var verteron = FarmingAtlas.Regions[0];
        var anchor = verteron.Points.First(p => p.NpcId == 2100075);
        check(anchor.X == .476903 && anchor.Y == .5141284, "Known calibrated point retains axis order and normalization");
        profile = new Profile { Pets = Catalog.Create() };
        profile.Pets[1002].Level = 3;
        check(FarmingAtlas.Pets(verteron, profile, "", "", true).Any(p => p.Pet.Id == 1002), "Map keeps unverified manual MAX visible");
        profile.Pets[1002].LevelVerified = true;
        check(!FarmingAtlas.Pets(verteron, profile, "", "", true).Any(p => p.Pet.Id == 1002), "Map hides confirmed maxed pets");
        var spirits = FarmingAtlas.Pets(verteron, profile, "spirit", "Natura", false).ToArray();
        check(spirits.Length > 0 && spirits.All(p => PetFamily.Label(p.Pet.Id) == "Natura"), "Map combines search and family filters");
    }
}
