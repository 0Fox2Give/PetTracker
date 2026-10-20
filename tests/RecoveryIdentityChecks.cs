using System.Text.Json;
using System.Text.Json.Nodes;
using PetTracker;

static class RecoveryIdentityChecks
{
    public static void Run(string root, Action<bool, string> check)
    {
        var binding = new LocalProfileBinding { ProfileKey = new string('b', 24), CharacterName = "Test", RealmLabel = "Siel", AccountName = "My account" };
        var book = new LocalProfileBook { Profiles = new() { binding }, SelectedProfileKey = binding.ProfileKey };
        foreach (string field in new[] { nameof(LocalProfileBinding.ProfileKey), nameof(LocalProfileBinding.AccountName) })
        {
            string folder = Path.Combine(root, "null-binding-" + field), path = Path.Combine(folder, "profiles.json");
            Directory.CreateDirectory(folder);
            book.Save(path);
            File.Copy(path, path + ".bak");
            var broken = JsonNode.Parse(File.ReadAllText(path))!;
            broken["Profiles"]![0]![field] = null;
            string damaged = broken.ToJsonString();
            File.WriteAllText(path, damaged);
            var restored = AutomaticRecovery.Book(folder);
            check(restored.Selected?.ProfileKey == binding.ProfileKey && restored.Selected.AccountName == binding.AccountName, "Null " + field + " triggers account-book recovery instead of crashing startup");
            check(Directory.GetFiles(Path.Combine(folder, "recovery")).Any(p => File.ReadAllText(p) == damaged), "Null " + field + " recovery preserves the damaged original");
        }
        string renamedRoot = Path.Combine(root, "renamed-backup");
        using var engine = new Engine(renamedRoot);
        engine.Execute(new("create", Name: "Test", AccountName: "Old label"));
        engine.Feed("fixture", Convert.FromHexString("143336D105000000000704546573740100"));
        var original = engine.Book.Selected!;
        engine.Tracker.Profile.Pets[1066].Notes = "Keep this note";
        engine.Backup();
        engine.Execute(new("rename", Key: original.ProfileKey, AccountName: "New label"));
        var renamed = engine.Book.Selected!;
        string profile = Path.Combine(renamedRoot, "profiles", renamed.ProfileKey + ".json");
        // Both rolling copies are damaged, leaving only a managed pre-rename backup.
        File.WriteAllText(profile, "{broken");
        File.WriteAllText(profile + ".bak", "{broken");
        var recovered = AutomaticRecovery.Profile(renamedRoot, renamed);
        check(recovered.AccountKey == renamed.ProfileKey && recovered.Pets.TryGetValue(1066, out var pet) && pet.Notes == "Keep this note", "Renaming an account does not invalidate its matching managed collection backup");
        var foreign = JsonSerializer.Deserialize<LocalProfileBinding>(JsonSerializer.Serialize(renamed))!;
        foreign.ProfileKey = new string('c', 24);
        check(AutomaticRecovery.Profile(renamedRoot, foreign).Pets.Count == 0, "Matching labels and character names cannot recover another account's collection");
    }
}
