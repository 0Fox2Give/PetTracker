using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PetTracker;
public sealed class Tracker
{
    readonly List<RecentPetActivity> recent = new();
    public IReadOnlyList<RecentPetActivity> RecentActivities => recent;
    public IReadOnlyList<NearbyPet> NearbyPets { get; private set; } = Array.Empty<NearbyPet>();
    public PetRankIncrease? LastRankIncrease { get; private set; }

    bool sessionCollectionReceived;
    readonly Dictionary<int, int> liveBalances = new();
    readonly Dictionary<int, int> pendingSpends = new();
    public Profile Profile { get; private set; } = new();
    public string? ProfileKey { get; private set; }
    public string CharacterName { get; private set; } = "";
    public string? LocalProfileLabel => binding?.Matches(CharacterName) == true && ProfileKey == binding.ProfileKey ? CharacterName + " · " + binding.RealmLabel : null;
    public int? LocalEntity { get; private set; }
    public Dictionary<int, EntityLife> Lives { get; } = new();
    public Dictionary<int, int> Summons { get; } = new();
    public Dictionary<int, long> Session { get; } = new();
    public Dictionary<int, PetProgress> Catalog { get; }
    public KillObservation? LastKill { get; private set; }
    public PetPointObservation? LastPointObservation { get; private set; }
    public Dictionary<int, long> SessionPointObservations { get; } = new();
    public long ObservationEpoch { get; private set; }
    public long ObservedDeaths { get; private set; }
    public long CountedKills { get; private set; }
    public long RosterUpdates { get; private set; }
    public string Error { get; set; } = "";

    public event Action? Changed;
    readonly string directory;
    readonly CreaturePetMappings? mappings;
    LocalProfileBinding? binding;
    string? nativeIdentityKey;
    bool freshLocalForIdentity;
    bool identityPending = true;
    readonly Dictionary<int, int> preIdentityRoster = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public Tracker(string directory, Dictionary<int, PetProgress>? catalog = null, CreaturePetMappings? mappings = null, LocalProfileBinding? binding = null)
    {
        this.directory = directory;
        this.mappings = mappings;
        binding?.Validate();
        this.binding = binding;
        Catalog = catalog ?? new();
    }

