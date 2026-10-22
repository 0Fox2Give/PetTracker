using System.Text.Json;

namespace PetTracker;

public sealed class Engine : IDisposable
{
    public readonly object Gate = new();
    public Tracker Tracker { get; }
    public LocalProfileBook Book { get; }

    readonly string root;
    readonly Dictionary<string, Node> nodes = new();
    readonly CreaturePetMappings mappings;
    public AppSettings Settings { get; private set; }
    public DateTimeOffset? LastSync { get; private set; }
    public string CaptureError { get; set; } = "";
    public string? Active { get; private set; }
    public bool ExitRequested { get; private set; }

    public Action<string>? CaptureAction;
    public Action<string, int, int>? Protect;
    public Action? Release;
    bool dirty;
    long nextBackup = Environment.TickCount64 + 300000;
    long? gameGeneration;
    long identitySequence;
    readonly Dictionary<int, MarketStock> market = new();
    readonly ProtocolActivity activity = new();
    readonly NearbyWorld nearby;
    long payloads, frames, rewards, decoderFaults, activeInterruptions, backgroundInterruptions, retiredFlows;
    DateTimeOffset? lastPayload, lastIdentity, lastReward;
    string lastInterruption = "";
    DecodeFault? lastFault;
    readonly List<DiagnosticEvent> events = new();
    void ClearObservations()
    {
        nearby.Clear();
        market.Clear();
    }

    void Trace(string kind, string message)
    {
        events.Add(new(DateTimeOffset.UtcNow, kind, message));
        if (events.Count > 80)
            events.RemoveAt(0);
    }

    ConnectionDiagnostics Health() => new(payloads, frames, rewards, decoderFaults, lastPayload, lastIdentity, lastReward, lastInterruption, events.ToArray(), lastFault, activity.View(), activeInterruptions, backgroundInterruptions, retiredFlows);
    sealed class Node
    {
        public CollectionWire Wire = null!;
        public LocalCharacter? Local;
        public long IdentitySequence;
        public CollectionSnapshot? Snapshot;
        public bool Reliable;
        public DateTimeOffset Sync;
        public Dictionary<int, int> Deltas = new();
        public HashSet<int> Stale = new();
        public Dictionary<int, int> SpendRanks = new();
    }

    public Engine(string root)
    {
        this.root = root;
        if (OperatingSystem.IsLinux())
            Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        else
            Directory.CreateDirectory(root);
        Settings = AutomaticRecovery.Settings(root);
        Settings.Normalize();
        Book = AutomaticRecovery.Book(root);
        AutomaticRecovery.MigrateLegacy(root, Book);
        var cat = Catalog.Create();
        mappings = new("", cat);
        nearby = new(mappings);
        Tracker = new(Path.Combine(root, "profiles"), cat, mappings, Book.Selected);
        Tracker.Changed += () => dirty = true;
    }

    public void Feed(string flow, byte[] bytes, int source = 1, int destination = 2)
    {
        lock (Gate)
        {
            payloads++;
            lastPayload = DateTimeOffset.UtcNow;
            if (!nodes.TryGetValue(flow, out var node))
            {
                if (nodes.Count >= 8)
                {
                    string? old = nodes.Keys.FirstOrDefault(k => k != Active);
                    if (old != null)
                        nodes.Remove(old);
                }

                node = new() { Wire = new(Catalog.Entries.Select(p => p.Id)) };
                nodes[flow] = node;
                var n = node;
                n.Wire.Frame += (op, f, b) => Frame(flow, n, op, f, b);
                n.Wire.Decoded += e => Event(flow, n, e);
                n.Wire.Faulted += fault =>
                {
                    decoderFaults++;
                    lastFault = fault;
                    Invalidate(flow, false, "Decoder: " + fault.Reason);
                };
            }

            node.Wire.Feed(bytes);
            if (Active == flow)
                Protect?.Invoke(flow, source, destination);
        }
    }

