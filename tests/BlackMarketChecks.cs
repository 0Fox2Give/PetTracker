using System.Buffers.Binary;
using System.Text.Json;
using PetTracker;

static class BlackMarketChecks
{
    public static void Run(Action<bool, string> check, string root, Func<string, int, byte[]> identity, Func<Dictionary<int, int>, Dictionary<int, int>, byte[]> snapshot, Func<byte, byte, byte[], byte[]> frame)
    {
        using var resource = typeof(BlackMarketChecks).Assembly.GetManifestResourceStream("BlackMarket.json")!;
        using var doc = JsonDocument.Parse(resource);
        var samples = doc.RootElement.EnumerateArray().ToArray();
        var when = DateTimeOffset.Parse(samples[0].GetProperty("Utc").GetString()!);
        byte[] special = Convert.FromHexString(samples[0].GetProperty("Body").GetString()!);
        byte[] pets = Convert.FromHexString(samples[1].GetProperty("Body").GetString()!);
        var first = BlackMarket.Decode(0x91, 0x56, special, when)!;
        var second = BlackMarket.Decode(0x91, 0x56, pets, DateTimeOffset.Parse(samples[1].GetProperty("Utc").GetString()!))!;
        check(first.TabName == "Special" && first.Offers.Count == 2 && first.Offers.Any(o => o.PetId == 1196 && o.ItemId == 534760190 && o.Price == 80000) && first.Offers.Any(o => o.PetId == 1092 && o.Price == 90000), "Sanitized Special tab fixture resolves both exact pet unlock IDs and visible prices, excluding gear");
        var expected = new Dictionary<int, long> { { 1133, 90000 }, { 1004, 90000 }, { 1028, 90000 }, { 1164, 90000 }, { 1082, 80000 }, { 1188, 50000 }, { 1197, 90000 }, { 1219, 50000 }, { 1046, 70000 }, { 1217, 70000 } };
        check(second.TabName == "Pets" && second.Offers.Count == 10 && second.Offers.All(o => expected[o.PetId] == o.Price), "Sanitized Pets tab fixture matches every visible pet and discounted price");
        check(second.RefreshUtc == DateTimeOffset.Parse("2030-01-01T08:00:00Z"), "Fixture refresh epoch decodes to the expected time");
        check(BlackMarket.Items.Count == 199 && BlackMarket.Items.Select(i => i.ItemId).Distinct().Count() == 199 && BlackMarket.Items.All(i => Catalog.Entries.Any(p => p.Id == i.PetId)), "All unlock mappings are unique exact item IDs referencing known pets");
        bool Reject(byte[] b) => BlackMarket.Decode(0x91, 0x56, b, when) == null;
        check(Enumerable.Range(0, special.Length).All(n => Reject(special[..n])) && Reject(special.Concat(new byte[] { 0 }).ToArray()), "Every truncated response and appended bytes fail closed");
        foreach (int index in new[] { 0, 1, 36, 173, 310, 353 })
        {
            var changed = special.ToArray();
            changed[index] ^= 0xff;
            check(Reject(changed), "Reject unsupported shop shape at byte " + index);
        }
        var invalid = special.ToArray();
        invalid[19] = 0;
        check(Reject(invalid), "Unknown currency does not acquire a Kina price label");
        invalid = special.ToArray();
        invalid[28]++;
        check(Reject(invalid), "Inconsistent discount rejects the whole batch");
        invalid = special.ToArray();
        special.AsSpan(2, 4).CopyTo(invalid.AsSpan(37, 4));
        check(Reject(invalid), "Duplicate offer identity rejects ambiguous stock");
        invalid = special.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(invalid.AsSpan(10), 2);
        check(Reject(invalid), "Unsupported multi-quantity offer is not extrapolated");
        invalid = special.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(6), 534660193);
        check(BlackMarket.Decode(0x91, 0x56, invalid, when)!.Offers.All(o => o.PetId != 1196), "Soul item ID is never mistaken for an unlock item");
        check(BlackMarket.Decode(0x91, 0x56, special, when.AddHours(2)) == null && BlackMarket.Decode(0x91, 0x56, special, when.AddDays(-1)) == null && BlackMarket.Decode(0x90, 0x56, special, when) == null && BlackMarket.Decode(0x91, 0x55, special, when) == null, "Wrong message, expired response and implausible refresh time are rejected");
        var profile = new Profile { Pets = Catalog.Create() };
        foreach (var p in profile.Pets.Values)
        {
            p.Level = 1;
            p.LevelVerified = true;
            p.ProgressNeedsSync = false;
        }
        string before = JsonSerializer.Serialize(profile);
        check(BlackMarket.Watchlist("", profile).Count() == 42 && BlackMarket.Watchlist("Elite Dratona", profile).Any() && !BlackMarket.Watchlist("Lesser Wind Spirit", profile).Any(), "Watchlist includes chest and dungeon pets but excludes ordinary world drops");
        foreach (int id in new[] { 1058, 1077, 1091, 1093, 1215 })
        {
            var item = BlackMarket.Items.Single(i => i.PetId == id);
            var dungeonStock = first with { Offers = new[] { new MarketOffer(1, item.ItemId, id, 80000) } };
            check(BlackMarket.Recommendations(dungeonStock, profile, true, when).Single().PetId == id, "Dungeon pet offered by merchant is recommended: " + id);
            profile.Pets[id].Level = 3;
            check(!BlackMarket.Recommendations(dungeonStock, profile, true, when).Any(), "Maxed dungeon pet is excluded: " + id);
            profile.Pets[id].Level = 1;
        }
        var noCoverage = PetFinder.Entries.Single(p => p.Pet.Id == 1196) with
        {
            Location = null
        };
        check(!BlackMarket.WatchPet(noCoverage), "Missing location coverage cannot establish shop priority");
        check(BlackMarket.Recommendations(first, profile, true, when).Select(o => o.PetId).SequenceEqual(new[] { 1196 }) && !BlackMarket.Recommendations(first, profile, false, when).Any(), "Only needed non-farmable pets alert, and only against a synchronized collection");
        check(JsonSerializer.Serialize(profile) == before, "Reading market stock and recommendations cannot alter soul totals or collection state");
        var view = new WorkerView(3, profile, true, "Live", "", "Test", new(), "account-a", new(), null, when, 1, 0, 0, 0, new(), Market: new[] { first }, CollectionSynced: true);
        check(MarketOverlay.Select(view, when).Count == 1 && MarketOverlay.Select(view, when.AddSeconds(1)).Count == 1, "Fresh recommendations remain visible across repeated polls");
        var display = new MerchantDisplay();
        check(!display.Select(view, when).Single().Updated, "Fresh offer starts as a shopping recommendation");
        var rewardTime = when.AddSeconds(2);
        var rewarded = view with
        {
            Recent = new() { new(1196, null, "", rewardTime, SoulRewardUtc: rewardTime) },
            Market = Array.Empty<MarketStock>()
        };
        check(display.Select(rewarded, rewardTime).Single().Updated, "Observed reward retains the displayed offer when purchase traffic clears stock");
        check(display.Select(rewarded, when.AddSeconds(21)).Single().Updated, "Updated row remains visible for nineteen seconds");
        check(display.Select(rewarded, when.AddSeconds(22)).Count == 0, "Updated row leaves the shopping list after exactly twenty seconds");
        check(display.Select(rewarded with
        {
            Market = view.Market
        }, when.AddSeconds(23)).Count == 0, "Old stock cannot recommend an updated pet again after expiry");
        check(display.Select(rewarded with
        {
            Market = new[] { first with { SeenUtc = when.AddSeconds(24) } }
        }, when.AddSeconds(24)).Count == 0, "Reopening the same stock cycle cannot resurrect the completed recommendation");
        check(!display.Select(rewarded with
        {
            Market = new[] { first with { SeenUtc = when.AddSeconds(25), RefreshUtc = first.RefreshUtc.AddHours(1) } }
        }, when.AddSeconds(25)).Single().Updated, "A new shop refresh can recommend the pet again without replaying the prior reward");
        var noReward = new MerchantDisplay();
        noReward.Select(view, when);
        check(!noReward.Select(view with
        {
            Recent = new() { new(1196, 123, "Mob", rewardTime) }
        }, rewardTime).Single().Updated, "Kill activity is not treated as a fulfilled merchant recommendation");
        check(noReward.Select(view with
        {
            Market = Array.Empty<MarketStock>()
        }, rewardTime).Count == 0, "Stock disappearing alone does not manufacture purchase confirmation");
        var threeStock = first with { Offers = new[] { 1196, 1058, 1093 }.Select((id, index) => new MarketOffer(index + 1, BlackMarket.Items.Single(i => i.PetId == id).ItemId, id, 80000)).ToArray() };
        foreach (string mode in new[] { "Recent", "Nearby" })
        {
            var threeView = view with { Market = new[] { threeStock }, Settings = new() { TrackerMode = mode } };
            var threeDisplay = new MerchantDisplay();
            check(threeDisplay.Select(threeView, when).Count == 3, mode + " shows three merchant recommendations");
            threeView = threeView with { Recent = rewarded.Recent };
            var afterPurchase = threeDisplay.Select(threeView, rewardTime);
            check(afterPurchase.Count == 3 && afterPurchase.Count(r => r.Updated) == 1, mode + " retains unbought pets alongside purchase feedback");
            check(threeDisplay.Select(threeView, rewardTime.AddSeconds(20)).Select(r => r.Offer.PetId).ToHashSet().SetEquals(new[] { 1058, 1093 }), mode + " removes only the rewarded pet after twenty seconds");
        }
        var maxDisplay = new MerchantDisplay();
        maxDisplay.Select(view, when);
        var priorLevel = profile.Pets[1196].Level;
        profile.Pets[1196].Level = 3;
        check(maxDisplay.Select(rewarded, rewardTime).Single().Updated, "Final-rank purchase feedback survives normal maxed-pet exclusion");
        profile.Pets[1196].Level = priorLevel;
        check(maxDisplay.Select(rewarded with
        {
            CollectionSynced = false
        }, rewardTime).Count == 0, "Capture or collection uncertainty clears retained purchase feedback");
        var switched = new MerchantDisplay();
        switched.Select(view, when);
        switched.Select(rewarded, rewardTime);
        check(switched.Select(rewarded with
        {
            Character = "Other"
        }, rewardTime).Count == 0, "Character switch cannot carry merchant feedback across connections");
        check(MarketOverlay.Select(null, when).Count == 0 && MarketOverlay.Select(view with
        {
            Selected = null
        }, when).Count == 0 && MarketOverlay.Select(view with
        {
            CollectionSynced = false
        }, when).Count == 0 && MarketOverlay.Select(view, when.AddMinutes(2)).Count == 0, "Missing identity, synchronization or fresh stock hides recommendations");
        var both = view with
        {
            Market = new[] { first, first with { Tab = 104 } }
        };
        check(MarketOverlay.Select(both, when).Count == 1, "The same pet in Special and Pets alerts only once");
        check(MarketOverlay.Select(view with
        {
            Selected = "account-b"
        }, when).Count == 1, "Alert dedup is scoped to the selected account");
        check(MarketOverlay.Select(view with
        {
            Settings = new()
            {
                BlackMarketAlerts = false
            }
        }, when).Count == 0 && MarketOverlay.Select(view with
        {
            Ready = false
        }, when).Count == 0, "Disabled alerts and disconnected views remain silent");
        foreach (var state in new[] { "max", "unknown", "stale", "manual" })
        {
            profile.Pets[1196].Level = state == "max" ? 3 : state == "unknown" ? null : 1;
            profile.Pets[1196].LevelVerified = state != "manual";
            profile.Pets[1196].ProgressNeedsSync = state == "stale";
            check(MarketOverlay.Select(view, when).Count == 0, "No market recommendation for " + state + " collection state");
        }
        profile.Pets[1196].Level = 2;
        profile.Pets[1196].LevelVerified = true;
        profile.Pets[1196].ProgressNeedsSync = false;
        check(MarketOverlay.Select(view, when).Count == 1, "Owned non-max pet remains eligible for the documented duplicate-to-soul route");
        check(!first.Fresh(when.AddMinutes(2)) && !first.Fresh(when.AddTicks(-1)) && !first.Fresh(first.RefreshUtc), "Stock hides after two minutes, clock rollback, or refresh boundary");
        var current = special.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(current.AsSpan(345), DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeMilliseconds());
        using var engine = new Engine(Path.Combine(root, "black-market"));
        WorkerView State() => engine.View(true, 1, 0, 0, 0, true);
        engine.Feed("unknown", frame(0x91, 0x56, current));
        check(State().Market!.Count == 0, "An unbound game connection cannot supply market offers");
        engine.Feed("game", identity("ShopTester", 0));
        engine.Feed("game", snapshot(new()
        {
            [1196] = 1
        }, new()
        {
            [1196] = 7
        }));
        var initial = JsonSerializer.Serialize(engine.Tracker.Profile);
        engine.Feed("game", frame(0x91, 0x56, current));
        check(State().Market!.Single().Offers.Count == 2 && State().CollectionSynced, "Bound stock flows through WorkerView with collection reliability");
        var transported = JsonSerializer.Deserialize<WorkerView>(JsonSerializer.Serialize(State()))!;
        check(MarketOverlay.Select(transported, DateTimeOffset.UtcNow).Single().Offer.PetId == 1196, "Real engine collection and stock survive IPC serialization and trigger the eligible alert");
        check(JsonSerializer.Serialize(engine.Tracker.Profile) == initial, "End-to-end stock ingestion never modifies progress");
        engine.Feed("game", frame(0x1e, 0x56, new byte[] { 1, 2, 3 }));
        check(State().Market!.Count == 1, "Unrelated recurring 56:1E traffic cannot erase a just-observed shop");
        engine.Feed("game", frame(0x92, 0x56, new byte[] { 1, 2, 3 }));
        check(State().Market!.Count == 1, "An unrecognized adjacent shop message is not evidence that all offers disappeared");
        engine.Feed("game", frame(0x93, 0x56, new byte[] { 0, 0, 104 }));
        check(State().Market!.Count == 0, "Observed tab-open message invalidates preceding offers");
        engine.Feed("game", frame(0x91, 0x56, current));
        engine.Feed("background", frame(0x91, 0x56, current));
        check(State().Market!.Count == 1, "Background connection cannot replace or invalidate the active shop");
        engine.Feed("game", frame(0x91, 0x56, current[..20]));
        check(State().Market!.Count == 0, "Malformed replacement clears previous offers rather than reusing them");
        engine.Feed("game", frame(0x91, 0x56, current));
        engine.Gap("game");
        check(State().Market!.Count == 0 && !State().CollectionSynced, "Capture gap invalidates stock and recommendation reliability");
        engine.Feed("game", frame(0x91, 0x56, current));
        check(MarketOverlay.Select(State(), DateTimeOffset.UtcNow).Count == 0, "New stock after a capture gap cannot bypass collection resynchronization");
        engine.Feed("game", identity("AltShopTester", 0));
        check(State().Market!.Count == 0, "Character changes cannot inherit observed shop offers");
        engine.Feed("game", frame(0x91, 0x56, current));
        engine.StopCapture();
        check(State().Market!.Count == 0, "Capture stop discards observed shop stock");
        check(MarketOverlay.Select(view with
        {
            Market = null,
            CollectionSynced = false
        }, when).Count == 0, "An older worker without optional market fields remains compatible and silent");

