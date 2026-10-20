using System.Text.Json;

using System.Security.Cryptography;

using System.Text;

namespace PetTracker;



public record CatalogEntry(int Id, string CatalogName, string Name, string Category);

public static class Catalog
{

    public static IReadOnlyList<CatalogEntry> Entries { get; } = Load();

    static List<CatalogEntry> Load()
    {
        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.Catalog.json")!;
        return JsonSerializer.Deserialize<List<CatalogEntry>>(stream)!;
    }

    public static Dictionary<int, PetProgress> Create() => Entries.ToDictionary(p => p.Id, p => new PetProgress { Id = p.Id, CatalogName = p.CatalogName, DisplayName = p.Name, Category = p.Category });

}

public sealed record CreaturePetMatch(int PetId, string CatalogName, string InternalName, string Source = "Source data");

public sealed class CreaturePetMappings
{

    readonly Dictionary<int, CreaturePetMatch> matches = new();
    readonly Dictionary<int, string> names = new();

    public CreaturePetMappings(string root, IReadOnlyDictionary<int, PetProgress> catalog)
    {

        using var stream = typeof(Catalog).Assembly.GetManifestResourceStream("PetTracker.PetSources.json")!;

        using var doc = JsonDocument.Parse(stream);

        foreach (var group in doc.RootElement.GetProperty("Rows").EnumerateArray().GroupBy(r => r.GetProperty("NpcId").GetInt32()))
        {

            names[group.Key] = group.First().GetProperty("NpcName").GetString() ?? ("NPC #" + group.Key);

            var claims = group.Select(r => (Id: r.GetProperty("PetId").GetInt32(), Name: r.GetProperty("CatalogName").GetString()!, Asset: r.GetProperty("InternalName").GetString()!)).Distinct().ToArray();

            if (claims.Length == 1 && catalog.TryGetValue(claims[0].Id, out var pet) && pet.CatalogName == claims[0].Name)
                matches[group.Key] = new(pet.Id, pet.CatalogName, claims[0].Asset);

        }

        Add(2100034, 1008, "OdSpider_01");
        Add(2100231, 1168, "WaterEle_01");
        Add(2400240, 1163, "Basilisk_01");

        void Add(int npc, int id, string name)
        {
            if (catalog.TryGetValue(id, out var pet) && pet.CatalogName == name)
                matches[npc] = new(id, name, "", "Gameplay/source corroborated");
        }

    }

    public CreaturePetMatch? Find(int npc) => matches.GetValueOrDefault(npc);

    public string CreatureName(int npc) => names.GetValueOrDefault(npc) ?? ("NPC #" + npc);

}

public class AppSettings
{
    public bool ShareAllCharacters
    {
        get; set;
    }
    // Retained for settings-file compatibility. Walking guidance is not ready for
    // distribution; neither old preferences nor IPC patches may enable it.
    public bool ExperimentalDirections
    {
        get; set;
    }

    public string TrackerMode { get; set; } = "Recent"; public int NearbyStaleSeconds { get; set; } = 60;
    // Legacy field is consumed once by Normalize when loading an older install.
    public int? PinnedPetId
    {
        get; set;
    }
    public List<int>? PinnedPetIds
    {
        get; set;
    }
    public int PinLimit { get; set; } = 3;
    public IReadOnlyList<int> Pins() => PinnedPetIds ?? (PinnedPetId is int id ? new List<int> { id } : new List<int>());
    public bool IsPinned(int? id) => id is int pet && Pins().Contains(pet);
    public bool CanPin(int id) => IsPinned(id) || Pins().Count < PinLimit;
    void NormalizePins()
    {
        PinLimit = PinLimit is >= 1 and <= 5 ? PinLimit : 3;
        PinnedPetIds = Pins().Where(id => Catalog.Entries.Any(p => p.Id == id)).Distinct().Take(5).ToList();
        PinnedPetId = null;
        // Keep a slot for ordinary activity, even when every allowed pin is used.
        RowLimit = new[] { 3, 5, 8, 10 }.First(n => n >= Math.Max(RowLimit, PinnedPetIds.Count + 1));
    }

    public bool Visible { get; set; } = true; public double? Left
    {
        get; set;
    }
    public double? Top
    {
        get; set;
    }

    public bool HideMaxed
    {
        get; set;
    }
    public bool ExpireInactive { get; set; } = true; public int InactiveSeconds { get; set; } = 20;
    public bool HideWhenEmpty
    {
        get; set;
    }

    public int RowLimit { get; set; } = 5; public double Scale { get; set; } = 1; public double Opacity { get; set; } = .95;

    public string Accent { get; set; } = "Gold"; public string Sort { get; set; } = "Unfinished first";

    public bool Locked
    {
        get; set;
    }
    public bool ClickThrough
    {
        get; set;
    }
    public bool ReducedMotion
    {
        get; set;
    }

    public bool Portraits { get; set; } = true; public bool ProgressBars { get; set; } = true; public bool Compact
    {
        get; set;
    }

    public bool GameOnly
    {
        get; set;
    }
    public bool StartWithWindows
    {
        get; set;
    }
    public bool StartMinimized
    {
        get; set;
    }

    public bool BlackMarketAlerts { get; set; } = true;
    public bool Toasts
    {
        get; set;
    }
    public bool Sounds
    {
        get; set;
    }
    public int Hotkey
    {
        get; set;
    }