    void Frame(string flow, Node n, byte op, byte family, byte[] body)
    {
        frames++;
        activity.Frame(op, family, Active == flow && Tracker.Ready);
        if (GameProtocol.Local(op, family, body) is { } local)
        {
            if (Active == flow)
                ClearObservations();
            lastIdentity = DateTimeOffset.UtcNow;
            if (n.Local != local)
                Trace("Character", local.Name + " detected");
            bool changed = n.Local != null && (n.Local.Name != local.Name || n.Local.ServerToken != local.ServerToken);
            if (changed)
            {
                // The account snapshot may arrive BEFORE the alt's identity on the same TCP
                // stream. Keep that baseline only for an explicitly shared account. Never
                // restore Reliable here: a real capture gap still needs a fresh snapshot.
                var selected = Book.Selected;
                bool sameAccount = selected?.IsAccount == true && selected.Matches(n.Local!.Name) && (Settings.ShareAllCharacters || selected.Matches(local.Name));
                if (!sameAccount)
                {
                    n.Snapshot = null;
                    n.Deltas.Clear();
                    n.Stale.Clear();
                    n.SpendRanks.Clear();
                    n.Reliable = false;
                }

                if (Active == flow)
                {
                    Tracker.Save();
                    Tracker.Pause();
                }
            }

            n.Local = local;
            n.IdentitySequence = ++identitySequence;
            if (Book.Profiles.Count == 0)
                Execute(new("create", Name: local.Name, AccountName: "My account"));
            Bind(flow, n);
        }

        if (Active != flow || !Tracker.Ready)
            return;
        long ms = Environment.TickCount64;
        var now = DateTimeOffset.UtcNow;
        if (family == 0x56 && op is >= 0x90 and <= 0x9f)
        {
            if (op == 0x91)
            {
                // A fresh response replaces the tab atomically. Unsupported stock must not
                // leave the previous tab looking current. Adjacent message numbers alone
                // are not evidence that every offer disappeared after a purchase.
                if (BlackMarket.Decode(op, family, body, now) is { } stock)
                    market[stock.Tab] = stock;
                else
                    market.Clear();
            }
            else if (op == 0x93)
                market.Clear();
        }

        nearby.NavigationFrame(Settings.ExperimentalDirections, Tracker.LocalEntity ?? 0, op, family, body, now);
        if (family == 0x90)
            activity.Observe(op, family, body, "Bound pet message", now);
        if (GameProtocol.Removal(op, family, body) is { } removal)
        {
            nearby.Remove(removal.Entity);
            bool tracked = Tracker.Remove(removal.Entity, removal.Reason, ms);
            activity.Observe(op, family, body, tracked ? "Pet kill tracked" : removal.Reason != 3 ? "Non-death removal" : "Death without linked participation", now);
            return;
        }

        if (family == 0x36 && op is 0x40 or 0x41 or 0x42)
        {
            if (GameProtocol.Appearance(op, family, body) is { } spawn)
            {
                nearby.Observe(spawn, now);
                var match = mappings.Find(spawn.Npc);
                if (Tracker.Lives.Count >= 4096)
                    foreach (int id in Tracker.Lives.Where(p => p.Value.Ended || ms - p.Value.SpawnMs > 600000).Select(p => p.Key).ToArray())
                        Tracker.Lives.Remove(id);
                if (match != null && Tracker.Lives.Count < 4096)
                    Tracker.Spawn(spawn.Entity, spawn.Npc, mappings.CreatureName(spawn.Npc), ms, match != null);
                if (match == null)
                    Tracker.Lives.Remove(spawn.Entity);
                activity.Observe(op, family, body, match == null ? "NPC without pet link" : "Linked pet spawn", now);
            }
            else
                activity.Observe(op, family, body, "Non-pet or unsupported NPC layout", now);
        }

        if (family == 0x38 && op == 4)
        {
            var hit = GameProtocol.Hit(op, family, body, out string outcome);
            if (hit != null)
            {
                nearby.Refresh(hit.Target, now);
                Tracker.Damage(hit.Actor, hit.Target, hit.Damage, false, ms);
                outcome = hit.Actor != Tracker.LocalEntity ? "Other actor hit" : !Tracker.Lives.ContainsKey(hit.Target) ? "Own hit without pet spawn" : "Own hit on linked NPC";
            }

            activity.Observe(op, family, body, outcome, now);
        }
    }

