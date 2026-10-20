using System.Text.Json;
namespace PetTracker;

static class AccountMutationChecks
{
    public static void Run(Action<bool, string> check, string root, Action<Engine, string, string> identify, Action<Engine, string, int> snapshot, Action<Engine, string> reward)
    {
        foreach (string action in new[] { "select", "create", "link", "rename" })
        {
            string folder = Path.Combine(root, "account-write-" + action);
            using var engine = new Engine(folder);
            engine.Execute(new("create", Name: "Encore", AccountName: "First account"));
            string first = engine.Book.SelectedProfileKey!;
            engine.Execute(new("create", Name: "Zero", AccountName: "Second account"));
            string second = engine.Book.SelectedProfileKey!;
            engine.Execute(new("select", Key: first));
            identify(engine, "first", "Encore");
            snapshot(engine, "first", 7);
            if (action is "link" or "select")
                identify(engine, "second", "Zero");
            engine.Flush();
            string bookPath = Path.Combine(folder, "profiles.json");
            string before = JsonSerializer.Serialize(engine.Book);
            byte[] original = File.ReadAllBytes(bookPath);
            var command = action switch
            {
                "select" => new Command("select", Key: second),
                "create" => new Command("create", Name: "Rave", AccountName: "Third account"),
                "link" => new Command("link", Key: first, Name: "Zero"),
                _ => new Command("rename", Key: first, AccountName: "Renamed account")
            };
            using (var held = File.Open(bookPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                bool failed = false;
                try
                {
                    engine.Execute(command);
                }
                catch (IOException)
                {
                    failed = true;
                }
                check(failed, "A locked account book rejects " + action);
                check(JsonSerializer.Serialize(engine.Book) == before && engine.Tracker.Ready && engine.Active == "first" && engine.Tracker.ProfileKey == first, "Rejected " + action + " preserves account membership, selection and live binding");
                reward(engine, "first");
                engine.Flush();
                check(engine.Tracker.Profile.Pets[1066].CurrentSouls == 8 && !engine.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Rewards continue once on the original account after rejected " + action);
                check(File.ReadAllBytes(bookPath).SequenceEqual(original), "Rejected " + action + " leaves the persisted account book unchanged");
            }
            engine.Execute(command);
            var saved = LocalProfileBook.Load(bookPath);
            check(JsonSerializer.Serialize(saved) == JsonSerializer.Serialize(engine.Book), "Retry commits matching account state on disk and in memory: " + action);
            check(action switch
            {
                "select" => saved.SelectedProfileKey == second && engine.Tracker.Ready && engine.Tracker.ProfileKey == second,
                "create" => saved.Profiles.Count == 3 && saved.Selected!.Label == "Third account",
                "link" => saved.SelectedProfileKey == first && saved.Selected!.Matches("Zero") && engine.Tracker.Ready && engine.Tracker.CharacterName == "Zero",
                _ => saved.SelectedProfileKey == first && saved.Selected!.Label == "Renamed account" && engine.Tracker.Ready
            }, "Retry applies the requested account change: " + action);
            var firstProfile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(Path.Combine(folder, "profiles", first + ".json")))!;
            check(firstProfile.Pets[1066].CurrentSouls == 8 && firstProfile.AccountKey == first, "Successful " + action + " preserves the original account's last reward");
        }
        string blockedFolder = Path.Combine(root, "account-outgoing-save");
        using var blockedEngine = new Engine(blockedFolder);
        blockedEngine.Execute(new("create", Name: "Encore", AccountName: "First account"));
        string oldKey = blockedEngine.Book.SelectedProfileKey!;
        blockedEngine.Execute(new("create", Name: "Zero", AccountName: "Second account"));
        string newKey = blockedEngine.Book.SelectedProfileKey!;
        blockedEngine.Execute(new("select", Key: oldKey));
        identify(blockedEngine, "first", "Encore");
        snapshot(blockedEngine, "first", 7);
        blockedEngine.Flush();
        reward(blockedEngine, "first");
        string outgoing = Path.Combine(blockedFolder, "profiles", oldKey + ".json");
        using (var held = File.Open(outgoing, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool refused = false;
            try
            {
                blockedEngine.Execute(new("select", Key: newKey));
            }
            catch (IOException)
            {
                refused = true;
            }
            check(refused && blockedEngine.Tracker.Ready && blockedEngine.Book.SelectedProfileKey == oldKey && blockedEngine.Tracker.Profile.Pets[1066].CurrentSouls == 8, "Unsaved outgoing rewards prevent an account switch without losing the pending total");
            check(LocalProfileBook.Load(Path.Combine(blockedFolder, "profiles.json")).SelectedProfileKey == oldKey && JsonSerializer.Deserialize<Profile>(File.ReadAllText(outgoing))!.Pets[1066].CurrentSouls == 7, "Failed outgoing save cannot publish the new account choice to disk");
        }
        blockedEngine.Execute(new("select", Key: newKey));
        check(blockedEngine.Book.SelectedProfileKey == newKey && JsonSerializer.Deserialize<Profile>(File.ReadAllText(outgoing))!.Pets[1066].CurrentSouls == 8, "Account switch retries only after saving the outgoing pending reward");
    }
}
