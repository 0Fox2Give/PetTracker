using System.Text.Json;
namespace PetTracker;

static class RecoveryAccessChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string folder = Path.Combine(root, "recovery-access");
        Directory.CreateDirectory(folder);
        var binding = new LocalProfileBinding { CharacterName = "Encore", AccountName = "My account", ProfileKey = new string('b', 24) };
        var current = new Profile { AccountKey = binding.ProfileKey, CharacterName = binding.CharacterName, Pets = Catalog.Create() };
        var pet = current.Pets[1066];
        pet.Level = 1;
        pet.Souls = 17;
        pet.Goal = 25;
        var old = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(current))!;
        old.Pets[1066].Souls = 7;
        string profile = Path.Combine(folder, "profiles", binding.ProfileKey + ".json");
        Storage.Save(profile, current);
        Storage.Save(profile + ".bak", old);
        string settings = Path.Combine(folder, "settings.json");
        Storage.Save(settings, new AppSettings { Accent = "Teal" });
        Storage.Save(settings + ".bak", new AppSettings { Accent = "Gold" });
        string book = Path.Combine(folder, "profiles.json");
        Storage.Save(book, new LocalProfileBook { Profiles = new() { binding }, SelectedProfileKey = binding.ProfileKey });
        Storage.Save(book + ".bak", new LocalProfileBook());
        foreach (var item in new (string Path, Action Load)[]{
   (profile,()=>AutomaticRecovery.Profile(folder,binding)),
   (settings,()=>AutomaticRecovery.Settings(folder)),
   (book,()=>AutomaticRecovery.Book(folder))
  })
        {
            var original = File.ReadAllBytes(item.Path);
            var backup = File.ReadAllBytes(item.Path + ".bak");
            // This legitimate sharing mode denies reads but permits renaming. Treating
            // that read error as corruption would replace valid current data with a backup.
            using (var blocked = File.Open(item.Path, FileMode.Open, FileAccess.Write, FileShare.Delete))
            {
                bool refused = false;
                try
                {
                    item.Load();
                }
                catch (IOException)
                {
                    refused = true;
                }
                check(refused, "An unreadable current " + Path.GetFileName(item.Path) + " must defer recovery rather than restore an older backup");
            }
            check(File.ReadAllBytes(item.Path).SequenceEqual(original) && File.ReadAllBytes(item.Path + ".bak").SequenceEqual(backup) && !Directory.Exists(Path.Combine(folder, "recovery")), "Temporary read denial preserves the current file, backup and recovery directory");
            item.Load();
        }
        check(AutomaticRecovery.Profile(folder, binding).Pets[1066].Souls == 17 && AutomaticRecovery.Settings(folder).Accent == "Teal" && AutomaticRecovery.Book(folder).Profiles.Count == 1, "Once the lock clears the original collection, settings and account return intact");
    }
}