    void Bind(string flow, Node n)
    {
        var selected = Book.Selected;
        if (selected == null || n.Local == null)
            return;
        // Account membership does not make an older character connection current.
        // Keep the observation watermark even when the newest flow loses its identity.
        if (selected.IsAccount && n.IdentitySequence < identitySequence)
            return;
        if (Settings.ShareAllCharacters && selected.IsAccount)
        {
            if (!selected.Matches(n.Local.Name))
            {
                var candidate = JsonSerializer.Deserialize<LocalProfileBinding>(JsonSerializer.Serialize(selected))!;
                candidate.LinkedCharacters.Add(n.Local.Name);
                candidate.Validate();
                var next = new LocalProfileBook
                {
                    SelectedProfileKey = Book.SelectedProfileKey,
                    Profiles = Book.Profiles.Select(p => p == selected ? candidate : p).ToList()
                };
                next.Save(Path.Combine(root, "profiles.json"));
                selected.LinkedCharacters = candidate.LinkedCharacters;
                Trace("Account", n.Local.Name + " joined the shared collection");
            }
        }

        if (!selected.Matches(n.Local.Name) || !selected.IsAccount && selected.ServerToken is int token && n.Local.ServerToken != 0 && token != n.Local.ServerToken)
        {
            if (Active == flow)
            {
                Tracker.Save();
                Tracker.Pause();
                ClearObservations();
                Active = null;
            }

            return;
        }

        if (Active != null && Active != flow)
        {
            if (nodes.TryGetValue(Active, out var old))
                old.Reliable = false;
            Tracker.Save();
            Tracker.Pause();
        }

        if (!selected.IsAccount && selected.ServerToken == null && n.Local.ServerToken != 0)
        {
            selected.ServerToken = n.Local.ServerToken;
            Book.Save(Path.Combine(root, "profiles.json"));
        }

        if (Active != flow)
            ClearObservations();
        Active = flow;
        Tracker.LocalPlayer(n.Local.Entity, n.Local.Name);
        if (n.Reliable && n.Snapshot != null)
        {
            Tracker.ImportCollection(n.Snapshot, n.Sync, n.Deltas, n.Stale, n.SpendRanks);
            LastSync = n.Sync;
            // The imported snapshot is already retained in memory. A disk write failure
            // must not abort decoding the rewards that follow it in this TCP payload.
            // Keep identity/account-switch saves strict; only this completed import can
            // safely defer persistence to the existing maintenance retry.
            try
            {
                Tracker.Save();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                dirty = true;
                MaintenanceResult(error);
            }
        }
    }

    void Event(string flow, Node n, CollectionWireEvent e)
    {
        bool bound = Active == flow && Tracker.Ready;
        if (e.Snapshot is { } snapshot)
        {
            n.Snapshot = snapshot;
            n.Sync = DateTimeOffset.UtcNow;
            n.Deltas.Clear();
            n.Stale.Clear();
            n.SpendRanks.Clear();
            n.Reliable = true;
            Trace("Collection", $"Validated snapshot: {snapshot.Ranks.Count} owned pets");
            if (n.Local != null)
                Bind(flow, n);
            return;
        }

        if (e.Rank is int rank)
        {
            int previous = n.Snapshot?.Ranks.GetValueOrDefault(e.PetId) ?? 0;
            // An unchanged rank is only a repeated observation. Keep the deduction's
            // original rank until an actual transition arrives, including across alts.
            bool spentFirst = previous != rank && n.SpendRanks.Remove(e.PetId, out int spentRank) && n.Reliable && rank == spentRank + 1;
            if (n.Snapshot is { } old)
                n.Snapshot = new(old.EquippedPet, new Dictionary<int, int>(old.Ranks) { [e.PetId] = rank }, old.Souls);
            if (spentFirst)
                n.Stale.Remove(e.PetId);
            else if (previous != rank)
                n.Stale.Add(e.PetId);
            if (bound)
            {
                Tracker.Roster(new[] { (e.PetId, rank) });
                if (spentFirst)
                    RestoreLedger(n);
            }
        }
        else if (e.Spent is int spent)
        {
            bool ledgerBalanced = false;
            if (n.Reliable && n.Snapshot != null)
            {
                int before = n.Snapshot.Souls.GetValueOrDefault(e.PetId) + n.Deltas.GetValueOrDefault(e.PetId);
                if (before < spent)
                {
                    n.Stale.Add(e.PetId);
                    n.SpendRanks.Remove(e.PetId);
                }
                else
                {
                    int rankBeforeSpend = n.Snapshot.Ranks.GetValueOrDefault(e.PetId);
                    if (PetStages.CounterGoal(rankBeforeSpend) == spent)
                        n.SpendRanks[e.PetId] = rankBeforeSpend;
                    else
                        n.SpendRanks.Remove(e.PetId);
                    n.Deltas[e.PetId] = checked(n.Deltas.GetValueOrDefault(e.PetId) - spent);
                    n.Stale.Remove(e.PetId);
                    ledgerBalanced = true;
                }
            }

            if (bound && n.Reliable)
            {
                bool applied = Tracker.ObservePointsSpent(e.PetId, spent, DateTimeOffset.UtcNow);
                if (!applied && ledgerBalanced)
                    RestoreLedger(n);
            }
        }
        else if (e.Delta > 0)
        {
            rewards++;
            lastReward = DateTimeOffset.UtcNow;
            if (n.Reliable && n.Snapshot != null)
                n.Deltas[e.PetId] = checked(n.Deltas.GetValueOrDefault(e.PetId) + e.Delta);
            if (bound)
            {
                if (n.Reliable && n.Snapshot != null)
                    Tracker.ObservePointUpdate(e.PetId, e.Delta, DateTimeOffset.UtcNow);
                else
                    Tracker.ObserveSoulActivity(e.PetId, e.Delta, DateTimeOffset.UtcNow);
            }
        }

        if (bound)
            dirty = true;
    }

