using System.IO;
using System.Text.Json;

namespace PetTracker;

// Account membership is declared locally; character/server records are not
// account identifiers. Legacy character profiles stay intact until converted.
public sealed class LocalProfileBinding
{
    public string AccountName { get; set; } = "";
    public List<string> LinkedCharacters { get; set; } = new();
    public bool IsAccount => !string.IsNullOrWhiteSpace(AccountName);
    public int? ServerToken { get; set; }
    public string CharacterName { get; set; } = "";
    public string RealmLabel { get; set; } = "";
    public string ProfileKey { get; set; } = "";
    public string Label => IsAccount ? AccountName : CharacterName + " · " + RealmLabel;

    public override string ToString() => Label;
    public bool Matches(string name) => string.Equals(name, CharacterName, StringComparison.OrdinalIgnoreCase) || IsAccount && LinkedCharacters.Contains(name, StringComparer.OrdinalIgnoreCase);
    public void Validate()
    {
        // JSON can contain explicit nulls even for nonnullable string properties.
        // Report invalid saved data so automatic recovery can try a valid backup.
        if (ProfileKey == null || AccountName == null)
            throw new InvalidDataException("Invalid local pet profile binding.");
        if (string.IsNullOrWhiteSpace(CharacterName) || !IsAccount && string.IsNullOrWhiteSpace(RealmLabel) || ProfileKey.Length != 24 || ProfileKey.Any(c => !char.IsAsciiHexDigit(c)) || AccountName.Length > 80 || LinkedCharacters == null || LinkedCharacters.Count > 100 || LinkedCharacters.Any(string.IsNullOrWhiteSpace) || LinkedCharacters.Distinct(StringComparer.OrdinalIgnoreCase).Count() != LinkedCharacters.Count)
            throw new InvalidDataException("Invalid local pet profile binding.");
    }

    public static LocalProfileBinding? Load(string path)
    {
        if (!File.Exists(path))
            return null;
        var value = JsonSerializer.Deserialize<LocalProfileBinding>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty local pet profile binding.");
        value.Validate();
        return value;
    }
}

public sealed class LocalProfileBook
{
    public List<LocalProfileBinding> Profiles { get; set; } = new();
    public string? SelectedProfileKey { get; set; }
    public LocalProfileBinding? Selected => Profiles.FirstOrDefault(p => p.ProfileKey == SelectedProfileKey);

    public void Validate()
    {
        if (Profiles == null || Profiles.Any(p => p == null))
            throw new InvalidDataException("Invalid pet profile list.");
        foreach (var p in Profiles)
            p.Validate();
        if (Profiles.Select(p => p.ProfileKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Profiles.Count || Profiles.Select(p => p.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Profiles.Count || SelectedProfileKey != null && Selected == null)
            throw new InvalidDataException("Duplicate or unknown pet profile selection.");
    }

    public static LocalProfileBook Load(string path)
    {
        var book = File.Exists(path) ? JsonSerializer.Deserialize<LocalProfileBook>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty pet profile list.") : new LocalProfileBook();
        book.Validate();
        return book;
    }

    public void Save(string path)
    {
        Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path))
            File.Replace(temporary, path, path + ".bak");
        else
            File.Move(temporary, path);
    }
}
