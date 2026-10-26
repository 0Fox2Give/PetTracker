using System.Text.Json;
namespace PetTracker;

static class PetPinChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        var ids = PetFinder.Search("").Take(6).Select(p => p.Pet.Id).ToArray();
        var legacy = JsonSerializer.Deserialize<AppSettings>($"{{\"PinnedPetId\":{ids[0]},\"RowLimit\":3}}")!;
        legacy.Normalize();
        legacy.Normalize();
        check(legacy.Pins().SequenceEqual(ids.Take(1)) && legacy.PinnedPetId == null && legacy.PinLimit == 3, "Legacy pin migrates once without losing its target");
        var first = PetPins.Change(legacy, ids[1], true);
        var second = PetPins.Change(first, ids[2], true);
        check(legacy.Pins().Count == 1 && first.Pins().Count == 2 && second.Pins().SequenceEqual(ids.Take(3)), "Pin commands preserve insertion order without mutating previous settings");
        check(second.RowLimit == 5, "Adding a third pin reserves room for ordinary activity");
        bool denied = false;
        try
        {
            PetPins.Change(second, ids[3], true);
        }
        catch (InvalidOperationException)
        {
            denied = true;
        }
        check(denied && second.Pins().SequenceEqual(ids.Take(3)), "Full pin list rejects another pin without replacing an existing goal");
        check(PetPins.Change(second, ids[2], true).Pins().SequenceEqual(second.Pins()), "Repeated pin request is idempotent at capacity");
        var lower = SettingsPatch.Apply(second, SettingsPatch.Fields((nameof(AppSettings.PinLimit), 1), (nameof(AppSettings.RowLimit), 3)));
        check(lower.Pins().Count == 3 && lower.PinLimit == 1 && lower.RowLimit == 5 && !lower.CanPin(ids[3]), "Lowering limits preserves existing pins and a normal activity slot");
        var removed = PetPins.Change(lower, ids[1], false);
        check(removed.Pins().SequenceEqual(new[] { ids[0], ids[2] }) && PetPins.Change(removed, ids[1], false).Pins().Count == 2, "Unpin removes only its own target and repeated removal is safe");
        var expanded = SettingsPatch.Apply(second, SettingsPatch.Fields((nameof(AppSettings.PinLimit), 5)));
        expanded = PetPins.Change(PetPins.Change(expanded, ids[3], true), ids[4], true);
        check(expanded.Pins().SequenceEqual(ids.Take(5)) && expanded.RowLimit == 8, "Five pins retain order and expand the overlay row limit");
        var reset = SettingsPatch.Apply(expanded, SettingsSections.Defaults("Tracking"));
        check(reset.Pins().SequenceEqual(ids.Take(5)) && reset.PinLimit == 3 && reset.RowLimit == 8, "Tracking reset keeps all pinned goals");
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(expanded))!;
        restored.Normalize();
        check(restored.Pins().SequenceEqual(ids.Take(5)), "Multiple pins survive settings serialization and normalization");
        var corrupt = new AppSettings { PinnedPetIds = new() { ids[1], int.MaxValue, ids[1], ids[0] }, PinLimit = 99 };
        corrupt.Normalize();
        check(corrupt.Pins().SequenceEqual(new[] { ids[1], ids[0] }) && corrupt.PinLimit == 3, "Invalid pin IDs and duplicates are removed without sorting valid pins");
        bool invalid = false;
        try
        {
            PetPins.Change(expanded, int.MaxValue, true);
        }
        catch (InvalidDataException)
        {
            invalid = true;
        }
        check(invalid, "Pin command rejects unknown pet IDs");

        var profile = new Profile { Pets = Catalog.Create() };
        var now = DateTimeOffset.UtcNow;
        profile.Pets[ids[0]].Level = 3;
        var tracker = new Tracker("", Catalog.Create());
        var recent = new List<RecentPetActivity> { new(ids[2], null, "", now), new(ids[5], null, "", now), new(ids[1], null, "", now.AddMinutes(-10)) };
        var view = new WorkerView(3, profile, true, "Live", "", "", new(), null, recent, null, now, 1, 0, 0, 0, expanded, Nearby: new[] { new NearbyPet(ids[2], "", now), new NearbyPet(ids[5], "", now) });
        tracker.ReadOnlyView(view);
        expanded.HideMaxed = true;
        foreach (string mode in new[] { "Recent", "Nearby" })
            foreach (string sort in new[] { "Recent only", "Closest to next rank", "Unfinished first" })
            {
                expanded.TrackerMode = mode;
                expanded.Sort = sort;
                var selected = Activity.Select(tracker, expanded, now).Select(a => a.PetId!.Value).ToArray();
                check(selected.SequenceEqual(ids), $"{mode}/{sort}: five pins remain ordered, unique and visible above ordinary activity");
            }
        string folder = Path.Combine(root, "multi-pins");
        Directory.CreateDirectory(folder);
        Storage.Save(Path.Combine(folder, "settings.json"), new AppSettings { PinnedPetId = ids[0] });
        using (var engine = new Engine(folder))
        {
            engine.Execute(new("pin", PetId: ids[1]));
            engine.Execute(new("pin", PetId: ids[2]));
            engine.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Opacity), .7))));
            engine.Execute(new("unpin", PetId: ids[0]));
            check(engine.Settings.Pins().SequenceEqual(new[] { ids[1], ids[2] }) && engine.Settings.Opacity == .7, "Worker pin commands compose with unrelated preference patches");
        }
        using (var reopened = new Engine(folder))
            check(reopened.Settings.Pins().SequenceEqual(new[] { ids[1], ids[2] }), "Worker restart loads the migrated multi-pin list");
    }
}