    // Recover only from the same uninterrupted, validated account ledger. An alt
    // can clear the presentation's transient balance while rank/spend messages split.
    void RestoreLedger(Node n)
    {
        if (n.Reliable && n.Snapshot != null)
            Tracker.ImportCollection(n.Snapshot, n.Sync, n.Deltas, n.Stale, n.SpendRanks);
    }

    void Invalidate(string flow, bool restarted, string reason)
    {
        if (nodes.TryGetValue(flow, out var n))
        {
            n.Reliable = false;
            n.SpendRanks.Clear();
            if (restarted)
            {
                n.Local = null;
                n.Snapshot = null;
                n.Deltas.Clear();
                n.Stale.Clear();
                n.SpendRanks.Clear();
            }
        }

        if (flow == Active)
        {
            ClearObservations();
            activeInterruptions++;
            Trace("Interruption", reason);
            lastInterruption = reason;
            Tracker.InvalidateProgress();
            Tracker.Save();
            if (restarted)
            {
                Tracker.Pause();
                ClearObservations();
                Active = null;
                Release?.Invoke();
            }
        }
        else
            backgroundInterruptions++;
    }

    public void RetireFlow(string flow)
    {
        lock (Gate)
        {
            if (flow == Active)
                return;
            nodes.Remove(flow);
            retiredFlows++;
        }
    }

    public void Gap(string flow, bool restarted = false, string reason = "Game stream continuity lost")
    {
        lock (Gate)
        {
            if (nodes.TryGetValue(flow, out var n))
                n.Wire.Reset();
            Invalidate(flow, restarted, reason);
        }
    }

    public void StopCapture(string reason = "Capture restarted")
    {
        lock (Gate)
        {
            Trace("Interruption", reason);
            lastInterruption = reason;
            foreach (var n in nodes.Values)
            {
                n.Reliable = false;
                n.Wire.Reset();
            }

            Tracker.InvalidateProgress();
            Tracker.Save();
            Tracker.Pause();
            ClearObservations();
            Active = null;
            nodes.Clear();
            Release?.Invoke();
        }
    }

    public void GameGeneration(long generation)
    {
        lock (Gate)
        {
            if (gameGeneration.HasValue && gameGeneration != generation)
                StopCapture("Game process changed");
            gameGeneration = generation;
        }
    }

    public void Flush()
    {
        lock (Gate)
        {
            if (dirty)
            {
                Tracker.Save();
                dirty = false;
            }

            if (Environment.TickCount64 >= nextBackup)
            {
                Backup();
                nextBackup = Environment.TickCount64 + 300000;
            }
        }
    }

    string maintenanceError = "";
    public void MaintenanceResult(Exception? error)
    {
        lock (Gate)
        {
            string message = error == null ? "" : error is IOException or UnauthorizedAccessException ? "Your latest changes could not be saved. Retrying automatically; check free disk space and folder access if this continues." : "Background tracking checks were interrupted. Retrying automatically.";
            if (message != maintenanceError)
                Trace("Maintenance", message.Length == 0 ? "Background checks and saving recovered" : message);
            maintenanceError = message;
        }
    }