        // Reproduce the reported collection/offer state using the supported fixture
        // layout. This does not establish what stock bytes the reporter received.
        var dranaProfile = new Profile { Pets = Catalog.Create() };
        var drana = dranaProfile.Pets[1160];
        drana.Level = 1;
        drana.LevelVerified = true;
        drana.ProgressNeedsSync = false;
        drana.Souls = 5;
        drana.Goal = 25;
        var dranaBody = pets.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(dranaBody.AsSpan(6), 534760186);
        dranaBody[18] = 2;
        BinaryPrimitives.WriteInt64LittleEndian(dranaBody.AsSpan(20), 100000);
        BinaryPrimitives.WriteInt64LittleEndian(dranaBody.AsSpan(28), 70000);
        var reportTime = DateTimeOffset.UtcNow;
        BinaryPrimitives.WriteInt64LittleEndian(dranaBody.AsSpan(345), reportTime.AddMinutes(30).ToUnixTimeMilliseconds());
        var dranaStock = BlackMarket.Decode(0x91, 0x56, dranaBody, reportTime)!;
        check(BlackMarket.Watchlist("Drana Contaminant", dranaProfile).Single().Pet.Id == 1160, "Reported Drana Contaminant at Rank 1 is eligible for the watchlist");
        foreach (var mode in new[] { "Recent", "Nearby" })
        {
            var dranaView = view with { Profile = dranaProfile, Market = new[] { dranaStock }, Settings = new() { TrackerMode = mode } };
            var selected = new MerchantDisplay().Select(dranaView, reportTime);
            check(selected.Count == 1 && selected[0].Offer.PetId == 1160 && selected[0].Offer.Price == 70000, "Supported Drana offer reaches " + mode + " even with no mob activity");
        }