    public bool CheckUpdatesAutomatically { get; set; } = true; public bool DownloadUpdatesAutomatically
    {
        get; set;
    }

    public string UpdateChannel { get; set; } = "win-stable";

    public string GameProcesses { get; set; } = "AION2,AION2-Win64-Shipping";

    public string? Adapter
    {
        get; set;
    }

    public void Normalize()
    {
        StartWithWindows = false;
        StartMinimized = false;
        ExperimentalDirections = false;
        if (TrackerMode is not ("Recent" or "Nearby"))
            TrackerMode = "Recent";
        if (!new[] { 15, 30, 60, 120, 300 }.Contains(NearbyStaleSeconds))
            NearbyStaleSeconds = 60;
        if (PinnedPetId is int pin && !Catalog.Entries.Any(p => p.Id == pin))
            PinnedPetId = null;
        if (UpdateChannel is not ("win-stable" or "win-beta"))
            UpdateChannel = "win-stable";
        if (string.IsNullOrWhiteSpace(GameProcesses))
            GameProcesses = "AION2,AION2-Win64-Shipping";
        if (!new[] { "Gold", "Blue", "Teal" }.Contains(Accent))
            Accent = "Gold";
        if (!new[] { "Unfinished first", "Recent only", "Closest to next rank" }.Contains(Sort))
            Sort = "Unfinished first";
        if (!new[] { 0, 0x50, 0x54, 0x4f }.Contains(Hotkey))
            Hotkey = 0;
        RowLimit = new[] { 3, 5, 8, 10 }.Contains(RowLimit) ? RowLimit : 5;
        InactiveSeconds = new[] { 15, 20, 30, 60, 120 }.Contains(InactiveSeconds) ? InactiveSeconds : 20;
        Scale = double.IsFinite(Scale) ? Math.Clamp(Scale, .75, 1.5) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .4, 1) : .95;
        if (Left.HasValue && !double.IsFinite(Left.Value))
            Left = null;
        if (Top.HasValue && !double.IsFinite(Top.Value))
            Top = null;
        NormalizePins();
    }

}

public static class SettingsPatch
{

    public static AppSettings Apply(AppSettings current, IReadOnlyDictionary<string, JsonElement> patch)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(current))!.AsObject();
        var allowed = typeof(AppSettings).GetProperties().Where(p => p.CanWrite).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var field in patch)
        {
            if (!allowed.Contains(field.Key))
                throw new InvalidDataException("Unknown setting");
            node[field.Key] = System.Text.Json.Nodes.JsonNode.Parse(field.Value.GetRawText());
        }
        var next = node.Deserialize<AppSettings>() ?? throw new InvalidDataException("Invalid settings");
        next.Normalize();
        return next;
    }

    public static Dictionary<string, JsonElement> Fields(params (string Name, object? Value)[] values) => values.ToDictionary(p => p.Name, p => JsonSerializer.SerializeToElement(p.Value));

}

public static class Storage
{

    public static string Root(string[] args)
    {
        int i = Array.IndexOf(args, "--data");
        return i >= 0 && i + 1 < args.Length ? Path.GetFullPath(args[i + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PetTracker");
    }

    public static string Pipe(string root) => PipeHash(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant());
    // Compatibility probe only: do not use the old spelling-sensitive name to
    // create a new worker. Existing workers can still be quit through this pipe.
    public static string LegacyPipe(string root) => PipeHash(Path.GetFullPath(root));
    static string PipeHash(string root) => "PetTracker-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName + root)))[..20];

    public static T Load<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path))
            return fallback();
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("The saved file is empty.");
    }

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path))
            File.Replace(tmp, path, path + ".bak");
        else
            File.Move(tmp, path);
    }

}

public sealed record DiagnosticEvent(DateTimeOffset Time, string Kind, string Message);

public sealed record ConnectionDiagnostics(long Payloads, long Frames, long Rewards, long DecoderFaults, DateTimeOffset? LastPayload, DateTimeOffset? LastIdentity, DateTimeOffset? LastReward, string LastInterruption, IReadOnlyList<DiagnosticEvent> Events, DecodeFault? LastFault, ProtocolActivityView? Activity = null, long ActiveInterruptions = 0, long BackgroundInterruptions = 0, long RetiredFlows = 0);

public sealed record CaptureAdapterOption(string Id, string Label);
public record WorkerView(int Protocol, Profile Profile, bool Ready, string Status, string Detail, string Character, List<LocalProfileBinding> Profiles, string? Selected, List<RecentPetActivity> Recent, PetRankIncrease? Increase, DateTimeOffset? LastSync, int Devices, long Received, long Dropped, long Gaps, AppSettings Settings, string? Error = null, string? DetectedCharacter = null, ConnectionDiagnostics? Health = null, int WorkerProcessId = 0, IReadOnlyList<NearbyPet>? Nearby = null, IReadOnlyList<CaptureAdapterOption>? Adapters = null, IReadOnlyList<MarketStock>? Market = null, bool CollectionSynced = false);

public record Command(string Action, string? Key = null, string? Name = null, string? Realm = null, AppSettings? Settings = null, Profile? Profile = null, List<LocalProfileBinding>? Bindings = null, Dictionary<string, Profile>? Imports = null, string? AccountName = null, Dictionary<string, JsonElement>? Patch = null, int? PetId = null);