    public WorkerView View(bool running, int devices, long rx, long drops, long gaps, bool game)
    {
        lock (Gate)
        {
            Profile profile = Tracker.Ready ? Tracker.Profile : Saved();
            bool synced = Tracker.Ready && Active != null && nodes[Active].Reliable && nodes[Active].Snapshot != null;
            var selected = Book.Selected;
            var detected = nodes.Values.Where(n => n.Local != null).OrderByDescending(n => n.IdentitySequence).FirstOrDefault()?.Local;
            bool wrongName = selected != null && detected != null && !selected.Matches(detected.Name);
            bool wrongServer = selected?.IsAccount == false && selected.ServerToken is int token && detected != null && detected.ServerToken != 0 && token != detected.ServerToken;
            bool firstRun = Book.Profiles.Count == 0;
            string accountStatus = firstRun ? (game ? "Waiting for character login" : "Waiting for Aion 2") : "Choose your account";
            string accountDetail = firstRun ? (game ? "Log into your character to load pet ranks and soul totals automatically. If you are already playing, return to character selection and log in again. No account setup is needed." : "Open Aion 2 and log into your character. Pet Tracker will set up your collection automatically.") : "Open Settings > Account > Manage accounts to choose your saved account.";
            string status = CaptureError.Length > 0 ? "Capture unavailable" : !running ? "Capture stopped" : selected == null ? accountStatus : !game ? "Waiting for Aion 2" : !Tracker.Ready ? wrongName ? selected.IsAccount ? $"{detected!.Name} — link to account" : $"{detected!.Name} detected — choose profile" : wrongServer ? "Character server differs" : "Waiting for your character…" : !synced ? "Collection sync needed" : "Live";
            string detail = CaptureError.Length > 0 ? CaptureError : !running ? "Open Settings > Help > Advanced troubleshooting to restart capture." : selected == null ? accountDetail : !game ? "Open Aion 2 to connect your character." : !Tracker.Ready ? wrongName ? $"Selected account: {selected.Label}. Open Settings > Account > Manage accounts to link {detected!.Name} or select another account. Pet collections are account-wide." : wrongServer ? $"The detected server differs from {selected.Label}. Choose this character's account in Settings > Account > Manage accounts." : "Waiting for your character record. If you are already in game, return to character selection and log in again. " + (Settings.ShareAllCharacters ? "New characters join your selected account automatically." : "You can link a new character in Settings > Account > Manage accounts.") : !synced ? "Your saved collection is available. Start Pet Tracker before the next full game login to sync current totals." : "Connected to " + Tracker.CharacterName + ". Collection synchronized. " + (lastReward.HasValue ? "Last soul reward: " + lastReward.Value.ToLocalTime().ToString("T") + "." : "Waiting for pet activity.");
            if (!synced && Tracker.Ready)
                detail = "Connected to " + Tracker.CharacterName + ". Recent pet rewards remain visible; totals are last-known until a fresh collection arrives." + (lastInterruption.Length > 0 ? " Last interruption: " + lastInterruption : " Start tracking before the next full game login to synchronize totals.");
            if (maintenanceError.Length > 0)
            {
                status = "Background retry needed";
                detail = maintenanceError;
            }

            return new(3, profile, Tracker.Ready, status, detail, Tracker.CharacterName, Book.Profiles, Book.SelectedProfileKey, Tracker.RecentActivities.ToList(), Tracker.LastRankIncrease, LastSync, devices, rx, drops, gaps, Settings, DetectedCharacter: detected?.Name, Health: Health(), WorkerProcessId: Environment.ProcessId, Nearby: Tracker.Ready ? nearby.View(DateTimeOffset.UtcNow, Settings.NearbyStaleSeconds, Settings.ExperimentalDirections) : Array.Empty<NearbyPet>(), Market: running && game && CaptureError.Length == 0 && Tracker.Ready ? market.Values.Where(s => s.Fresh(DateTimeOffset.UtcNow)).ToArray() : Array.Empty<MarketStock>(), CollectionSynced: synced && running && game && CaptureError.Length == 0, Farming: Tracker.Ready ? Tracker.Farming.View() : null);
        }
    }

