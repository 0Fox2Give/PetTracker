using System.Text.Json;
using System.Text.RegularExpressions;
namespace PetTracker;

// Recovery uses only this data directory's validated bindings and managed backups.
// Restored points remain last-known until a new game snapshot confirms them.
public static class AutomaticRecovery
{
    static bool SafeFile(string path) => File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
    static T? Read<T>(string path, Action<T> validate, bool current = false) where T : class
    {
        try
        {
            // File.Exists hides access errors. The current file must be readable before
            // deciding it is damaged; a sharing/access failure cannot authorize rollback.
            if (current)
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    return null;
            }
            else if (!SafeFile(path))
                return null;
            var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            if (value == null)
                return null;
            validate(value);
            return value;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or JsonException or InvalidDataException or ArgumentException)
        {
            return null;
        }
        catch (Exception ex) when (!current && (ex is IOException or UnauthorizedAccessException))
        {
            return null;
        }
    }
    static IEnumerable<string> Backups(string root)
    {
        string parent = Path.Combine(root, "backups");
        if (!Directory.Exists(parent) || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
            return Array.Empty<string>();
        return Directory.GetDirectories(parent).Where(p => Regex.IsMatch(Path.GetFileName(p), "^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$") && (File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0 && Read<Marker>(Path.Combine(p, "backup.json"), m =>
        {
            if (m.Product != "PetTracker" || m.Format != 1)
                throw new InvalidDataException();
        }) != null).OrderByDescending(Directory.GetCreationTimeUtc).ToArray();
    }
    sealed class Marker
    {
        public string Product { get; set; } = ""; public int Format
        {
            get; set;
        }
    }
    static void Preserve(string root, string path)
    {
        if (!SafeFile(path))
            return;
        string folder = Path.GetFullPath(Path.Combine(root, "recovery"));
        Directory.CreateDirectory(folder);
        string target = Path.GetFullPath(Path.Combine(folder, Path.GetFileName(path) + "-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid recovery path");
        File.Move(path, target);
    }
    static void Note(string root, string text)
    {
        var path = Path.Combine(root, "automatic-recovery.json");
        var notes = Read<List<DiagnosticEvent>>(path, n => { }) ?? new();
        notes.Add(new(DateTimeOffset.UtcNow, "Recovery", text));
        Storage.Save(path, notes.TakeLast(20).ToList());
    }
    public static AppSettings Settings(string root)
    {
        string path = Path.Combine(root, "settings.json");
        var current = Read<AppSettings>(path, s => s.Normalize(), current: true);
        if (current != null)
            return current;
        foreach (string candidate in new[] { path + ".bak" }.Concat(Backups(root).Select(p => Path.Combine(p, "settings.json"))).Where(SafeFile).OrderByDescending(File.GetLastWriteTimeUtc))
            if (Read<AppSettings>(candidate, s => s.Normalize()) is { } restored)
            {
                Preserve(root, path);
                Storage.Save(path, restored);
                Note(root, "Settings recovered automatically");
                return restored;
            }
        Preserve(root, path);
        return new();
    }
    public static LocalProfileBook Book(string root)
    {
        string path = Path.Combine(root, "profiles.json");
        var current = Read<LocalProfileBook>(path, b => b.Validate(), current: true);
        if (current != null)
            return current;
        foreach (string candidate in new[] { path + ".bak" }.Concat(Backups(root).Select(p => Path.Combine(p, "profiles.json"))).Where(SafeFile).OrderByDescending(File.GetLastWriteTimeUtc))
            if (Read<LocalProfileBook>(candidate, b => b.Validate()) is { } restored)
            {
                string backupRoot = Path.GetDirectoryName(candidate)!;
                bool managed = backupRoot != root;
                if (managed && restored.Profiles.Any(b => Read<Profile>(Path.Combine(backupRoot, "profiles", b.ProfileKey + ".json"), p => Validate(p, b)) == null))
                    continue;
                Preserve(root, path);
                restored.Save(path);
                Note(root, "Account selection recovered automatically");
                return restored;
            }
        Preserve(root, path);
        return new();
    }
    static void Validate(Profile p, LocalProfileBinding binding)
    {
        Tracker.Validate(p);
        if (!Tracker.OwnedBy(p, binding) || p.Pets.Keys.Any(id => !Catalog.Entries.Any(c => c.Id == id)))
            throw new InvalidDataException("Backup does not match this account");
    }
    public static Profile Profile(string root, LocalProfileBinding binding)
    {
        binding.Validate();
        string path = Path.Combine(root, "profiles", binding.ProfileKey + ".json");
        var current = Read<Profile>(path, p => Validate(p, binding), current: true);
        if (current != null)
            return current;
        var candidates = new List<string> { path + ".bak" };
        foreach (string backup in Backups(root))
        {
            var book = Read<LocalProfileBook>(Path.Combine(backup, "profiles.json"), b => b.Validate());
            var paired = book?.Profiles.FirstOrDefault(b => b.ProfileKey == binding.ProfileKey);
            if (paired == null)
                continue;
            // Account labels can change. The stable profile key and the collection's
            // ownership validation below establish identity, not the display label.
            if (!(paired.IsAccount && binding.IsAccount) && (paired.CharacterName != binding.CharacterName || paired.AccountName != binding.AccountName))
                continue;
            candidates.Add(Path.Combine(backup, "profiles", binding.ProfileKey + ".json"));
        }
        foreach (string candidate in candidates.Where(SafeFile).OrderByDescending(File.GetLastWriteTimeUtc))
            if (Read<Profile>(candidate, p => Validate(p, binding)) is { } restored)
            {
                foreach (var pet in restored.Pets.Values)
                    if (!pet.Max)
                        pet.ProgressNeedsSync = true;
                Preserve(root, path);
                Storage.Save(path, restored);
                Note(root, "Saved collection recovered automatically; waiting for game confirmation");
                return restored;
            }
        Preserve(root, path);
        return new()
        {
            CharacterName = binding.CharacterName,
            AccountKey = binding.IsAccount ? binding.ProfileKey : null
        };
    }
    public static void MigrateLegacy(string root, LocalProfileBook book)
    {
        bool changed = false;
        foreach (var binding in book.Profiles.Where(b => !b.IsAccount))
        {
            var profile = Profile(root, binding);
            string label = book.Profiles.Count == 1 ? "My account" : binding.Label + " account";
            int suffix = 2;
            string proposed = label;
            while (book.Profiles.Any(b => b != binding && b.Label.Equals(proposed, StringComparison.OrdinalIgnoreCase)))
                proposed = label + " " + suffix++;
            binding.AccountName = proposed;
            binding.ServerToken = null;
            profile.AccountKey = binding.ProfileKey;
            Storage.Save(Path.Combine(root, "profiles", binding.ProfileKey + ".json"), profile);
            changed = true;
        }
        if (changed)
        {
            book.Save(Path.Combine(root, "profiles.json"));
            Note(root, "Earlier local profiles migrated to account collections");
        }
    }
}