        var reportView = view with { Profile = dranaProfile, Market = new[] { dranaStock } };
        using var merchantReport = JsonDocument.Parse(SupportReport.Create(reportView, "test-version"));
        var merchant = merchantReport.RootElement.GetProperty("Merchant");
        check(merchantReport.RootElement.GetProperty("CollectionSynced").GetBoolean() && merchant.GetProperty("Enabled").GetBoolean() && merchant.GetProperty("FreshStockTabs").GetInt32() == 1 && merchant.GetProperty("MappedOffers").GetInt32() == 10 && merchant.GetProperty("EligiblePets").GetInt32() == 1, "Support report distinguishes received stock from eligible recommendations");
        check(merchant.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] { "EligiblePets", "Enabled", "FreshStockTabs", "MappedOffers" }), "Merchant diagnostics contain only allowlisted counts and the setting, without stock or collection details");
        using var noStockReport = JsonDocument.Parse(SupportReport.Create(reportView with { Market = Array.Empty<MarketStock>() }, "test-version"));
        check(noStockReport.RootElement.GetProperty("Merchant").GetProperty("FreshStockTabs").GetInt32() == 0 && noStockReport.RootElement.GetProperty("Merchant").GetProperty("EligiblePets").GetInt32() == 0, "Support report identifies missing stock even with a synced collection");
        using var noMatchReport = JsonDocument.Parse(SupportReport.Create(reportView with { Market = new[] { dranaStock with { Offers = Array.Empty<MarketOffer>() } } }, "test-version"));
        check(noMatchReport.RootElement.GetProperty("Merchant").GetProperty("FreshStockTabs").GetInt32() == 1 && noMatchReport.RootElement.GetProperty("Merchant").GetProperty("EligiblePets").GetInt32() == 0, "Support report distinguishes decoded stock with no matches from missing stock");
    }
}