    public void WriteHealth(bool running, int devices, long received, long dropped, long gaps, bool game)
    {
        lock (Gate)
        {
            try
            {
                var view = View(running, devices, received, dropped, gaps, game);
                Storage.Save(Path.Combine(root, "connection-status.json"), new { Version = typeof(Engine).Assembly.GetName().Version?.ToString(), RecordedUtc = DateTimeOffset.UtcNow, view.Status, view.Detail, view.Ready, view.Character, view.DetectedCharacter, Account = Book.Selected?.Label, view.Devices, view.Received, view.Dropped, view.Gaps, view.LastSync, RecentPets = view.Recent.Count, NearbyPets = view.Nearby?.Count ?? 0, view.Settings.TrackerMode, view.Health, Navigation = Settings.ExperimentalDirections ? nearby.NavigationHealth(DateTimeOffset.UtcNow) : null });
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    Profile Saved()
    {
        var p = Book.Selected;
        Profile profile = p == null ? new() : AutomaticRecovery.Profile(root, p);
        Tracker.Validate(profile);
        if (p != null && !Tracker.OwnedBy(profile, p))
            throw new InvalidDataException("The saved collection belongs to another profile.");
        var result = new Profile
        {
            AccountKey = profile.AccountKey,
            CharacterName = profile.CharacterName,
            Creatures = profile.Creatures,
            Pets = Catalog.Create()
        };
        foreach (var pet in profile.Pets)
            result.Pets[pet.Key] = pet.Value;
        return result;
    }

    public void Execute(Command cmd)
    {
        lock (Gate)
        {
            switch (cmd.Action)
            {
                case "status":
                case "adapters":
                    break;
                case "activity-check":
                    activity.Start(DateTimeOffset.UtcNow);
                    Trace("Activity check", "Recording supported combat and pet message samples for 90 seconds; no chat or login messages");
                    break;
                case "reset-farming":
                    Tracker.Farming.Reset();
                    break;
                case "farming-goal":
                    Tracker.SetFarmingGoal(cmd.PetId ?? 0, cmd.TargetRank);
                    break;
                case "settings":
                    SaveSettings(cmd.Settings ?? throw new InvalidDataException("Missing settings."));
                    break;
                case "patch":
                    SaveSettings(SettingsPatch.Apply(Settings, cmd.Patch ?? throw new InvalidDataException("Missing settings changes")));
                    break;
                case "pin":
                case "unpin":
                    SaveSettings(PetPins.Change(Settings, cmd.PetId, cmd.Action == "pin"));
                    break;
                case "create":
                    var b = new LocalProfileBinding
                    {
                        AccountName = (cmd.AccountName ?? "").Trim(),
                        CharacterName = (cmd.Name ?? "").Trim(),
                        RealmLabel = (cmd.Realm ?? "").Trim(),
                        ProfileKey = Guid.NewGuid().ToString("N")[..24]
                    };
                    b.Validate();
                    if (Book.Profiles.Any(p => p.Label.Equals(b.Label, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("That character profile already exists.");
                    Select(b);
                    break;
                case "link":
                    LinkDetected(cmd);
                    break;
                case "select":
                    Select(Book.Profiles.Single(p => p.ProfileKey == cmd.Key));
                    break;
                case "import":
                    Import(cmd);
                    break;
                case "restart":
                    CaptureAction?.Invoke("restart");
                    break;
                case "backup":
                    Backup();
                    break;
                case "details":
                    SaveDetails(cmd);
                    break;
                case "rename":
                    Rename(cmd);
                    break;
                case "edit":
                    if (cmd.Profile == null || !Tracker.Ready)
                        throw new InvalidDataException("Connect your character before editing progress.");
                    foreach (var pet in cmd.Profile.Pets.Values)
                    {
                        if (!Tracker.Catalog.ContainsKey(pet.Id))
                            throw new InvalidDataException("Unknown pet.");
                        Tracker.EditPet(pet.Id, pet.DisplayName, pet.Level, pet.Souls, pet.Goal, pet.Notes);
                    }

                    Tracker.Save();
                    break;
                case "exit":
                    Tracker.Save();
                    ExitRequested = true;
                    break;
                default:
                    throw new InvalidDataException("Unknown request.");
            }
        }
    }

    void SaveSettings(AppSettings next)
    {
        next.Normalize();
        bool enable = next.ShareAllCharacters && !Settings.ShareAllCharacters;
        if (enable)
            Backup();
        Storage.Save(Path.Combine(root, "settings.json"), next);
        Settings = next;
        if (enable)
        {
            var latest = nodes.Where(p => p.Value.Local != null).OrderByDescending(p => p.Value.IdentitySequence).FirstOrDefault();
            if (latest.Value != null)
                Bind(latest.Key, latest.Value);
        }
    }

    void LinkDetected(Command cmd)
    {
        var selected = Book.Selected ?? throw new InvalidOperationException("Choose an account profile first.");
        var detected = nodes.Values.Where(n => n.Local != null).OrderByDescending(n => n.IdentitySequence).FirstOrDefault()?.Local;
        if (detected == null || !string.Equals(detected.Name, cmd.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The detected character changed. Open Profile again before linking it.");
        if (cmd.Key != null)
            selected = Book.Profiles.SingleOrDefault(p => p.ProfileKey == cmd.Key) ?? throw new InvalidDataException("The chosen account no longer exists.");
        string account = selected.IsAccount ? selected.AccountName : (cmd.AccountName ?? "My account").Trim();
        if (string.IsNullOrWhiteSpace(account) || account.Length > 80 || Book.Profiles.Any(p => p != selected && p.Label.Equals(account, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Choose a unique account label of up to 80 characters.");
        var candidate = JsonSerializer.Deserialize<LocalProfileBinding>(JsonSerializer.Serialize(selected))!;
        candidate.AccountName = account;
        candidate.ServerToken = null;
        if (!candidate.Matches(detected.Name))
            candidate.LinkedCharacters.Add(detected.Name);
        candidate.Validate();
        Backup();
        Select(candidate);
        Trace("Account", detected.Name + " linked to " + candidate.Label);
    }

    void Rename(Command cmd)
    {
        var target = Book.Profiles.SingleOrDefault(p => p.ProfileKey == cmd.Key) ?? throw new InvalidDataException("Choose an account first.");
        string label = (cmd.AccountName ?? "").Trim();
        if (label.Length is < 1 or > 80 || Book.Profiles.Any(p => p != target && p.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Enter a unique account label of up to 80 characters.");
        var candidate = JsonSerializer.Deserialize<LocalProfileBinding>(JsonSerializer.Serialize(target))!;
        candidate.AccountName = label;
        BookWith(candidate, Book.SelectedProfileKey).Save(Path.Combine(root, "profiles.json"));
        target.AccountName = label;
    }

    void SaveDetails(Command cmd)
    {
        if (cmd.Profile == null || Book.Selected == null)
            throw new InvalidDataException("Choose an account before editing local details.");
        var target = Tracker.Ready ? Tracker.Profile : Saved();
        foreach (var pet in cmd.Profile.Pets.Values)
            if (!target.Pets.ContainsKey(pet.Id) || pet.DisplayName.Length > 80 || pet.Notes.Length > 2048)
                throw new InvalidDataException("Invalid pet details.");
        foreach (var pet in cmd.Profile.Pets.Values)
        {
            var saved = target.Pets[pet.Id];
            saved.DisplayName = pet.DisplayName.Trim();
            saved.Notes = pet.Notes.Replace("[favorite]", "").Trim();
            saved.Favorite = pet.Favorite;
        }

        if (Tracker.Ready)
            Tracker.Save();
        else
        {
            target.AccountKey = Book.Selected.IsAccount ? Book.SelectedProfileKey : null;
            Storage.Save(Path.Combine(root, "profiles", Book.SelectedProfileKey + ".json"), target);
        }
    }

    LocalProfileBook BookWith(LocalProfileBinding binding, string? selected)
    {
        var profiles = Book.Profiles.Select(p => p.ProfileKey == binding.ProfileKey ? binding : p).ToList();
        if (!profiles.Any(p => p.ProfileKey == binding.ProfileKey))
            profiles.Add(binding);
        return new() { Profiles = profiles, SelectedProfileKey = selected };
    }

    void Select(LocalProfileBinding b)
    {
        var candidate = BookWith(b, b.ProfileKey);
        candidate.Validate();
        // Persist both the outgoing rewards and requested account choice before
        // pausing the current stream or publishing any new membership in memory.
        Tracker.Save();
        candidate.Save(Path.Combine(root, "profiles.json"));
        Book.Profiles = candidate.Profiles;
        Book.SelectedProfileKey = candidate.SelectedProfileKey;
        ClearObservations();
        Tracker.Pause();
        Tracker.SelectLocalProfile(b);
        Active = null;
        var pair = nodes.Where(p => p.Value.Local != null && (b.Matches(p.Value.Local.Name) || Settings.ShareAllCharacters && b.IsAccount) && (b.IsAccount || b.ServerToken == null || p.Value.Local.ServerToken == 0 || p.Value.Local.ServerToken == b.ServerToken)).OrderByDescending(p => p.Value.IdentitySequence).FirstOrDefault();
        if (pair.Value != null)
            Bind(pair.Key, pair.Value);
    }

    void Import(Command cmd)
    {
        var bindings = cmd.Bindings ?? throw new InvalidDataException("No profiles selected.");
        var imports = cmd.Imports ?? throw new InvalidDataException("No collection data supplied.");
        if (bindings.Count is < 1 or > 100 || bindings.Select(b => b.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count() != bindings.Count)
            throw new InvalidDataException("Invalid or duplicate profiles.");
        foreach (var b in bindings)
        {
            b.Validate();
            if (!imports.TryGetValue(b.ProfileKey, out var p) || !Tracker.OwnedBy(p, b))
                throw new InvalidDataException("Profile identity does not match its collection.");
            Tracker.Validate(p);
            if (p.Pets.Keys.Any(id => !Tracker.Catalog.ContainsKey(id)))
                throw new InvalidDataException("This collection uses unsupported pet IDs.");
            if (Book.Profiles.Any(old => old.Label.Equals(b.Label, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("A matching profile already exists. Use a separate profile label to avoid overwriting it.");
        }

        Backup();
        var candidate = new LocalProfileBook { Profiles = new(Book.Profiles), SelectedProfileKey = Book.SelectedProfileKey };
        foreach (var b in bindings)
        {
            var p = imports[b.ProfileKey];
            foreach (var pet in p.Pets.Values)
                if (!pet.Max)
                    pet.ProgressNeedsSync = true;
            var target = new LocalProfileBinding
            {
                AccountName = b.AccountName,
                LinkedCharacters = new(b.LinkedCharacters),
                CharacterName = b.CharacterName,
                RealmLabel = b.RealmLabel,
                ProfileKey = Guid.NewGuid().ToString("N")[..24]
            };
            if (target.IsAccount)
                p.AccountKey = target.ProfileKey;
            Storage.Save(Path.Combine(root, "profiles", target.ProfileKey + ".json"), p);
            candidate.Profiles.Add(target);
        }

        candidate.Save(Path.Combine(root, "profiles.json"));
        Book.Profiles = candidate.Profiles;
        if (Book.Selected == null)
            Select(Book.Profiles.Last());
    }

    public void Backup()
    {
        lock (Gate)
        {
            Tracker.Save();
            string parent = Path.GetFullPath(Path.Combine(root, "backups"));
            Directory.CreateDirectory(parent);
            string path = Path.Combine(parent, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(path, "profiles"));
            foreach (string name in new[] { "profiles.json", "settings.json" })
                if (File.Exists(Path.Combine(root, name)))
                    File.Copy(Path.Combine(root, name), Path.Combine(path, name));
            foreach (var b in Book.Profiles)
            {
                string file = Path.Combine(root, "profiles", b.ProfileKey + ".json");
                if (File.Exists(file))
                    File.Copy(file, Path.Combine(path, "profiles", b.ProfileKey + ".json"));
            }

            Storage.Save(Path.Combine(path, "backup.json"), new { Product = "PetTracker", Format = 1 });
            var managed = Directory.GetDirectories(parent).Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), "^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$") && File.Exists(Path.Combine(d, "backup.json"))).OrderByDescending(Directory.GetCreationTimeUtc).ToArray();
            foreach (string old in managed.Skip(7))
                PruneBackup(parent, old);
            Storage.Save(Path.Combine(parent, "index.json"), managed.Take(7).Select(Path.GetFileName).ToArray());
        }
    }

    static void PruneBackup(string parent, string folder)
    {
        folder = Path.GetFullPath(folder);
        if (!folder.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            return;
        using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "backup.json")));
        if (marker.RootElement.GetProperty("Product").GetString() != "PetTracker")
            return;
        var book = LocalProfileBook.Load(Path.Combine(folder, "profiles.json"));
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "profiles.json", "settings.json", "backup.json" };
        foreach (var p in book.Profiles)
            allowed.Add(Path.Combine("profiles", p.ProfileKey + ".json"));
        var directories = Directory.GetDirectories(folder);
        if (directories.Any(d => Path.GetFileName(d) != "profiles" || (File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0))
            return;
        var files = Directory.GetFiles(folder).Concat(directories.SelectMany(d => Directory.GetFiles(d))).ToArray();
        if (directories.Any(d => Directory.GetDirectories(d).Length > 0) || files.Any(f => !allowed.Contains(Path.GetRelativePath(folder, f))))
            return;
        foreach (string f in files)
            File.Delete(f);
        foreach (string d in directories)
            Directory.Delete(d);
        Directory.Delete(folder);
    }

    public void Dispose()
    {
        lock (Gate)
            Tracker.Save();
    }
}
