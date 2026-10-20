using PetTracker;
using System.Text.Json;
using System.Buffers.Binary;
static class NearbyChecks
{
    sealed record Fixture(byte Opcode, byte Family, int Npc, string Body);
    sealed record AbyssSource(int NpcId, int PetId, string NpcName, int SoulItemId);
    public static void Run(Action<bool, string> check, string root, Func<string, int, byte[]> identity, Func<byte, byte, byte[], byte[]> frame)
    {
        using var stream = typeof(NearbyChecks).Assembly.GetManifestResourceStream("NearbyAppearances.json")!;
        var fixtures = JsonSerializer.Deserialize<Fixture[]>(stream)!;
        var catalog = Catalog.Create();
        var mappings = new CreaturePetMappings("", catalog);
        int pet = mappings.Find(2100481)!.PetId;
        var allowed = new HashSet<int> { 2100481 };
        foreach (var f in fixtures)
        {
            var bytes = Convert.FromHexString(f.Body);
            var spawn = GameProtocol.Spawn(f.Opcode, f.Family, bytes, allowed);
            check(spawn?.Npc == f.Npc, "Synthetic nonzero NPC appearance flag decodes the exact source mob");
            int p = 0;
            GameProtocol.Var(bytes, ref p, out _);
            var summon = bytes.ToArray();
            summon[p] = 0x3f;
            check(GameProtocol.Appearance(f.Opcode, f.Family, summon) == null, "Summon flag is excluded even with a known pet mob ID");
            check(GameProtocol.Appearance(f.Opcode, 0x38, bytes) == null && GameProtocol.Appearance(f.Opcode, f.Family, bytes[..p]) == null, "Wrong lane and truncated appearance are rejected");
        }
        var data = Convert.FromHexString(fixtures[0].Body);
        var known = GameProtocol.Appearance(0x41, 0x36, data)!;
        int offset = 0;
        GameProtocol.Var(data, ref offset, out _);
        var unknown = data.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(unknown.AsSpan(offset + 3), 2100482);
        check(GameProtocol.Spawn(0x41, 0x36, unknown, allowed) == null, "Nearby never invents a link for an unknown NPC");
        using (var sourceStream = typeof(NearbyChecks).Assembly.GetManifestResourceStream("AbyssSources.json")!)
        {
            var sources = JsonSerializer.Deserialize<AbyssSource[]>(sourceStream)!;
            check(sources.Length == 32 && sources.Select(s => s.NpcId).Distinct().Count() == 32, "Lower Reshanta fixture covers thirty-two distinct drop sources");
            using var abyss = new Engine(Path.Combine(root, "abyss-nearby"));
            abyss.Feed("abyss", identity("Scout", 0));
            string collectionBefore = JsonSerializer.Serialize(abyss.Tracker.Profile);
            foreach (var source in sources)
            {
                check(mappings.Find(source.NpcId)?.PetId == source.PetId && mappings.CreatureName(source.NpcId) == source.NpcName, "Exact Abyss NPC ID resolves to its verified pet and mob name");
                var appearance = data.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(appearance.AsSpan(offset + 3), source.NpcId);
                check(GameProtocol.Appearance(0x41, 0x36, appearance)?.Npc == source.NpcId, "Abyss NPC range is accepted by the existing appearance decoder");
                abyss.Feed("abyss", frame(0x41, 0x36, appearance));
                check(abyss.View(true, 1, 0, 0, 0, true).Nearby!.Single().PetId == source.PetId, "Abyss appearance reaches Nearby before any attack or reward");
                abyss.Feed("abyss", frame(0x42, 0x36, Var(known.Entity).Concat(new byte[] { 0, 3 }).ToArray()));
            }
            check(JsonSerializer.Serialize(abyss.Tracker.Profile) == collectionBefore && abyss.Tracker.CountedKills == 0, "Abyss appearances and unengaged removals never invent collection points");
            check(mappings.Find(2600078) == null, "Dimensional Core is not guessed to be a pet soul source");
            check(PetFinder.Search("Abyss Drakan Scout Beast").Any(p => p.Pet.Id == 1014 && p.AvailableMaps.Any(m => m.Region == "Chaotic Lower Reshanta")), "Abyss mob can be found by name with its verified Global map");
        }
        using var engine = new Engine(Path.Combine(root, "nearby"));
        engine.Execute(new("create", Name: "Scout", Realm: "Test"));
        IReadOnlyList<NearbyPet> Seen() => engine.View(true, 1, 0, 0, 0, true).Nearby!;
        engine.Feed("game", frame(0x41, 0x36, data));
        check(Seen().Count == 0, "Appearance before local identity is not attributed to an account");
        engine.Feed("game", identity("Scout", 0));
        var before = JsonSerializer.Serialize(engine.Tracker.Profile);
        var packet = frame(0x41, 0x36, data);
        engine.Feed("game", packet[..10]);
        check(Seen().Count == 0, "Fragmented appearance does not create a partial mob");
        engine.Feed("game", packet[10..]);
        check(Seen().Single().PetId == pet && Seen().Single().CreatureName == "Sensitive Karnif", "Appearance reaches the worker view before combat");
        check(before == JsonSerializer.Serialize(engine.Tracker.Profile) && engine.Tracker.CountedKills == 0 && engine.Tracker.RecentActivities.Count == 0, "Nearby detection changes no collection totals, kills or recent rewards");
        engine.Feed("game", packet);
        check(Seen().Count == 1, "Duplicate appearance does not duplicate the pet row");
        var removal = frame(0x42, 0x36, Var(known.Entity).Concat(new byte[] { 0, 1 }).ToArray());
        engine.Feed("unrelated", removal);
        check(Seen().Count == 1, "Another connection cannot remove an active mob");
        engine.Gap("unrelated");
        check(Seen().Count == 1, "Background stream gaps preserve nearby mobs");
        engine.Feed("game", removal);
        check(Seen().Count == 0, "Leaving client visibility removes the nearby mob immediately");
        engine.Feed("game", packet);
        engine.Feed("game", frame(0x42, 0x36, Var(known.Entity).Concat(new byte[] { 0, 3 }).ToArray()));
        check(Seen().Count == 0 && engine.Tracker.CountedKills == 0, "An unengaged death removes nearby without inventing a kill");
        engine.Feed("game", packet);
        engine.Feed("game", frame(0x41, 0x36, unknown));
        check(Seen().Count == 0, "Entity reuse by an unmapped NPC removes the old pet association");
        engine.Feed("game", packet);
        engine.Gap("game");
        check(Seen().Count == 0, "Active continuity loss clears all uncertain nearby sightings");
        engine.Feed("game", packet);
        engine.Feed("game", identity("Scout", 0));
        check(Seen().Count == 0, "Fresh local-player/world record clears prior nearby sightings");
        engine.Feed("game", packet);
        engine.StopCapture();
        check(Seen().Count == 0, "Capture restart clears nearby state");
        var world = new NearbyWorld(mappings);
        var now = DateTimeOffset.UtcNow;
        world.Observe(new(1, 2100481), now);
        world.Observe(new(2, 2100481), now);
        check(world.View(now, 120).Count == 1, "Multiple instances of one mob share a pet row");
        world.Remove(1);
        check(world.View(now, 120).Count == 1, "Removing one instance preserves another nearby instance");
        check(world.View(now.AddSeconds(119), 120).Count == 1 && world.View(now.AddSeconds(120), 120).Count == 0, "Stale timeout expires at its configured boundary");
        world.Observe(new(1, 2100481), now);
        world.Refresh(1, now.AddSeconds(100));
        check(world.View(now.AddSeconds(130), 120).Count == 1, "Supported combat refreshes an existing sighting");
        world.Clear();
        world.Refresh(1, now);
        check(world.View(now, 120).Count == 0, "Combat alone cannot create a pet association");
        for (int i = 1; i <= NearbyWorld.Capacity + 1; i++)
            world.Observe(new(i, 2100481), now.AddMilliseconds(i));
        world.Remove(NearbyWorld.Capacity + 1);
        // After removing all newer sightings, the oldest must already have been evicted.
        for (int i = 2; i <= NearbyWorld.Capacity; i++)
            world.Remove(i);
        check(world.View(now.AddSeconds(3), 120).Count == 0, "Nearby world memory is bounded and evicts oldest instances");
        var profile = new Profile { Pets = catalog };
        profile.Pets[pet].Level = 2;
        profile.Pets[pet].Souls = 24;
        profile.Pets[pet].Goal = 75;
        var settings = new AppSettings { TrackerMode = "Nearby", ExpireInactive = true, InactiveSeconds = 20, NearbyStaleSeconds = 120 };
        var view = engine.View(true, 1, 0, 0, 0, true) with
        {
            Ready = true,
            Selected = "preview",
            Character = "Scout",
            Profile = profile,
            Settings = settings,
            Nearby = new[] { new NearbyPet(pet, "Sensitive Karnif", now.AddSeconds(-30)) }
        };
        var tracker = new Tracker("", catalog);
        tracker.ReadOnlyView(JsonSerializer.Deserialize<WorkerView>(JsonSerializer.Serialize(view))!);
        check(Activity.Select(tracker, settings, now).Single().IsNearby, "IPC preserves nearby records and ignores the recent expiry timer");
        profile = tracker.Profile;
        profile.Pets[pet].Level = 3;
        settings.HideMaxed = true;
        check(!Activity.Select(tracker, settings, now).Any() && Activity.EmptyText(tracker, settings, now, view).Contains("Nearby pets are maxed"), "Nearby Hide maxed filter has a clear empty state");
        settings.PinnedPetId = pet;
        check(Activity.Select(tracker, settings, now).Count() == 1, "Explicit pin overrides Hide maxed without duplicate rows");
        settings.PinnedPetId = null;
        settings.TrackerMode = "Recent";
        check(!Activity.Select(tracker, settings, now).Any(), "Nearby sightings do not leak into Recent mode");
        var normalized = SettingsPatch.Apply(new(), SettingsPatch.Fields((nameof(AppSettings.TrackerMode), "Nearby"), (nameof(AppSettings.NearbyStaleSeconds), 300)));
        check(normalized.TrackerMode == "Nearby" && normalized.NearbyStaleSeconds == 300, "Mode and stale timeout save through normal settings patches");
        normalized.TrackerMode = "radar";
        normalized.NearbyStaleSeconds = -1;
        normalized.Normalize();
        check(normalized.TrackerMode == "Recent" && normalized.NearbyStaleSeconds == 60, "Invalid nearby settings return to safe defaults");
        var options = new AppSettings { TrackerMode = "Nearby", NearbyStaleSeconds = 30 };
        var sighting = new RecentPetActivity(pet, null, "Sensitive Karnif", now, true);
        check(Activity.ObservationNotice(sighting, options, now.AddSeconds(14)) == "" && Activity.ObservationNotice(sighting, options, now.AddSeconds(15)) == "Last seen 15s ago", "Observation age changes at the exact freshness boundary");
        check(Activity.ObservationNotice(sighting, options, now.AddSeconds(30)) == "Not currently observed" && Activity.ObservationNotice(sighting, options, now.AddSeconds(-1)) == "Not currently observed", "Expired and future sightings cannot claim current observation");
        tracker.ReadOnlyView(view with
        {
            Settings = options,
            Nearby = new[] { new NearbyPet(pet, "Sensitive Karnif", now) }
        });
        check(Activity.Select(tracker, options, now.AddSeconds(29)).Count() == 1 && !Activity.Select(tracker, options, now.AddSeconds(30)).Any(), "Short timeout removes an unrefreshed row without new worker traffic");
        options.PinnedPetId = pet;
        var expiredPin = Activity.Select(tracker, options, now.AddSeconds(30)).Single();
        check(!expiredPin.IsNearby && Activity.ObservationNotice(expiredPin, options, now.AddSeconds(30)) == "Not currently observed", "An expired pinned sighting stays visible without implying proximity");
        tracker.ReadOnlyView(view with
        {
            Settings = options,
            Nearby = new[] { new NearbyPet(pet, "Sensitive Karnif", now.AddSeconds(31)) }
        });
        check(Activity.Select(tracker, options, now.AddSeconds(31)).Single().IsNearby && Activity.ObservationNotice(Activity.Select(tracker, options, now.AddSeconds(31)).Single(), options, now.AddSeconds(31)) == "", "A renewed sighting clears the pinned absence status");
        foreach (int seconds in new[] { 15, 30, 60, 120, 300 })
            check(SettingsPatch.Apply(new(), SettingsPatch.Fields((nameof(AppSettings.NearbyStaleSeconds), seconds))).NearbyStaleSeconds == seconds, "All supported observation timeouts survive settings patches");
        check(SettingsPatch.Apply(new()
        {
            NearbyStaleSeconds = 300
        }, SettingsSections.Defaults("Tracking")).NearbyStaleSeconds == 60, "Tracking reset includes the observation timeout at its new location");
        var oldSettings = JsonSerializer.Deserialize<AppSettings>("{\"NearbyStaleSeconds\":120}")!;
        oldSettings.Normalize();
        check(oldSettings.NearbyStaleSeconds == 120 && new AppSettings().NearbyStaleSeconds == 60, "Existing saved timeout is preserved while new installations use sixty seconds");
        var orderedProfile = new Profile { Pets = Catalog.Create() };
        var orderedIds = orderedProfile.Pets.Keys.Take(6).ToArray();
        foreach (int id in orderedIds)
        {
            orderedProfile.Pets[id].Level = 3;
            orderedProfile.Pets[id].Goal = 75;
            orderedProfile.Pets[id].Souls = id % 75;
        }
        var sortOptions = new AppSettings { TrackerMode = "Nearby", RowLimit = 3, HideMaxed = false };
        var names = new PetNames();
        var expected = orderedIds.OrderBy(id => names.Resolve(orderedProfile.Pets[id]), StringComparer.OrdinalIgnoreCase).ThenBy(id => id).Take(3).ToArray();
        foreach (string sort in new[] { "Unfinished first", "Closest to next rank" })
        {
            sortOptions.Sort = sort;
            for (int refresh = 0; refresh < 6; refresh++)
            {
                var observed = orderedIds.Reverse().Select((id, index) => new NearbyPet(id, "Mob", now.AddSeconds(-((index + refresh) % 6)))).ToArray();
                tracker.ReadOnlyView(view with
                {
                    Profile = orderedProfile,
                    Settings = sortOptions,
                    Nearby = observed
                });
                check(Activity.Select(tracker, sortOptions, now).Select(a => a.PetId!.Value).SequenceEqual(expected), "Completed rows and row-limit membership remain stable across alternating refreshes");
            }
        }
        int unfinished = orderedIds.Except(expected).First();
        orderedProfile.Pets[unfinished].Level = 1;
        orderedProfile.Pets[unfinished].Goal = 25;
        orderedProfile.Pets[unfinished].Souls = 2;
        sortOptions.Sort = "Unfinished first";
        tracker.ReadOnlyView(view with
        {
            Profile = orderedProfile,
            Settings = sortOptions,
            Nearby = orderedIds.Select(id => new NearbyPet(id, "Mob", now)).ToArray()
        });
        check(Activity.Select(tracker, sortOptions, now).First().PetId == unfinished, "Unfinished pet takes priority over completed alphabetical entries");
        sortOptions.HideMaxed = true;
        check(Activity.Select(tracker, sortOptions, now).Single().PetId == unfinished, "Stable sorting preserves Hide maxed");
        sortOptions.PinnedPetId = expected[0];
        check(Activity.Select(tracker, sortOptions, now).Select(a => a.PetId).SequenceEqual(new int?[] { expected[0], unfinished }), "Pin overrides completed filter and remains first without duplication");
        sortOptions.HideMaxed = false;
        sortOptions.PinnedPetId = null;
        sortOptions.Sort = "Recent only";
        tracker.ReadOnlyView(view with
        {
            Profile = orderedProfile,
            Settings = sortOptions,
            Nearby = orderedIds.Select((id, index) => new NearbyPet(id, "Mob", now.AddSeconds(-index))).ToArray()
        });
        check(Activity.Select(tracker, sortOptions, now).First().PetId == orderedIds[0], "Explicit Recent only continues to honor freshness order");
        sortOptions.Sort = "Unfinished first";
        sortOptions.TrackerMode = "Recent";
        tracker.ReadOnlyView(view with
        {
            Profile = orderedProfile,
            Settings = sortOptions,
            Recent = orderedIds.Select((id, index) => new RecentPetActivity(id, null, "Mob", now.AddSeconds(-index))).ToList()
        });
        check(Activity.Select(tracker, sortOptions, now).Skip(1).Select(a => a.PetId!.Value).SequenceEqual(expected.Take(2)), "Maxed Recent rows also stop shuffling under Unfinished first");
    }
    static byte[] Var(int n)
    {
        var b = new List<byte>();
        do
        {
            byte v = (byte)(n & 127);
            n >>= 7;
            b.Add((byte)(v | (n > 0 ? 128 : 0)));
        } while (n > 0);
        return b.ToArray();
    }
}