    static string Key(uint character, uint account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + ":" + character))).Substring(0, 24).ToLowerInvariant();
    public void LocalPlayer(int entity, string name)
    {
        if (entity <= 0 || string.IsNullOrWhiteSpace(name))
            return;
        if (LocalEntity.HasValue && !string.Equals(CharacterName, name, StringComparison.Ordinal))
        {
            // Stop recording until the new character's stable identity is known.
            identityPending = true;
            ProfileKey = null;
            Profile = new();
            Session.Clear();
            SessionPointObservations.Clear();
            preIdentityRoster.Clear();
            recent.Clear();
            liveBalances.Clear();
            pendingSpends.Clear();
            ClearWorld();
        }
        else if (LocalEntity != entity)
            ClearWorld();
        LocalEntity = entity;
        CharacterName = name;
        freshLocalForIdentity = true;
        // A user-selected character/server profile has its own stable identity.
        // A fresh native local-player event can bind it even if the meter missed
        // the login-only character discriminator. Saved counters remain stale.
        if (ProfileKey == null && binding?.Matches(name) == true)
            LoadProfile(binding.ProfileKey, false);
        if (ProfileKey != null)
        {
            string key = binding?.Matches(name) == true ? binding.ProfileKey : nativeIdentityKey ?? ProfileKey;
            if (key != ProfileKey)
                LoadProfile(key, false);
            identityPending = false;
            Profile.CharacterName = name;
            if (Ready)
                ApplyPreIdentityRoster();
        }

        Changed?.Invoke();
    }

    public void Identity(uint character, uint account)
    {
        if (character == 0)
            return;
        string packetKey = Key(character, account);
        bool nativeChanged = nativeIdentityKey != null && nativeIdentityKey != packetKey;
        bool changed = ProfileKey != null && (binding == null ? ProfileKey != packetKey : nativeChanged && !freshLocalForIdentity);
        nativeIdentityKey = packetKey;
        freshLocalForIdentity = false;
        string key = binding?.Matches(CharacterName) == true && LocalEntity.HasValue && !changed ? binding.ProfileKey : packetKey;
        if (ProfileKey == key && !identityPending && !nativeChanged)
            return;
        LoadProfile(key, changed);
    }

    void LoadProfile(string key, bool changed)
    {
        identityPending = true;
        ProfileKey = null; // A failed load cannot leave the old profile writable.
        if (changed)
        {
            LocalEntity = null;
            preIdentityRoster.Clear();
        }

        var loaded = new Profile();
        string path = Path.Combine(directory, key + ".json");
        if (binding != null && key == binding.ProfileKey)
            loaded = AutomaticRecovery.Profile(Path.GetDirectoryName(directory)!, binding);
        else if (File.Exists(path))
        {
            loaded = JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty pet profile");
        }

        Validate(loaded);
        foreach (var pet in loaded.Pets.Values)
            if (pet.Notes.Contains("[favorite]"))
            {
                pet.Favorite = true;
                pet.Notes = pet.Notes.Replace("[favorite]", "").Trim();
            }

        if (binding != null && key == binding.ProfileKey && !OwnedBy(loaded, binding))
            throw new InvalidDataException("The saved collection belongs to a different profile.");
        // If a different account/character arrives before its local name, require
        // a fresh local-player event rather than applying the old name/scope.
        Profile = loaded;
        if (binding?.IsAccount == true)
            Profile.AccountKey = key;
        ProfileKey = key;
        Session.Clear();
        SessionPointObservations.Clear();
        recent.Clear();
        LastRankIncrease = null;
        sessionCollectionReceived = false;
        liveBalances.Clear();
        pendingSpends.Clear();
        ClearWorld();
        identityPending = changed;
        if (!changed)
            Profile.CharacterName = CharacterName;
        if (changed)
            LocalEntity = null;
        foreach (var item in Catalog)
            EnsurePet(item.Key);
        // Time spent outside this capture cannot be reconstructed from old saved totals.
        foreach (var pet in Profile.Pets.Values)
            if (pet.Souls.HasValue || pet.EstimatedSouls.HasValue)
                pet.ProgressNeedsSync = true;
        foreach (var creature in Profile.Creatures.Values)
            DefaultPetLink(creature);
        if (!changed && Ready)
            ApplyPreIdentityRoster();
        Changed?.Invoke();
    }

    void ApplyPreIdentityRoster()
    {
        foreach (var item in preIdentityRoster)
            SetLevel(item.Key, item.Value);
        preIdentityRoster.Clear();
    }

    public bool IdentityAvailable => !identityPending && LocalEntity.HasValue && (nativeIdentityKey != null || binding?.Matches(CharacterName) == true && ProfileKey == binding.ProfileKey);
    public bool Ready => IdentityAvailable && ProfileKey != null && (binding == null || binding.Matches(CharacterName) && ProfileKey == binding.ProfileKey);

    public void SelectLocalProfile(LocalProfileBinding selected, bool copyCurrent = false)
    {
        selected.Validate();
        if (copyCurrent && (!Ready || !selected.Matches(CharacterName)))
            throw new InvalidOperationException("Only the currently identified character can seed a new local profile.");
        Save();
        if (copyCurrent)
        {
            string destination = Path.Combine(directory, selected.ProfileKey + ".json");
            if (File.Exists(destination))
                throw new InvalidOperationException("The selected profile already has data; it cannot be replaced by another profile.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(destination, JsonSerializer.Serialize(Profile, Json));
        }

        binding = selected;
        if (LocalEntity.HasValue && selected.Matches(CharacterName))
            LoadProfile(selected.ProfileKey, false);
        else if (IdentityAvailable && nativeIdentityKey != null)
            LoadProfile(nativeIdentityKey, false);
        else
        {
            ClearWorld();
            Session.Clear();
            SessionPointObservations.Clear();
            Changed?.Invoke();
        }
    }

    public void Pause()
    {
        NearbyPets = Array.Empty<NearbyPet>();
        ClearWorld();
        identityPending = true;
        ProfileKey = null;
        LocalEntity = null;
        freshLocalForIdentity = false;
        LastKill = null;
        LastPointObservation = null;
        SessionPointObservations.Clear();
        recent.Clear();
        LastRankIncrease = null;
        sessionCollectionReceived = false;
        liveBalances.Clear();
        pendingSpends.Clear();
        ObservationEpoch++;
    }

    public void ClearWorld()
    {
        NearbyPets = Array.Empty<NearbyPet>();
        Lives.Clear();
        Summons.Clear();
        LastKill = null;
        LastPointObservation = null;
        ObservationEpoch++;
    }

    public void Spawn(int entity, int npc, string name, long ms, bool eligible)
    {
        if (entity <= 0 || npc <= 0)
            return;
        if (Lives.TryGetValue(entity, out var life) && !life.Ended && life.NpcId == npc)
        {
            if (name.Length > 0)
                life.Name = name;
            life.Eligible |= eligible;
            return; // Duplicate appearance does not erase participation.
        }

        Lives[entity] = new EntityLife { NpcId = npc, Name = name, SpawnMs = ms, Eligible = eligible };
    }

    public void Name(int entity, string name)
    {
        if (Lives.TryGetValue(entity, out var life))
            life.Name = name;
    }

    public void Summon(int summon, int owner)
    {
        Summons[summon] = owner;
        Lives.Remove(summon);
    }

    public void Damage(int actor, int target, int damage, bool heal, long timestamp)
    {
        if (!Ready || damage <= 0 || heal || !Lives.TryGetValue(target, out var life) || life.Ended || !life.Eligible || timestamp < life.SpawnMs)
            return;
        bool own = actor == LocalEntity;
        if (!own && Summons.TryGetValue(actor, out int owner))
            own = owner == LocalEntity;
        if (own)
            life.Participated = true;
    }

    public bool Remove(int entity, int reason, long ms)
    {
        Summons.Remove(entity);
        if (!Lives.TryGetValue(entity, out var life) || life.Ended || ms < life.SpawnMs)
            return false;
        life.Ended = true;
        if (reason != 3)
            return false;
        ObservedDeaths++;
        if (!Ready || !life.Eligible || !life.Participated)
            return false;
        if (!Profile.Creatures.TryGetValue(life.NpcId, out var row))
        {
            row = new CreatureKills { NpcId = life.NpcId, Name = life.Name };
            Profile.Creatures.Add(life.NpcId, row);
        }

        if (!string.IsNullOrWhiteSpace(life.Name))
            row.Name = life.Name;
        DefaultPetLink(row);
        checked
        {
            row.Total++;
            Session[life.NpcId] = Session.GetValueOrDefault(life.NpcId) + 1;
        }

        LastKill = new KillObservation(life.NpcId, row.Name);
        RememberRecent(row.PetId, row.NpcId, row.Name, DateTimeOffset.UtcNow);
        CountedKills++;
        Changed?.Invoke();
        return true;
    }

    PetProgress EnsurePet(int id)
    {
        if (Profile.Pets.TryGetValue(id, out var pet))
            return pet;
        Catalog.TryGetValue(id, out var item);
        pet = new PetProgress { Id = id, CatalogName = item?.CatalogName ?? ("Pet #" + id), Category = item?.Category ?? "" };
        Profile.Pets[id] = pet;
        return pet;
    }

    public void Roster(IReadOnlyList<(int PetId, int Level)> roster)
    {
        RosterUpdates++;
        if (ProfileKey == null || (!identityPending && !LocalEntity.HasValue) || IdentityAvailable && !Ready)
        {
            foreach (var p in roster.Where(p => p.Level is >= 0 and <= 3))
                preIdentityRoster[p.PetId] = p.Level;
            return;
        }

        if (!Ready)
            return; // Cannot attribute a roster during a character switch.
        foreach (var p in roster)
            SetLevel(p.PetId, p.Level);
        Changed?.Invoke();
    }

    void SetLevel(int id, int level)
    {
        if (id <= 0 || level < 0 || level > 3)
            return;
        var pet = EnsurePet(id);
        bool spentBeforeRank = pet.Level is int previous && level == previous + 1 && pendingSpends.TryGetValue(id, out int spentRank) && spentRank == previous;
        if (level != pet.Level)
            pendingSpends.Remove(id);
        // Partial rosters may still describe the login rank after its final reward.
        // A complete collection snapshot remains authoritative.
        if (pet.LevelDerivedFromSouls && pet.Max && level < 3)
            return;
        RecordRankIncrease(pet.Id, pet.Level, level, sessionCollectionReceived || pet.RankIncreasePending);
        if (pet.Level != level)
        {
            pet.Souls = null;
            pet.Goal = PetStages.CounterGoal(level);
            pet.EstimatedSouls = null;
            pet.ProgressNeedsSync = true;
            pet.CounterVerified = false;
            if (!liveBalances.ContainsKey(id))
                pet.SyncedUtc = null;
        }

        pet.Level = level;
        pet.LevelVerified = true;
        pet.LevelDerivedFromSouls = false;
        if (level == 3)
        {
            pet.Souls = null;
            pet.Goal = null;
            pet.EstimatedSouls = null;
            pet.ProgressNeedsSync = false;
        }
        else if (spentBeforeRank && liveBalances.TryGetValue(id, out int remaining) && pet.Goal is int cap && remaining <= cap)
        {
            pet.Souls = remaining;
            pet.EstimatedSouls = remaining;
            pet.ProgressNeedsSync = false;
            pet.CounterVerified = false;
        }
    }

    public void EditPet(int id, string name, int? level, int? souls, int? goal, string notes)
    {
        if (!Ready)
            throw new InvalidOperationException("Wait for your character identity before editing saved progress.");
        if (id <= 0 || level is < 0 or > 3 || souls is < 0 || goal is <= 0 || (souls.HasValue && (!goal.HasValue || souls > goal)))
            throw new InvalidDataException("Enter level 0–3 and a valid current/goal pair, or leave unknown values blank.");
        var p = EnsurePet(id);
        if (p.Level != level)
            p.LevelVerified = false;
        p.LevelDerivedFromSouls = false;
        p.CounterVerified = false;
        p.DisplayName = name.Trim();
        p.Level = level;
        p.Souls = level >= 3 ? null : souls;
        p.Goal = level >= 3 ? null : goal;
        p.Notes = notes.Trim();
        p.EstimatedSouls = null;
        p.ProgressNeedsSync = !p.Souls.HasValue || !p.Goal.HasValue;
        p.SyncedUtc = DateTimeOffset.UtcNow;
        if (p.Souls is int balance && !p.ProgressNeedsSync)
            liveBalances[id] = balance;
        else
            liveBalances.Remove(id);
        Changed?.Invoke();
    }

    public void Link(int npc, int? petId)
    {
        if (petId.HasValue && !Profile.Pets.ContainsKey(petId.Value))
            throw new InvalidDataException("Unknown pet ID.");
        Profile.Creatures[npc].PetId = petId;
        Profile.Creatures[npc].LinkManuallySet = true;
        Profile.Creatures[npc].LinkSource = "Manual";
        for (int index = 0; index < recent.Count; index++)
            if (recent[index].NpcId == npc)
                recent[index] = recent[index] with { PetId = petId };
        var unique = new HashSet<int>();
        recent.RemoveAll(r => r.PetId is not int id || !unique.Add(id));
        Changed?.Invoke();
    }

    void DefaultPetLink(CreatureKills creature)
    {
        // Exact known variants: Papis from gameplay evidence, red caps from bundled
        // client internal names Fungus_01 / ValleyFungy_01 and corresponding pet assets.
        if (creature.LinkManuallySet)
            return;
        if (mappings?.Find(creature.NpcId)is { } source && Profile.Pets.TryGetValue(source.PetId, out var sourcePet) && sourcePet.CatalogName == source.CatalogName)
        {
            if (creature.PetId == null || creature.LinkSource is "Client assets" or "Source data" or "Needs verification")
            {
                creature.PetId = source.PetId;
                creature.LinkSource = source.Source;
            }

            return;
        }

        if (creature.LinkSource is "Client assets" or "Source data")
        {
            creature.PetId = null;
            creature.LinkSource = "Needs verification";
        }

        if (creature.PetId != null)
            return;
        var mapping = creature.NpcId switch
        {
            2100009 when creature.Name == "Gentle Papis" => (1066, "Papis_01"),
            2100456 when creature.Name == "Red Cap Fungen" => (1086, "Fungus_01"),
            2100041 when creature.Name == "Red Cap Fungie" => (1012, "Fungy_01"),
            _ => (0, "")};
        if (Profile.Pets.TryGetValue(mapping.Item1, out var pet) && pet.CatalogName == mapping.Item2)
        {
            creature.PetId = mapping.Item1;
            creature.LinkSource = "Gameplay";
            return;
        }

        if (mappings?.Find(creature.NpcId)is { } match && Profile.Pets.TryGetValue(match.PetId, out var matched) && matched.CatalogName == match.CatalogName)
        {
            creature.PetId = match.PetId;
            creature.LinkSource = match.Source;
        }
    }

    public bool ObserveSoulActivity(int id, int amount, DateTimeOffset observed)
    {
        if (!Ready || amount is <= 0 or > 75 || !Profile.Pets.ContainsKey(id))
            return false;
        RememberRecent(id, null, "", observed, true);
        LastPointObservation = new(id, amount, observed);
        Changed?.Invoke();
        return true;
    }

    public bool ObservePointUpdate(int petId, int value, DateTimeOffset observed)
    {
        if (!Ready || value is <= 0 or > 75 || !Profile.Pets.TryGetValue(petId, out var pet))
            return false;
        string? catalog = PetPointDecoder.SupportedCatalog(petId);
        if (!pet.CollectionImportedUtc.HasValue && (catalog == null || pet.CatalogName != catalog))
            return false;
        SessionPointObservations[petId] = checked(SessionPointObservations.GetValueOrDefault(petId) + value);
        RememberRecent(petId, null, "", observed, true);
        if (!pet.Max && liveBalances.TryGetValue(petId, out int balance))
            liveBalances[petId] = checked(balance + value);
        if (!pet.ProgressNeedsSync && !pet.Max && pet.Level is >= 0 && pet.CurrentSouls is int current && pet.Goal is int goal && current < goal)
        {
            pet.EstimatedSouls = (int)Math.Min(goal, (long)current + value);
            if (!PromoteMax(pet) && pet.EstimatedSouls >= goal)
                pet.ProgressNeedsSync = true;
        }

        LastPointObservation = new PetPointObservation(petId, value, observed);
        Changed?.Invoke();
        return true;
    }

    public bool ObservePointsSpent(int id, int spent, DateTimeOffset observed)
    {
        if (!Ready || spent is <= 0 or > 75 || !Profile.Pets.TryGetValue(id, out var pet))
            return false;
        if (pet.Max)
            liveBalances.Remove(id);
        else
        {
            if (!liveBalances.TryGetValue(id, out int before) || before < spent || pet.Goal is not int goal)
            {
                liveBalances.Remove(id);
                pet.ProgressNeedsSync = true;
                Changed?.Invoke();
                return false;
            }

            int remaining = before - spent;
            liveBalances[id] = remaining;
            if (pet.Level is 0 or 1 && spent == PetStages.CounterGoal(pet.Level.Value))
            {
                // The spend can precede a rank message, including an unsupported unlock
                // layout. Preserve the completed stage until that rank is verified; keep
                // the deducted balance separately so later rewards are not lost.
                pendingSpends[id] = pet.Level.Value;
                pet.EstimatedSouls = goal;
                pet.ProgressNeedsSync = true;
                pet.CounterVerified = false;
            }
            else
            {
                pendingSpends.Remove(id);
                if (remaining > goal)
                {
                    pet.ProgressNeedsSync = true;
                    Changed?.Invoke();
                    return false;
                }

                pet.Souls = remaining;
                pet.EstimatedSouls = remaining;
                pet.ProgressNeedsSync = false;
                pet.CounterVerified = false;
                if (!PromoteMax(pet) && remaining >= goal)
                    pet.ProgressNeedsSync = true;
            }
        }

        RememberRecent(id, null, "", observed);
        Changed?.Invoke();
        return true;
    }

    bool PromoteMax(PetProgress pet, bool notify = true)
    {
        // User verified that the final reward automatically maxes the in-game pet.
        // Never promote a frozen estimate, or infer a different stage's rollover.
        if (pet.ProgressNeedsSync || pet.Level != 2 || pet.Goal != PetStages.CounterGoal(2) || pet.CurrentSouls is not int total || total < pet.Goal)
            return false;
        RecordRankIncrease(pet.Id, 2, 3, notify);
        pet.Level = 3;
        pet.LevelVerified = false;
        pet.LevelDerivedFromSouls = true;
        pet.Souls = null;
        pet.Goal = null;
        pet.EstimatedSouls = null;
        pet.ProgressNeedsSync = false;
        return true;
    }

    void RecordRankIncrease(int id, int? previous, int rank, bool eligible)
    {
        if (!eligible || previous is not int old || rank <= old || !recent.Any(r => r.PetId == id))
            return;
        var now = DateTimeOffset.UtcNow;
        LastRankIncrease = new(id, rank, now);
        RememberRecent(id, null, "", now);
    }

    void RememberRecent(int? pet, int? npc, string creature, DateTimeOffset time, bool reward = false)
    {
        // Keep non-pet kills in creature history, never in the limited farming HUD.
        if (pet is not int id || !Profile.Pets.ContainsKey(id))
            return;
        var existing = recent.FirstOrDefault(r => r.PetId == id);
        if (existing != null)
        {
            recent.Remove(existing);
            npc ??= existing.NpcId;
            if (creature.Length == 0)
                creature = existing.CreatureName;
        }

        recent.Insert(0, new(pet, npc, creature, time, SoulRewardUtc: reward ? time : existing?.SoulRewardUtc));
        if (recent.Count > 50)
            recent.RemoveRange(50, recent.Count - 50);
    }

    public void InvalidateProgress()
    {
        liveBalances.Clear();
        pendingSpends.Clear();
        bool changed = false;
        foreach (var pet in Profile.Pets.Values)
            if (!pet.Max && (pet.Souls.HasValue || pet.EstimatedSouls.HasValue) && !pet.ProgressNeedsSync)
            {
                pet.ProgressNeedsSync = true;
                changed = true;
            }

        if (changed)
            Changed?.Invoke();
    }

    public int ImportCollection(CollectionSnapshot snapshot, DateTimeOffset captured, IReadOnlyDictionary<int, int> deltas, IReadOnlySet<int> stale, IReadOnlyDictionary<int, int>? spends = null)
    {
        if (!Ready)
            throw new InvalidOperationException("Wait for your character before importing its collection.");
        int imported = 0;
        foreach (int id in Catalog.Keys)
        {
            if (stale.Contains(id))
                continue;
            pendingSpends.Remove(id);
            var p = EnsurePet(id);
            int rank = snapshot.Ranks.GetValueOrDefault(id);
            int count = snapshot.Souls.GetValueOrDefault(id);
            int? unlockGoal = p.Level == 0 && p.Goal >= count ? p.Goal : null;
            // Preserve a manual unlock goal and contradictory raw counters.
            if (unlockGoal == null && count <= PetStages.UnlockCost)
                unlockGoal = PetStages.UnlockCost;
            RecordRankIncrease(id, p.Level, rank, sessionCollectionReceived || p.RankIncreasePending);
            p.Level = rank;
            p.LevelVerified = true;
            p.LevelDerivedFromSouls = false;
            p.Souls = rank == 3 ? null : snapshot.Souls.GetValueOrDefault(id);
            p.Goal = rank == 0 ? unlockGoal : PetStages.CounterGoal(rank);
            p.EstimatedSouls = null;
            p.CollectionCounterPresent = snapshot.Souls.ContainsKey(id);
            p.CounterVerified = true;
            p.CollectionImportedUtc = captured;
            p.SyncedUtc = captured;
            p.ProgressNeedsSync = rank == 0 && p.Goal == null;
            int delta = deltas.GetValueOrDefault(id);
            if (rank is 0 or 1 or 2 && p.Souls is int current && p.Goal is int goal)
            {
                long balance = (long)current + delta;
                if (balance < 0 || balance > int.MaxValue)
                {
                    liveBalances.Remove(id);
                    p.ProgressNeedsSync = true;
                }
                else
                {
                    liveBalances[id] = (int)balance;
                    if (delta < 0)
                    {
                        p.Souls = (int)Math.Min(goal, balance);
                        p.EstimatedSouls = p.Souls;
                        p.CounterVerified = false;
                    }
                    else if (delta > 0)
                        p.EstimatedSouls = (int)Math.Min(goal, balance);
                }
            }
            else
                liveBalances.Remove(id);
            if (spends != null && spends.TryGetValue(id, out int spentRank) && spentRank == rank && rank is 0 or 1 && p.Goal is int pendingGoal)
            {
                pendingSpends[id] = rank;
                p.EstimatedSouls = pendingGoal;
                p.ProgressNeedsSync = true;
                p.CounterVerified = false;
            }

            if (!PromoteMax(p, sessionCollectionReceived) && p.CurrentSouls is int total && p.Goal is int cap && total >= cap)
                p.ProgressNeedsSync = true;
            imported++;
        }

        sessionCollectionReceived = true;
        Changed?.Invoke();
        return imported;
    }

    public void ClearPointAlert()
    {
        LastPointObservation = null;
        ObservationEpoch++;
        Changed?.Invoke();
    }

    public long TotalFor(int pet) => Profile.Creatures.Values.Where(c => c.PetId == pet).Sum(c => c.Total);
    public long SessionFor(int pet) => Profile.Creatures.Values.Where(c => c.PetId == pet).Sum(c => Session.GetValueOrDefault(c.NpcId));
    public void NewSession()
    {
        Session.Clear();
        SessionPointObservations.Clear();
        LastKill = null;
        LastPointObservation = null;
        recent.Clear();
        LastRankIncrease = null;
        ObservationEpoch++;
        Changed?.Invoke();
    }

    public void ReadOnlyView(WorkerView view)
    {
        Profile = view.Profile;
        ProfileKey = view.Selected;
        CharacterName = view.Character;
        LocalEntity = view.Ready ? 1 : null;
        identityPending = !view.Ready;
        nativeIdentityKey = view.Ready ? "view" : null;
        recent.Clear();
        recent.AddRange(view.Recent);
        NearbyPets = view.Nearby ?? Array.Empty<NearbyPet>();
        LastRankIncrease = view.Increase;
    }

    public static bool OwnedBy(Profile profile, LocalProfileBinding selected) => selected.IsAccount ? profile.AccountKey == selected.ProfileKey || profile.AccountKey == null && profile.CharacterName == selected.CharacterName : profile.AccountKey == null && profile.CharacterName == selected.CharacterName;
    public void Save()
    {
        if (ProfileKey == null)
            return;
        if (binding != null && (!binding.Matches(CharacterName) || ProfileKey != binding.ProfileKey))
            return;
        Validate(Profile);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, ProfileKey + ".json"), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Profile, Json), Encoding.UTF8);
        if (File.Exists(path))
            File.Replace(temp, path, path + ".bak");
        else
            File.Move(temp, path);
    }

    public static void Validate(Profile p)
    {
        if (p.Version != 1 || p.Pets == null || p.Creatures == null)
            throw new InvalidDataException("Unsupported pet profile.");
        foreach (var x in p.Pets)
        {
            if (x.Value == null || x.Key != x.Value.Id || x.Key <= 0 || x.Value.Level is < 0 or > 3 || x.Value.Souls is < 0 || x.Value.Goal is <= 0 || (x.Value.LevelDerivedFromSouls && (x.Value.Level != 3 || x.Value.LevelVerified)) || (x.Value.Souls.HasValue && ((!x.Value.Goal.HasValue && !(x.Value.Level == 0 && x.Value.CollectionImportedUtc.HasValue)) || x.Value.Goal.HasValue && x.Value.Souls > x.Value.Goal)) || x.Value.EstimatedSouls is < 0 || (x.Value.EstimatedSouls.HasValue && (!x.Value.Souls.HasValue || !x.Value.Goal.HasValue || x.Value.EstimatedSouls < x.Value.Souls || x.Value.EstimatedSouls > x.Value.Goal)))
                throw new InvalidDataException("Invalid pet progress.");
        }

        foreach (var x in p.Creatures)
        {
            if (x.Value == null || x.Key != x.Value.NpcId || x.Key <= 0 || x.Value.Total < 0 || (x.Value.PetId.HasValue && !p.Pets.ContainsKey(x.Value.PetId.Value)))
                throw new InvalidDataException("Invalid creature counts.");
        }
    }
}
