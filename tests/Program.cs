using PetTracker;

using System.Text;

using System.Text.Json;

using System.Buffers.Binary;

int checks = 0;
void Check(bool yes, string message)
{
    checks++;
    if (!yes)
        throw new Exception(message);
}

foreach (int oldKey in new[] { 0x50, 0x54, 0x4f })
{
    var oldShortcut = JsonSerializer.Deserialize<AppSettings>("{\"Hotkey\":" + oldKey + "}")!;
    oldShortcut.Normalize();
    Check(oldShortcut.Hotkey == oldKey && oldShortcut.HotkeyModifiers == 3, "Legacy shortcuts retain Ctrl+Alt after loading old settings");
}
foreach (int modifiers in Enumerable.Range(0, 8))
{
    var customShortcut = SettingsPatch.Apply(new(), SettingsPatch.Fields((nameof(AppSettings.Hotkey), 0x4b), (nameof(AppSettings.HotkeyModifiers), modifiers)));
    var reloaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(customShortcut))!;
    reloaded.Normalize();
    Check(reloaded.Hotkey == 0x4b && reloaded.HotkeyModifiers == modifiers, "Custom letter shortcuts preserve every Ctrl/Alt/Shift combination on reload");
}
foreach (int key in new[] { 0x08, 0x09, 0x2d, 0x30, 0x60, 0x6b, 0x70, 0x87, 0xba, 0xe2 })
{
    var shortcut = new AppSettings { Hotkey = key, HotkeyModifiers = 4 };
    shortcut.Normalize();
    Check(shortcut.Hotkey == key, "Editing, navigation, number, numpad, function and punctuation keys survive normalization");
}
foreach (var shortcut in new[] { new TrackerShortcut(0x10, 2), new TrackerShortcut(0xa3, 1), new TrackerShortcut(0x7b, 0), new TrackerShortcut(0x50, 16), new TrackerShortcut(500, 3) })
{
    var options = new AppSettings { Hotkey = shortcut.Key, HotkeyModifiers = shortcut.Modifiers };
    options.Normalize();
    Check(options.Hotkey == 0, "Invalid, modifier-only and reserved F12 shortcuts are disabled");
}
var resetShortcut = SettingsPatch.Apply(new() { Hotkey = 0x4b, HotkeyModifiers = 6 }, SettingsSections.Defaults("Overlay"));
Check(resetShortcut.Hotkey == 0 && resetShortcut.HotkeyModifiers == 3, "Overlay reset clears the complete custom shortcut");

byte[] V(int n)
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

byte[] Frame(byte op, byte family, byte[] b) => V(b.Length + 6).Concat(new[] { op, family }).Concat(b).ToArray();

byte[] Identity(string name, int token = 0)
{
    using var s = new MemoryStream();
    using var w = new BinaryWriter(s);
    w.Write(V(721));
    w.Write(0);
    w.Write((byte)7);
    var utf = Encoding.UTF8.GetBytes(name);
    w.Write((byte)utf.Length);
    w.Write(utf);
    w.Write((ushort)token);
    w.Write((ushort)0);
    return Frame(0x33, 0x36, s.ToArray());
}

byte[] Snapshot(Dictionary<int, int> ranks, Dictionary<int, int> counters)
{
    using var s = new MemoryStream();
    using var w = new BinaryWriter(s);
    w.Write(0);
    w.Write(V(ranks.Count));
    foreach (var p in ranks)
    {
        w.Write(p.Key);
        w.Write(p.Key);
        w.Write(p.Value);
    }
    w.Write(V(counters.Count));
    foreach (var p in counters)
    {
        w.Write(p.Key);
        w.Write(p.Value);
    }
    return Frame(0, 0x90, s.ToArray());
}

byte[] Points(byte op, params (int Id, int Amount)[] points)
{
    using var s = new MemoryStream();
    using var w = new BinaryWriter(s);
    w.Write(V(points.Length));
    foreach (var p in points)
    {
        w.Write(p.Id);
        w.Write(p.Amount);
    }
    return Frame(op, 0x90, s.ToArray());
}

byte[] Rank(int id, int rank)
{
    var b = new byte[23];
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(2), id);
    BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(6), rank);
    return Frame(0x14, 0x90, b);
}

string root = Path.Combine(Path.GetFullPath(args[0]), "case-" + Guid.NewGuid().ToString("N"));
using var e = new Engine(root);
e.Execute(new("create", Name: "Zero", Realm: "Siel"));

var initial = Snapshot(new() { [1007] = 1, [1060] = 1, [1086] = 2, [1141] = 2, [1214] = 2, [1163] = 1 }, new() { [1007] = 24, [1060] = 24, [1086] = 45, [1141] = 74, [1214] = 74 });

string firstRunRoot = Path.Combine(root, "first-run");
string firstRunKey;
using (var feedback = new Engine(Path.Combine(root, "nearby-progress")))
{
    feedback.Feed("progress", Snapshot(new()
    {
        [1007] = 1
    }, new()
    {
        [1007] = 5
    }));
    feedback.Feed("progress", Identity("ProgressTest", 5010));
    Check(!feedback.Tracker.RecentActivities.Any(a => a.SoulRewardUtc.HasValue), "Login collection does not masquerade as recent progress");
    feedback.Feed("progress", Points(0x0d, (1007, 5)));
    var activity = feedback.Tracker.RecentActivities.Single(a => a.PetId == 1007);
    Check(activity.SoulRewardUtc.HasValue && feedback.Tracker.Profile.Pets[1007].CurrentSouls == 10, "Decoded soul reward supplies Nearby feedback and the updated total");

}
using (var firstRun = new Engine(firstRunRoot))
{
    var beforeGame = firstRun.View(true, 1, 0, 0, 0, false);
    Check(beforeGame.Status == "Waiting for Aion 2" && beforeGame.Detail.Contains("automatically") && firstRun.Book.Profiles.Count == 0, "Fresh installation waits for the game without asking for an account or inventing one");
    var beforeLogin = firstRun.View(true, 1, 0, 0, 0, true);
    Check(beforeLogin.Status == "Waiting for character login" && beforeLogin.Detail.Contains("character selection") && !beforeLogin.Detail.Contains("Manage accounts"), "Already-running game gets an actionable first-login prompt, not account selection");
    Check(firstRun.View(false, 0, 0, 0, 0, true).Status == "Capture stopped", "First-run guidance does not mask stopped capture");
    firstRun.CaptureError = "Test capture failure";
    Check(firstRun.View(true, 0, 0, 0, 0, true).Status == "Capture unavailable", "First-run guidance does not mask capture failures");
    firstRun.CaptureError = "";
    firstRun.Feed("login", initial);
    Check(firstRun.Book.Profiles.Count == 0 && !firstRun.Tracker.Ready, "Collection alone does not fabricate an account identity");
    firstRun.Feed("login", Identity("NewPlayer", 5001));
    firstRunKey = firstRun.Book.SelectedProfileKey!;
    Check(firstRun.Book.Profiles.Count == 1 && firstRun.Book.Selected!.AccountName == "My account" && firstRun.Tracker.Ready, "First detected character creates and selects its account without user commands");
    Check(firstRun.View(true, 1, 0, 0, 0, true).Status == "Live" && firstRun.Tracker.Profile.Pets[1086].Level == 2 && firstRun.Tracker.Profile.Pets[1086].CurrentSouls == 45, "First login imports ranks and souls without manual setup");
    firstRun.Feed("login", Identity("NewPlayer", 5001));
    Check(firstRun.Book.Profiles.Count == 1 && firstRun.Book.SelectedProfileKey == firstRunKey, "Repeated identity does not create duplicate accounts");
}
using (var reopened = new Engine(firstRunRoot))
{
    Check(reopened.Book.SelectedProfileKey == firstRunKey && reopened.View(true, 1, 0, 0, 0, false).Status == "Waiting for Aion 2", "Restart retains the automatically selected account");
    reopened.Book.SelectedProfileKey = null;
    Check(reopened.View(true, 1, 0, 0, 0, true).Status == "Choose your account", "Existing unselected collections still require an explicit choice");
}

e.Feed("game", initial);
Check(!e.Tracker.Ready, "A snapshot alone must not fabricate character identity");
e.Feed("unrelated", Identity("Other"));
Check(!e.Tracker.Ready, "Another name must not bind the profile");

var identity = Identity("Zero", 5001);
for (int i = 0; i < identity.Length; i++) e.Feed("game", new[] { identity[i] });
Check(e.Tracker.Ready, "Fragmented independent character identity must bind");
Check(e.Tracker.LocalEntity == 721, "Character entity must match decoded record");
Check(e.Tracker.Profile.Pets[1086].CurrentSouls == 45, "Initial collection imports exact Fungen total");
Check(e.Tracker.Profile.Pets[1163].CurrentSouls == 0 && e.Tracker.Profile.Pets[1163].Level == 1 && e.Tracker.Profile.Pets[1163].Goal == 25, "Omitted owned counter means zero, not unowned");
Check(e.Book.Selected!.ServerToken == 5001, "Observed server discriminator stays scoped to selected profile");

e.Feed("game", Points(0xd, (1007, 1), (1060, 2)));
Check(e.Tracker.Profile.Pets[1007].CurrentSouls == 25, "Batched Potcrab reward applied");
Check(e.Tracker.Profile.Pets[1060].CurrentSouls == 25, "Batched Starturtle reward applied");

e.Feed("game", Rank(1007, 2));
e.Feed("game", Points(0xe, (1007, 25)));
Check(e.Tracker.Profile.Pets[1007].CurrentSouls == 0 && e.Tracker.Profile.Pets[1007].Goal == 75, "Potcrab upgrade deducts old-stage points");
e.Feed("game", Points(0xd, (1007, 3)));
Check(e.Tracker.Profile.Pets[1007].CurrentSouls == 3, "Potcrab subsequent rewards advance from zero");

e.Feed("game", Rank(1060, 2));
e.Feed("game", Points(0xe, (1060, 25)));
Check(e.Tracker.Profile.Pets[1060].CurrentSouls == 1, "Over-threshold batch preserves one carried soul");
e.Feed("game", Points(0xd, (1060, 4)));
Check(e.Tracker.Profile.Pets[1060].CurrentSouls == 5, "Starturtle advances after upgrade");

e.Feed("game", Points(0xd, (1214, 1)));
Check(e.Tracker.Profile.Pets[1214].Max, "Final uninterrupted soul derives MAX");
e.Feed("game", Rank(1141, 3));
Check(e.Tracker.Profile.Pets[1141].Max, "Native MAX repairs a missed final reward");

int fungen = e.Tracker.Profile.Pets[1086].CurrentSouls!.Value;
e.Feed("game", Points(0xd, (1086, 1), (999999, 1)));
Check(e.Tracker.Profile.Pets[1086].CurrentSouls == fungen, "Malformed batch cannot partially apply");
e.Feed("otherflow", Points(0xd, (1086, 1)));
Check(e.Tracker.Profile.Pets[1086].CurrentSouls == fungen, "Unbound flow cannot advance points");

e.Gap("game");
e.Feed("game", Points(0xd, (1086, 1)));
Check(e.Tracker.Profile.Pets[1086].CurrentSouls == fungen && e.Tracker.Profile.Pets[1086].ProgressNeedsSync, "Gap freezes old balances");
e.Feed("game", Snapshot(new() { [1086] = 2 }, new() { [1086] = 50 }));
Check(e.Tracker.Profile.Pets[1086].CurrentSouls == 50 && !e.Tracker.Profile.Pets[1086].ProgressNeedsSync, "Fresh snapshot restores accurate totals");

e.Feed("game", Points(0xd, (1086, 1)));
var before = JsonSerializer.Serialize(e.Tracker.Profile);
var settings = new AppSettings { ExpireInactive = true, InactiveSeconds = 20 };
Check(!Activity.Select(e.Tracker, settings, DateTimeOffset.UtcNow.AddSeconds(21)).Any(), "Expired rows disappear");
Check(JsonSerializer.Serialize(e.Tracker.Profile) == before, "Expiry never changes saved collection");
settings.ExpireInactive = false;
Check(Activity.Select(e.Tracker, settings, DateTimeOffset.UtcNow.AddSeconds(60)).Any(), "Disabling expiry reveals retained activities");

e.Feed("game", Identity("Zero", 5002));
Check(!e.Tracker.Ready, "Same-name different-server identity cannot write existing profile");
e.Execute(new("create", Name: "Zero", Realm: "Other server"));
e.Feed("game", Snapshot(new() { [1086] = 1 }, new() { [1086] = 7 }));
Check(e.Tracker.Ready && e.Tracker.Profile.Pets[1086].CurrentSouls == 7, "Separate same-name/server profile is supported");

var second = new Engine(root);
Check(second.Book.Profiles.Count == 2, "Profiles persist independently");
Check(second.View(false, 0, 0, 0, 0, false).Profile.Pets[1086].CurrentSouls == 7, "Offline preview preserves last known data");
Check(!second.Tracker.Ready, "Saved state is not a fresh game identity");
second.Dispose();

var reassembly = new TcpReassembler();
reassembly.Reset(100);
Check(reassembly.Feed(103, new byte[] { 4, 5 }, TimeSpan.Zero).Chunks.Count == 0, "Future segment buffers");
var merged = reassembly.Feed(100, new byte[] { 1, 2, 3 }, TimeSpan.FromMilliseconds(5));
Check(merged.Chunks.SelectMany(x => x).SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "Reordered TCP merges once");
Check(reassembly.Feed(100, new byte[] { 1, 2, 3 }, TimeSpan.FromMilliseconds(10)).Chunks.Count == 0, "Retransmission cannot duplicate rewards");
reassembly.Feed(110, new byte[] { 7 }, TimeSpan.FromMilliseconds(20));
Check(reassembly.Poll(TimeSpan.FromSeconds(2)).LostBytes > 0, "Unrecoverable TCP loss is explicit");

var backlog = new TcpReassembler();
backlog.Reset(100);
backlog.Feed(103, new byte[] { 4, 5 }, TimeSpan.Zero);

Check(backlog.Poll(TimeSpan.FromSeconds(2), inputPending: true).LostBytes == 0, "A queued closing segment prevents a false reorder timeout");

var caughtUp = backlog.Feed(100, new byte[] { 1, 2, 3 }, TimeSpan.FromSeconds(2.1));
Check(caughtUp.LostBytes == 0 && caughtUp.Chunks.SelectMany(x => x).SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "Backlog catch-up delivers all bytes exactly once");

backlog.Feed(110, new byte[] { 6 }, TimeSpan.FromSeconds(3));
Check(backlog.Poll(TimeSpan.FromSeconds(5), inputPending: false).LostBytes == 5, "A genuine gap still expires after the delivery queue drains");

using (var healthCase = new Engine(Path.Combine(root, "health-classification")))
{
    healthCase.Feed("active", Identity("PrivateCharacter"));
    healthCase.Feed("active", Snapshot(new()
    {
        [1086] = 2
    }, new()
    {
        [1086] = 30
    }));
    healthCase.Feed("background", new byte[] { 0 });
    healthCase.RetireFlow("background");
    healthCase.RetireFlow("active");
    healthCase.Gap("other", reason: "Private network path");
    var hv = healthCase.View(true, 2, 123, 4, 1, true);
    Check(hv.Ready && hv.Health!.RetiredFlows == 1 && hv.Health.ActiveInterruptions == 0 && hv.Health.BackgroundInterruptions == 1 && hv.Status == "Live", "Idle cleanup and unrelated gaps preserve active collection reliability");
    healthCase.Gap("active", reason: "Private interruption");
    hv = healthCase.View(true, 2, 123, 4, 2, true);
    Check(hv.Health!.ActiveInterruptions == 1 && hv.Profile.Pets[1086].ProgressNeedsSync, "A real active stream gap still invalidates totals");

    var report = SupportReport.Create(hv with
    {
        Status = "PRIVATE_STATUS",
        Detail = "PRIVATE_PATH",
        Character = "PRIVATE_CHARACTER",
        DetectedCharacter = "PRIVATE_ALT",
        Error = "PRIVATE_ERROR"
    }, "test-version");
    Check(!new[] { "PrivateCharacter", "Private network path", "Private interruption", "PRIVATE_STATUS", "PRIVATE_PATH", "PRIVATE_CHARACTER", "PRIVATE_ALT", "PRIVATE_ERROR", healthCase.Book.Selected!.ProfileKey }.Any(report.Contains), "Shared reports exclude identity, free text, errors, and account keys");
    using var parsedReport = JsonDocument.Parse(report);
    Check(parsedReport.RootElement.GetProperty("Counters").GetProperty("ActiveInterruptions").GetInt64() == 1 && parsedReport.RootElement.GetProperty("AdapterPacketsDropped").GetInt64() == 4, "Shared reports retain useful typed counters");
}

byte[] raw = { 22, 0x33, 0x36, 0xd1, 5, 0, 0, 0, 0, 7, 4, (byte)'Z', (byte)'e', (byte)'r', (byte)'o', 0, 0, 0, 0 };
LocalCharacter? decoded = null;
var wire = new CollectionWire(Catalog.Entries.Select(p => p.Id));
wire.Frame += (op, f, b) => decoded = GameProtocol.Local(op, f, b);
wire.Feed(raw);
Check(decoded?.Name == "Zero" && decoded.Entity == 721, "Captured own-character record independently decodes");

byte[] removed = { 9, 0x42, 0x36, 43, 0, 3, 0, 0, 0 };
(int Entity, int Reason)? end = null;
wire.Frame += (op, f, b) => end = GameProtocol.Removal(op, f, b);
wire.Feed(removed);
Check(end is { Entity: 43, Reason: 3 }, "Captured removal framing decodes");

int calls = 0;
var atomic = new CollectionWire(Catalog.Entries.Select(p => p.Id));
atomic.Decoded += x => calls++;
atomic.Feed(Points(0xd, (1007, 1), (1060, 1)));
Check(calls == 2, "Ordered decoder emits every batch entry once");

var longFrames = Snapshot(Catalog.Entries.Take(20).ToDictionary(p => p.Id, p => 1), new());
var wide = new CollectionWire(Catalog.Entries.Select(p => p.Id));
Check(wide.Feed(longFrames).Single().Snapshot!.Ranks.Count == 20, "Multi-byte length header is supported");

var pool = new CaptureFlowPool<int, int>(2, x => TimeSpan.FromSeconds(x), (k, v) => k == 1);
pool.TryAdd(1, 0, out _);
pool.TryAdd(2, 1, out _);
pool.TryAdd(3, 2, out var evicted);
Check(evicted?.Key == 2 && pool.TryGetValue(1, out _), "Background pressure preserves protected game connection");

using (var k = new Engine(Path.Combine(root, "kills")))
{
    k.Execute(new("create", Name: "Zero", Realm: "Siel"));
    k.Feed("game", Identity("Zero"));
    k.Feed("game", Snapshot(new()
    {
        [1008] = 1
    }, new()
    {
        [1008] = 3
    }));
    using var s = new MemoryStream();
    using (var w = new BinaryWriter(s, Encoding.UTF8, true))
    {
        w.Write(V(43));
        w.Write((byte)0);
        w.Write((ushort)0);
        w.Write(2100034);
        w.Write((byte)0);
        w.Write((byte)2);
    }
    k.Feed("game", Frame(0x40, 0x36, s.ToArray()));

    using var damage = new MemoryStream();
    using (var w = new BinaryWriter(damage, Encoding.UTF8, true))
    {
        w.Write(V(43));
        w.Write(V(4));
        w.Write(V(0));
        w.Write(V(721));
        w.Write((uint)11010000);
        w.Write(V(0));
        w.Write(new byte[8]);
        w.Write(V(0));
        w.Write(V(100));
    }
    k.Feed("game", Frame(4, 0x38, damage.ToArray()));
    k.Feed("game", Frame(0x42, 0x36, new byte[] { 43, 0, 3 }));
    Check(k.Tracker.RecentActivities.Single().PetId == 1008, "Supported own kill refreshes the correctly linked pet");
    Check(k.Tracker.Profile.Pets[1008].CurrentSouls == 3, "Kills do not invent soul rewards");
    Check(k.Tracker.CountedKills == 1, "Removal counts the life once");
    k.Feed("game", Frame(0x42, 0x36, new byte[] { 43, 0, 3 }));
    Check(k.Tracker.CountedKills == 1, "Duplicate removal never counts twice");

    k.GameGeneration(1);
    k.GameGeneration(2);
    Check(!k.Tracker.Ready, "Game process restart invalidates character continuity");
    k.Feed("newgame", Identity("Zero"));
    k.Feed("newgame", Points(0xd, (1008, 1)));
    Check(k.Tracker.Profile.Pets[1008].CurrentSouls == 3 && k.Tracker.Profile.Pets[1008].ProgressNeedsSync, "New process cannot advance saved totals without a fresh snapshot");

    k.Execute(new("backup"));
    Check(Directory.GetDirectories(Path.Combine(root, "kills", "backups")).Length == 1, "Explicit backup preserves profiles outside install files");
}

var compressedFrames = Identity("Zero").Concat(initial).ToArray();
var literal = new List<byte> { 0xff, 0xff };
literal.AddRange(BitConverter.GetBytes(compressedFrames.Length));
literal.Add(0xf0);
int extra = compressedFrames.Length - 15;
while (extra >= 255)
{
    literal.Add(255);
    extra -= 255;
}
literal.Add((byte)extra);
literal.AddRange(compressedFrames);
int snapshots = 0, characters = 0;
var compressed = new CollectionWire(Catalog.Entries.Select(p => p.Id));
compressed.Decoded += x =>
{
    if (x.Snapshot != null)
        snapshots++;
};
compressed.Frame += (op, f, b) =>
{
    if (GameProtocol.Local(op, f, b) != null)
        characters++;
};
for (int i = 0; i < literal.Count; i++) compressed.Feed(new[] { literal[i] });
Check(snapshots == 1 && characters == 1, "Fragmented compressed frames preserve character and collection event ordering");

using (var order = new Engine(Path.Combine(root, "reverse")))
{
    order.Execute(new("create", Name: "Zero", Realm: "Siel"));
    order.Feed("game", Identity("Zero"));
    order.Feed("game", Snapshot(new()
    {
        [1007] = 1
    }, new()
    {
        [1007] = 24
    }));
    order.Feed("game", Points(0xd, (1007, 2)));
    order.Feed("game", Points(0xe, (1007, 25)));
    order.Feed("game", Rank(1007, 2));
    Check(order.Tracker.Profile.Pets[1007].CurrentSouls == 1 && !order.Tracker.Profile.Pets[1007].ProgressNeedsSync, "Spend before rank preserves known leftover in the new stage");
    order.Feed("game", Points(0xd, (1007, 2)));
    Check(order.Tracker.Profile.Pets[1007].CurrentSouls == 3, "Rewards keep advancing after spend-first rank transition");
}

// A deduction is not evidence of the resulting rank. Keep the old stage pending.
using (var pending = new Engine(Path.Combine(root, "pending-unlock-spend")))
{
    pending.Feed("game", Identity("PendingTester"));
    pending.Feed("game", Snapshot(new(), new()
    {
        [1007] = 4
    }));
    pending.Feed("game", Points(0xd, (1007, 1)));
    pending.Feed("game", Points(0xe, (1007, 5)));
    var pet = pending.Tracker.Profile.Pets[1007];
    Check(pet.Level == 0 && pet.CurrentSouls == 5 && pet.RankIncreasePending, "Unrecognized unlock stays pending instead of presenting a fresh 0/5 counter");
    pending.Feed("game", Points(0xd, (1007, 2)));
    Check(pet.Level == 0 && pet.CurrentSouls == 5 && pet.ProgressNeedsSync, "Rewards after an unconfirmed spend cannot revive the old stage");
    pending.Feed("game", Identity("PendingTester"));
    pet = pending.Tracker.Profile.Pets[1007];
    Check(pet.RankIncreasePending && pet.CurrentSouls == 5, "Rebinding the uninterrupted ledger preserves a pending rank transition");
    pending.Feed("game", Rank(1007, 1));
    pet = pending.Tracker.Profile.Pets[1007];
    Check(pet.Level == 1 && pet.CurrentSouls == 2 && pet.Goal == 25 && !pet.ProgressNeedsSync, "A recognized rank restores rewards earned while its transition was pending");
    pending.Feed("game", Rank(1007, 1));
    Check(pet.CurrentSouls == 2 && !pet.ProgressNeedsSync, "Duplicate rank confirmation does not reset the recovered balance");
}
using (var pending = new Engine(Path.Combine(root, "pending-rank-spend")))
{
    pending.Feed("game", Identity("PendingTester"));
    pending.Feed("game", Snapshot(new()
    {
        [1007] = 1
    }, new()
    {
        [1007] = 24
    }));
    pending.Feed("game", Points(0xd, (1007, 2)));
    pending.Feed("game", Points(0xe, (1007, 25)));
    Check(pending.Tracker.Profile.Pets[1007].RankIncreasePending, "Rank 1 deduction also waits for the next rank confirmation");
    pending.Gap("game");
    pending.Feed("game", Rank(1007, 2));
    Check(pending.Tracker.Profile.Pets[1007].ProgressNeedsSync, "A rank confirmation across a capture gap cannot restore a stale ledger");
    pending.Feed("game", Snapshot(new()
    {
        [1007] = 2
    }, new()
    {
        [1007] = 1
    }));
    Check(pending.Tracker.Profile.Pets[1007].CurrentSouls == 1 && !pending.Tracker.Profile.Pets[1007].ProgressNeedsSync, "A fresh collection resolves the pending transition after a gap");
}

using (var pending = new Engine(Path.Combine(root, "pending-unlock-alt")))
{
    pending.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
    pending.Feed("game", Identity("PendingTester"));
    pending.Feed("game", Snapshot(new(), new()
    {
        [1007] = 4
    }));
    pending.Feed("game", Points(0xd, (1007, 1)));
    pending.Feed("game", Points(0xe, (1007, 5)));
    pending.Feed("game", Identity("LinkedAlt", 5999));
    Check(pending.Tracker.Ready && pending.Tracker.Profile.Pets[1007].RankIncreasePending, "Linked-alt rebinding retains the shared account's unconfirmed transition");
    pending.Feed("game", Snapshot(new()
    {
        [1007] = 1
    }, new()
    {
        [1007] = 0
    }));
    var pet = pending.Tracker.Profile.Pets[1007];
    Check(pet.Level == 1 && pet.Goal == 25 && pet.CurrentSouls == 0 && !pet.ProgressNeedsSync, "A fresh login snapshot authoritatively resolves an unsupported first unlock");
    pending.Feed("game", Points(0xd, (1007, 3)));
    Check(pet.CurrentSouls == 3 && !pet.ProgressNeedsSync, "Normal reward tracking resumes from the corrected snapshot");
}

// Supported rank-message path only: live unlock opcode remains to be confirmed.
using (var unlock = new Engine(Path.Combine(root, "unlock-stage")))
{
    unlock.Feed("game", Identity("UnlockTester"));
    unlock.Feed("game", Snapshot(new(), new()
    {
        [1007] = 4
    }));
    unlock.Feed("game", Points(0xd, (1007, 1)));
    unlock.Feed("game", Points(0xe, (1007, 5)));
    unlock.Feed("game", Rank(1007, 1));
    var pet = unlock.Tracker.Profile.Pets[1007];
    Check(pet.Level == 1 && pet.Goal == 25 && pet.CurrentSouls == 0 && !pet.ProgressNeedsSync, "Recognized unlock transition enters Rank 1 with a fresh 0/25 counter");
    Check(unlock.Tracker.LastRankIncrease is { PetId: 1007, Rank: 1 }, "Recognized 0-to-1 transition records a rank increase after a soul reward");
    unlock.Feed("game", Points(0xd, (1007, 2)));
    Check(pet.CurrentSouls == 2, "Rank 1 continues counting earned soul rewards");
}

using (var scope = new Engine(Path.Combine(root, "reused")))
{
    scope.Execute(new("create", Name: "Zero", Realm: "Siel"));
    scope.Feed("same-endpoints", Identity("Zero"));
    scope.Feed("same-endpoints", Snapshot(new()
    {
        [1086] = 2
    }, new()
    {
        [1086] = 45
    }));
    scope.Gap("same-endpoints", true);
    scope.Feed("same-endpoints", Snapshot(new()
    {
        [1086] = 2
    }, new()
    {
        [1086] = 70
    }));
    Check(!scope.Tracker.Ready && scope.View(true, 1, 0, 0, 0, true).Profile.Pets[1086].CurrentSouls == 45, "Reused TCP endpoints cannot borrow the previous character identity");
    scope.Feed("same-endpoints", Identity("Other"));
    Check(!scope.Tracker.Ready, "Another character on reused endpoints cannot bind the old profile");
}

byte[] Zip(bool escape = false, bool corrupt = false)
{
    using var stream = new MemoryStream();
    using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
    {
        var hashes = new Dictionary<string, string>();
        foreach (string name in new[] { "PetTracker.exe", "PetTracker.Capture.exe", "PetTracker.Core.dll", "library.dll", escape ? "../escape.dll" : "Player Guide.txt" })
        {
            using (var item = zip.CreateEntry(name).Open())
                item.WriteByte(corrupt && name == "PetTracker.exe" ? (byte)43 : (byte)42);
            hashes[name] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 42 }));
        }
        using var manifest = new StreamWriter(zip.CreateEntry("package.json").Open());
        manifest.Write(JsonSerializer.Serialize(hashes));
    }
    return stream.ToArray();
}

using (var zip = new MemoryStream(Zip())) PetTracker.Setup.Installer.Extract(zip, Path.Combine(root, "valid-package"));
Check(File.Exists(Path.Combine(root, "valid-package", "PetTracker.exe")), "Installer verifies and extracts a valid package");

bool denied = false;
try
{
    using var zip = new MemoryStream(Zip(true));
    PetTracker.Setup.Installer.Extract(zip, Path.Combine(root, "escape-package"));
}
catch (InvalidDataException ex)
{
    denied = ex.Message.Contains("invalid path");
}
Check(denied && !File.Exists(Path.Combine(root, "escape.dll")), "Installer rejects traversal paths before writing outside its stage");

bool rejected = false;
try
{
    using var zip = new MemoryStream(Zip(false, true));
    PetTracker.Setup.Installer.Extract(zip, Path.Combine(root, "corrupt-package"));
}
catch (InvalidDataException ex)
{
    rejected = ex.Message.Contains("verification");
}
Check(rejected, "Installer rejects files that fail manifest hashes");

using (var backups = new Engine(Path.Combine(root, "backups-case")))
{
    backups.Execute(new("create", Name: "Zero", Realm: "Siel"));
    for (int i = 0; i < 9; i++)
        backups.Backup();
    Check(Directory.GetDirectories(Path.Combine(root, "backups-case", "backups")).Length == 7, "Automatic backup retention stays bounded");
}

using (var mismatch = new Engine(Path.Combine(root, "character-mismatch")))
{
    mismatch.Execute(new("create", Name: "Zero", Realm: "Siel"));
    Check(mismatch.View(true, 1, 0, 0, 0, true).Status == "Waiting for your character…", "Waiting message does not name a saved character");
    mismatch.Feed("encore-flow", Identity("Encore", 5001));
    mismatch.Feed("encore-flow", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 4
    }));
    var waiting = mismatch.View(true, 1, 0, 0, 0, true);
    Check(!waiting.Ready && waiting.Status == "Encore detected — choose profile" && waiting.Detail.Contains("Zero"), "Wrong-character capture explains the profile mismatch");
    Check(waiting.Profile.Pets[1066].CurrentSouls != 4, "Encore's collection cannot overwrite Zero");
    mismatch.Execute(new("create", Name: "Encore", Realm: "Siel"));
    Check(mismatch.Tracker.Ready && mismatch.Tracker.Profile.Pets[1066].CurrentSouls == 4 && mismatch.View(true, 1, 0, 0, 0, true).Status == "Live", "Choosing the detected character imports its staged snapshot without another relog");
}

string automaticRoot = Path.Combine(root, "automatic-characters");
string automaticKey;
using (var damagedBlock = new Engine(Path.Combine(root, "compressed-frame-loss")))
{
    damagedBlock.Feed("game", Identity("Encore"));
    damagedBlock.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    damagedBlock.Feed("game", FramingChecks.LiteralBlock(new byte[] { 0x80 }).Concat(Points(0xd, (1066, 1))).ToArray());
    Check(damagedBlock.Tracker.Profile.Pets[1066].CurrentSouls == 7 && damagedBlock.Tracker.Profile.Pets[1066].ProgressNeedsSync && damagedBlock.View(true, 1, 0, 0, 0, true).Health!.DecoderFaults == 1, "Malformed compressed contents invalidate totals before following rewards");
    damagedBlock.Feed("game", FramingChecks.LiteralBlock(Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 9
    })).Concat(Points(0xd, (1066, 1))).ToArray());
    Check(damagedBlock.Tracker.Profile.Pets[1066].CurrentSouls == 10 && !damagedBlock.Tracker.Profile.Pets[1066].ProgressNeedsSync, "A fresh valid compressed snapshot recovers totals and subsequent rewards");
}
foreach (bool spendFirst in new[] { false, true })
    using (var transition = new Engine(Path.Combine(root, "alt-promotion-" + spendFirst)))
    {
        transition.Execute(new("create", Name: "Zero", AccountName: "My account"));
        transition.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
        transition.Feed("game", Identity("Zero"));
        transition.Feed("game", Snapshot(new(), new()
        {
            [1066] = 4
        }));
        transition.Feed("game", Points(0xd, (1066, 1)));
        if (spendFirst)
        {
            transition.Feed("game", Points(0xe, (1066, 5)));
            transition.Feed("game", Rank(1066, 1));
        }
        else
        {
            transition.Feed("game", Rank(1066, 1));
            transition.Feed("game", Points(0xe, (1066, 5)));
        }
        transition.Feed("game", Identity("Rave"));
        transition.Feed("game", Points(0xd, (1066, 1)));
        Check(transition.Tracker.Profile.Pets[1066].CurrentSouls == 1 && !transition.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Completed promotion survives alt switch with spend-first=" + spendFirst);
        transition.Feed("game", Points(0xd, (1066, 24)));
        transition.Feed("game", Rank(1066, 2));
        transition.Feed("game", Identity("Encore"));
        transition.Feed("game", Points(0xe, (1066, 25)));
        transition.Feed("game", Points(0xd, (1066, 1)));
        Check(transition.Tracker.Profile.Pets[1066].Level == 2 && transition.Tracker.Profile.Pets[1066].CurrentSouls == 1 && !transition.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Rank/spend split across alt identity recovers from continuous account balance");
        transition.Feed("game", Rank(1066, 2));
        transition.Feed("game", Identity("Lux"));
        transition.Feed("game", Points(0xd, (1066, 1)));
        Check(transition.Tracker.Profile.Pets[1066].CurrentSouls == 2 && !transition.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Repeated confirmed rank cannot freeze a known balance on the next alt");
    }
using (var repeatedPriorRank = new Engine(Path.Combine(root, "repeated-rank-during-promotion")))
{
    repeatedPriorRank.Execute(new("create", Name: "Zero", AccountName: "My account"));
    repeatedPriorRank.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
    repeatedPriorRank.Feed("game", Identity("Zero"));
    repeatedPriorRank.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 24
    }));
    repeatedPriorRank.Feed("game", Points(0xd, (1066, 2)));
    repeatedPriorRank.Feed("game", Points(0xe, (1066, 25)));
    // A repeated current-rank observation is not the pending promotion's result.
    repeatedPriorRank.Feed("game", Rank(1066, 1));
    repeatedPriorRank.Feed("game", Identity("Rave"));
    repeatedPriorRank.Feed("game", Rank(1066, 2));
    repeatedPriorRank.Feed("game", Points(0xd, (1066, 1)));
    Check(repeatedPriorRank.Tracker.Profile.Pets[1066].Level == 2 && repeatedPriorRank.Tracker.Profile.Pets[1066].CurrentSouls == 2 && !repeatedPriorRank.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Repeated prior rank cannot consume pending spend evidence across an alt promotion");
    repeatedPriorRank.Feed("game", Identity("Encore"));
    repeatedPriorRank.Feed("game", Points(0xd, (1066, 1)));
    Check(repeatedPriorRank.Tracker.Profile.Pets[1066].CurrentSouls == 3 && !repeatedPriorRank.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Reconciled promotion remains exact through the next shared character");
}
using (var interruptedPromotion = new Engine(Path.Combine(root, "interrupted-alt-promotion")))
{
    interruptedPromotion.Execute(new("create", Name: "Zero", AccountName: "My account"));
    interruptedPromotion.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
    interruptedPromotion.Feed("game", Identity("Zero"));
    interruptedPromotion.Feed("game", Snapshot(new(), new()
    {
        [1066] = 5
    }));
    interruptedPromotion.Feed("game", Points(0xe, (1066, 5)));
    interruptedPromotion.Gap("game");
    interruptedPromotion.Feed("game", Rank(1066, 1));
    interruptedPromotion.Feed("game", Identity("Rave"));
    interruptedPromotion.Feed("game", Points(0xd, (1066, 1)));
    Check(interruptedPromotion.Tracker.Profile.Pets[1066].ProgressNeedsSync && interruptedPromotion.View(true, 1, 0, 0, 0, true).Status != "Live", "A gap between deduction and rank invalidates promotion balance evidence");
}
using (var sameConnection = new Engine(Path.Combine(root, "same-connection-alt")))
{
    sameConnection.Execute(new("create", Name: "Zero", AccountName: "My account"));
    sameConnection.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
    sameConnection.Feed("game", Identity("Zero", 5001));
    sameConnection.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 19
    }));
    // Live ordering: the complete account collection precedes the new alt identity.
    sameConnection.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 21
    }).Concat(Identity("Rave", 5002)).ToArray());
    Check(sameConnection.Tracker.CharacterName == "Rave" && sameConnection.View(true, 1, 0, 0, 0, true).Status == "Live", "Same-connection alt login retains its preceding account snapshot");
    sameConnection.Feed("game", Points(0xd, (1066, 1)));
    Check(sameConnection.Tracker.Profile.Pets[1066].CurrentSouls == 22 && !sameConnection.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Soul rewards keep advancing after snapshot-before-alt-identity login");
    sameConnection.Feed("game", Identity("Encore", 5003));
    sameConnection.Feed("game", Points(0xd, (1066, 1)));
    Check(sameConnection.Tracker.Profile.Pets[1066].CurrentSouls == 23, "Uninterrupted account balance survives identity-before-snapshot alt change");
    sameConnection.Gap("game");
    sameConnection.Feed("game", Identity("Lux", 5004));
    sameConnection.Feed("game", Points(0xd, (1066, 1)));
    Check(sameConnection.Tracker.Profile.Pets[1066].CurrentSouls == 23 && sameConnection.View(true, 1, 0, 0, 0, true).Status != "Live", "Alt changes cannot make a genuinely interrupted balance reliable");
    sameConnection.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 24
    }));
    sameConnection.Feed("game", Points(0xd, (1066, 1)));
    sameConnection.Feed("game", Rank(1066, 2));
    sameConnection.Feed("game", Points(0xe, (1066, 25)));
    Check(sameConnection.Tracker.Profile.Pets[1066].Level == 2 && sameConnection.Tracker.Profile.Pets[1066].CurrentSouls == 0, "Fresh recovery snapshot resumes rewards and automatic promotion");
}
using (var automatic = new Engine(automaticRoot))
{
    automatic.Execute(new("create", Name: "Zero", AccountName: "My account"));
    automaticKey = automatic.Book.SelectedProfileKey!;
    automatic.Feed("zero", Identity("Zero"));
    automatic.Feed("zero", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 4
    }));
    automatic.Feed("rave", Identity("Rave"));
    automatic.Feed("rave", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    automatic.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
    Check(automatic.Tracker.Ready && automatic.Tracker.CharacterName == "Rave" && automatic.Tracker.Profile.Pets[1066].CurrentSouls == 7, "Enabling single-account sharing binds the already detected alt and its own snapshot");
    Check(automatic.Book.Profiles.Count == 1 && automatic.Book.SelectedProfileKey == automaticKey && automatic.Book.Selected!.Matches("Rave"), "Automatic alt shares one persistent collection without creating another profile");
    Check(Directory.GetDirectories(Path.Combine(automaticRoot, "backups")).Length == 1, "Enabling shared-character mode backs up previous account state");
    automatic.Feed("zero", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 1
    }));
    Check(automatic.Tracker.CharacterName == "Rave" && automatic.Tracker.Profile.Pets[1066].CurrentSouls == 7, "Late old-character snapshot cannot rebind or overwrite the new character");
    automatic.Feed("encore", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 9
    }));
    Check(automatic.Tracker.CharacterName == "Rave", "Sharing never infers character identity from a collection alone");
    automatic.Feed("encore", Identity("Encore", 5999));
    Check(automatic.Tracker.Ready && automatic.Tracker.CharacterName == "Encore" && automatic.Tracker.Profile.Pets[1066].CurrentSouls == 9, "Previously unknown alt automatically uses fresh server collection across server tokens");
    automatic.Feed("encore", Points(0xd, (1066, 1)));
    Check(automatic.Tracker.Profile.Pets[1066].CurrentSouls == 10, "New alt soul reward updates the shared collection");
    automatic.Gap("encore", true);
    automatic.Feed("encore", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 11
    }));
    Check(!automatic.Tracker.Ready, "Shared mode cannot reuse identity across a restarted connection");
    automatic.Feed("encore", Identity("Encore", 5999));
    automatic.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), false))));
    automatic.Feed("encore", Identity("Stranger"));
    automatic.Feed("encore", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 20
    }));
    Check(!automatic.Tracker.Ready && !automatic.Book.Selected!.Matches("Stranger") && automatic.View(true, 1, 0, 0, 0, true).Profile.Pets[1066].CurrentSouls == 11, "Disabling sharing restores separation for unknown characters");
    automatic.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), true))));
}
using (var automatic = new Engine(automaticRoot))
{
    Check(automatic.Settings.ShareAllCharacters && automatic.Book.SelectedProfileKey == automaticKey && !automatic.Tracker.Ready, "Sharing preference persists but saved data never invents a live identity");
    automatic.Feed("lux", Identity("Lux"));
    automatic.Feed("lux", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 21
    }));
    Check(automatic.Tracker.Ready && automatic.Tracker.CharacterName == "Lux" && automatic.Book.Profiles.Count == 1, "A new character connects automatically after tracker restart");
}
string accountRoot = Path.Combine(root, "shared-account");
string sharedKey;
foreach (bool shareAll in new[] { false, true })
    using (var lateAccount = new Engine(Path.Combine(root, "late-account-snapshot-" + shareAll)))
    {
        lateAccount.Execute(new("create", Name: "Zero", AccountName: "My account"));
        lateAccount.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ShareAllCharacters), shareAll))));
        lateAccount.Feed("old", Identity("Zero"));
        lateAccount.Feed("old", Snapshot(new()
        {
            [1066] = 1
        }, new()
        {
            [1066] = 4
        }));
        lateAccount.Feed("current", Identity("Encore"));
        lateAccount.Feed("current", Snapshot(new()
        {
            [1066] = 1
        }, new()
        {
            [1066] = 7
        }));
        if (!shareAll)
            lateAccount.Execute(new("link", Name: "Encore", AccountName: "My account"));
        lateAccount.Feed("old", Snapshot(new()
        {
            [1066] = 1
        }, new()
        {
            [1066] = 3
        }));
        lateAccount.Feed("old", Points(0xd, (1066, 1)));
        Check(lateAccount.Active == "current" && lateAccount.Tracker.CharacterName == "Encore" && lateAccount.Tracker.Profile.Pets[1066].CurrentSouls == 7, "Delayed old-account snapshot cannot rebind over current alt; automatic sharing=" + shareAll);
        lateAccount.Feed("current", Points(0xd, (1066, 1)));
        Check(lateAccount.Tracker.Profile.Pets[1066].CurrentSouls == 8 && !lateAccount.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Current alt rewards continue after delayed old-account traffic; automatic sharing=" + shareAll);
        lateAccount.Gap("current", true);
        lateAccount.RetireFlow("current");
        lateAccount.Feed("old", Snapshot(new()
        {
            [1066] = 1
        }, new()
        {
            [1066] = 2
        }));
        Check(!lateAccount.Tracker.Ready && lateAccount.View(true, 1, 0, 0, 0, true).Profile.Pets[1066].CurrentSouls == 8, "Retiring the latest connection cannot revive the previous character's baseline; automatic sharing=" + shareAll);
        lateAccount.Feed("reconnected", Identity("Encore"));
        lateAccount.Feed("reconnected", Snapshot(new()
        {
            [1066] = 1
        }, new()
        {
            [1066] = 9
        }));
        lateAccount.Feed("reconnected", Points(0xd, (1066, 1)));
        Check(lateAccount.Tracker.CharacterName == "Encore" && lateAccount.Tracker.Profile.Pets[1066].CurrentSouls == 10 && !lateAccount.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Fresh identity and collection resume after latest connection retirement; automatic sharing=" + shareAll);
    }

using (var account = new Engine(accountRoot))
{
    account.Execute(new("create", Name: "Zero", Realm: "Siel"));
    account.Feed("zero", Identity("Zero", 5001));
    account.Feed("zero", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 4
    }));
    sharedKey = account.Book.SelectedProfileKey!;

    account.Feed("encore", Identity("Encore", 5010));
    account.Feed("encore", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    account.Execute(new("link", Name: "Encore", AccountName: "My account"));
    Check(account.Book.Profiles.Count == 1 && account.Book.SelectedProfileKey == sharedKey, "Linking an alt preserves one collection and its original storage key");
    Check(account.Tracker.Ready && account.Tracker.CharacterName == "Encore" && account.Tracker.Profile.Pets[1066].CurrentSouls == 7 && account.Tracker.Profile.AccountKey == sharedKey, "Account collection imports the linked alt's authoritative snapshot");
    Check(account.Book.Selected!.ServerToken == null && account.Book.Selected.IsAccount, "Legacy server token is not mistaken for an account identifier");

    account.Feed("encore", Points(0xd, (1066, 1)));
    account.Feed("zero-new-server", Identity("Zero", 5999));
    Check(account.Tracker.Ready && account.Tracker.Profile.Pets[1066].CurrentSouls == 8 && account.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Linked character switches retain account progress but need that connection's baseline");
    account.Feed("zero-new-server", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 8
    }));
    Check(account.View(true, 1, 0, 0, 0, true).Status == "Live" && account.Tracker.Profile.AccountKey == sharedKey, "Same account works across linked characters without separate server collections");

    account.Gap("zero-new-server", true);
    account.Feed("unknown-account", Identity("Stranger", 5999));
    account.Feed("unknown-account", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 20
    }));
    Check(!account.Tracker.Ready && account.View(true, 1, 0, 0, 0, true).Profile.Pets[1066].CurrentSouls == 8, "An unlinked character cannot overwrite the selected account");
    bool staleLink = false;
    try
    {
        account.Execute(new("link", Name: "Encore"));
    }
    catch (InvalidOperationException)
    {
        staleLink = true;
    }
    Check(staleLink, "A stale character-link request cannot link a different detected character");
}

using (var resumed = new Engine(accountRoot))
{
    Check(resumed.Book.Selected!.IsAccount && resumed.Book.Selected.LinkedCharacters.Contains("Encore") && resumed.View(false, 0, 0, 0, 0, false).Profile.AccountKey == sharedKey, "Account membership and collection owner survive restart");
    resumed.Feed("encore", Identity("Encore", 5010));
    resumed.Feed("encore", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 9
    }));
    Check(resumed.Tracker.Ready && resumed.Tracker.Profile.Pets[1066].CurrentSouls == 9, "Restart loads the shared account even when its last character differs from the legacy owner");

    var exported = resumed.View(true, 1, 0, 0, 0, true);
    using var imported = new Engine(Path.Combine(root, "account-import"));
    imported.Execute(new("import", Bindings: exported.Profiles, Imports: new()
    {
        [sharedKey] = exported.Profile
    }));
    Check(imported.Book.Selected!.IsAccount && imported.Book.Selected.ProfileKey != sharedKey && imported.View(false, 0, 0, 0, 0, false).Profile.AccountKey == imported.Book.Selected.ProfileKey, "Import remaps account ownership to a new local storage key");
}

using (var recovered = new Engine(Path.Combine(root, "ordered-recovery")))
{
    recovered.Execute(new("create", Name: "Encore", AccountName: "My account"));
    recovered.Feed("game", Identity("Encore"));
    recovered.Feed("game", Snapshot(new()
    {
        [1086] = 2
    }, new()
    {
        [1086] = 45
    }));
    recovered.Feed("game", Points(0xd, (999999, 1)).Concat(Snapshot(new()
    {
        [1086] = 2
    }, new()
    {
        [1086] = 50
    })).ToArray());
    Check(recovered.View(true, 1, 0, 0, 0, true).Status == "Live" && !recovered.Tracker.Profile.Pets[1086].ProgressNeedsSync, "A snapshot after a fault in the same TCP chunk recovers in packet order");
    recovered.Feed("game", Points(0xd, (1086, 1)));
    Check(recovered.Tracker.Profile.Pets[1086].CurrentSouls == 51, "Rewards advance from the recovered baseline");

    recovered.Feed("game", Snapshot(new()
    {
        [1086] = 2
    }, new()
    {
        [1086] = 55
    }).Concat(Points(0xd, (999999, 1))).ToArray());
    Check(recovered.Tracker.Profile.Pets[1086].ProgressNeedsSync, "A fault after a snapshot still invalidates its balance");
    recovered.Feed("game", Points(0xd, (1086, 2)));
    Check(recovered.Tracker.Profile.Pets[1086].CurrentSouls == 55 && recovered.Tracker.LastPointObservation?.Value == 2, "Unreliable rewards remain observable without changing frozen balances");
    Check(recovered.Tracker.RecentActivities.Any(p => p.PetId == 1086), "Recent pet rewards remain visible during collection sync recovery");

    recovered.WriteHealth(true, 1, 100, 0, 1, true);
    using var healthDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "ordered-recovery", "connection-status.json")));
    Check(healthDoc.RootElement.GetProperty("Health").GetProperty("DecoderFaults").GetInt64() == 2 && healthDoc.RootElement.GetProperty("Health").GetProperty("LastFault").GetProperty("Reason").GetString() == "Unsupported soul batch entry", "Persistent diagnostics identify the actual decoder failure");
    Check(healthDoc.RootElement.GetProperty("Character").GetString() == "Encore" && healthDoc.RootElement.GetProperty("RecentPets").GetInt32() > 0, "Diagnostics distinguish character binding from absent activity");
}

string recoveryRoot = Path.Combine(root, "automatic-recovery");
string recoveryKey;
using (var prepare = new Engine(recoveryRoot))
{
    prepare.Execute(new("create", Name: "Encore", AccountName: "My account"));
    prepare.Feed("game", Identity("Encore"));
    prepare.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    recoveryKey = prepare.Book.SelectedProfileKey!;
    prepare.Tracker.Profile.Pets[1066].Notes = "Favorite farming spot";
    prepare.Execute(new("settings", Settings: new()
    {
        Accent = "Teal",
        InactiveSeconds = 60
    }));
    prepare.Backup();
}

string damaged = Path.Combine(recoveryRoot, "profiles", recoveryKey + ".json");
File.WriteAllText(damaged, "{broken");
var foreign = new Profile { AccountKey = new string('a', 24), CharacterName = "Encore", Pets = Catalog.Create() };
Storage.Save(damaged + ".bak", foreign);

using (var restored = new Engine(recoveryRoot))
{
    var old = restored.View(false, 0, 0, 0, 0, false);
    Check(old.Profile.Pets[1066].CurrentSouls == 7 && old.Profile.Pets[1066].Notes == "Favorite farming spot", "Corrupted account recovers its matching managed backup and local notes automatically");
    Check(old.Profile.Pets[1066].ProgressNeedsSync && !restored.Tracker.Ready, "Recovered counts cannot masquerade as live game totals");
    Check(Directory.GetFiles(Path.Combine(recoveryRoot, "recovery")).Any(), "Failed data is retained for diagnosis rather than discarded");
    restored.Feed("game", Identity("Encore"));
    restored.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 9
    }));
    Check(restored.Tracker.Profile.Pets[1066].CurrentSouls == 9 && !restored.Tracker.Profile.Pets[1066].ProgressNeedsSync && restored.Tracker.Profile.Pets[1066].Notes == "Favorite farming spot", "Fresh game totals replace recovery data without removing customization");
    restored.Backup();
}

File.Delete(damaged);
using (var missing = new Engine(recoveryRoot))
{
    missing.Feed("game", Identity("Encore"));
    Check(missing.Tracker.Profile.Pets[1066].CurrentSouls == 9, "A missing collection recovers before character binding loads it");
}

File.WriteAllText(Path.Combine(recoveryRoot, "settings.json"), "not json");
File.WriteAllText(Path.Combine(recoveryRoot, "profiles.json"), "not json");
using (var recoveredBook = new Engine(recoveryRoot))
{
    Check(recoveredBook.Book.Profiles.Count == 1 && recoveredBook.Book.SelectedProfileKey == recoveryKey, "Account-book recovery preserves the same account without duplicate profiles");
    Check(recoveredBook.Settings.Accent == "Teal" && recoveredBook.Settings.InactiveSeconds == 60, "Settings recover automatically alongside the collection");
}

string noBackupRoot = Path.Combine(root, "no-valid-backup");
using (var clean = new Engine(noBackupRoot))
{
    clean.Execute(new("create", Name: "Encore", AccountName: "My account"));
    Storage.Save(Path.Combine(noBackupRoot, "profiles", clean.Book.SelectedProfileKey! + ".json"), foreign);
    Check(clean.View(false, 0, 0, 0, 0, false).Profile.Pets[1066].CurrentSouls == null, "Foreign data with the same character name cannot populate another account");
}

using (var defaults = new Engine(Path.Combine(root, "automatic-account")))
{
    defaults.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 3
    }));
    Check(!defaults.Tracker.Ready && defaults.Book.Profiles.Count == 0, "A snapshot does not create an account without character identification");
    defaults.Feed("game", Identity("Encore"));
    Check(defaults.Book.Selected!.Label == "My account" && defaults.Tracker.Ready && defaults.Tracker.Profile.Pets[1066].CurrentSouls == 3, "First identified character sets up its account and imports collection without typing a profile");
    defaults.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Left), 400d), (nameof(AppSettings.Top), 200d))));
    defaults.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Accent), "Teal"))));
    Check(defaults.Settings.Left == 400 && defaults.Settings.Top == 200 && defaults.Settings.Accent == "Teal", "Appearance changes cannot overwrite newer tracker geometry");
    bool patchRejected = false;
    try
    {
        defaults.Execute(new("patch", Patch: SettingsPatch.Fields(("UnsupportedSetting", 1))));
    }
    catch (InvalidDataException)
    {
        patchRejected = true;
    }
    Check(patchRejected && defaults.Settings.Left == 400, "Invalid setting patches leave the existing configuration intact");
    defaults.Execute(new("rename", Key: defaults.Book.SelectedProfileKey, AccountName: "Main account"));
    Check(defaults.Book.Selected!.Label == "Main account", "Account labels have a real rename action");
}

using (var offline = new Engine(accountRoot))
{
    var pet = offline.View(false, 0, 0, 0, 0, false).Profile.Pets[1066];
    int? points = pet.CurrentSouls;
    pet.Notes = "Offline notes";
    pet.Favorite = true;
    offline.Execute(new("details", Profile: new()
    {
        Pets = new()
        {
            [1066] = pet
        }
    }));
    var savedOffline = offline.View(false, 0, 0, 0, 0, false).Profile.Pets[1066];
    Check(savedOffline.Notes == "Offline notes" && savedOffline.Favorite && savedOffline.CurrentSouls == points, "Offline customization saves without requiring capture or modifying game progress");
}

var protocol = new ProtocolActivity();
var checkTime = DateTimeOffset.UtcNow;
protocol.Observe(4, 0x38, new byte[] { 1, 2 }, "Unsupported hit header", checkTime);
Check(protocol.View().Samples.Count == 0 && protocol.View().Outcomes["Unsupported hit header"] == 1, "Diagnostic counters do not silently enable byte recording");
protocol.Start(checkTime);
protocol.Observe(0x33, 0x36, Encoding.UTF8.GetBytes("Private character record"), "Identity", checkTime);
protocol.Observe(1, 0x99, Encoding.UTF8.GetBytes("Private chat"), "Chat", checkTime);
Check(protocol.View().Samples.Count == 0, "Activity check excludes character records and unrelated chat lanes");
protocol.Observe(4, 0x38, new byte[400], "Unsupported hit header", checkTime.AddSeconds(1));
Check(protocol.View().Samples.Single().Prefix.Length == 384, "Numeric frame samples have a fixed byte bound");
protocol.Observe(4, 0x38, new byte[] { 3 }, "Late hit", checkTime.AddSeconds(90));
Check(protocol.View().Samples.Count == 1, "Activity sampling stops after 90 seconds");
for (int i = 0; i < 200; i++) protocol.Observe(4, 0x38, new byte[] { 4 }, "Hit", checkTime.AddSeconds(2));
Check(protocol.View().Samples.Count == 16, "Activity sampling bounds each message type independently");
protocol.Observe(0x41, 0x36, new byte[] { 9 }, "Spawn", checkTime.AddSeconds(3));
protocol.Observe(0x0d, 0x90, new byte[] { 8 }, "Reward", checkTime.AddSeconds(3));
for (int i = 0; i < 500; i++) protocol.Observe(4, 0x38, new byte[] { 4 }, "Hit", checkTime.AddSeconds(4));
Check(protocol.View().Samples.Any(s => s.Message == "36:41") && protocol.View().Samples.Any(s => s.Message == "90:0D") && protocol.View().Samples.Count == 18, "Combat floods cannot evict rare appearance or soul evidence");
protocol.Frame(0xd, 0x90, false);
protocol.Frame(4, 0x38, true);
Check(protocol.View().PetMessages["0D"] == 1 && !protocol.View().ActiveFrames.ContainsKey("90:0D") && protocol.View().ActiveFrames["38:04"] == 1, "Diagnostics distinguish background pet messages from active game traffic");

using (var diagnosis = new Engine(Path.Combine(root, "activity-diagnosis")))
{
    diagnosis.Feed("game", Identity("Encore"));
    diagnosis.Feed("game", Snapshot(new()
    {
        [1066] = 3
    }, new()));
    Check(diagnosis.View(true, 1, 0, 0, 0, true).Detail.Contains("Waiting for pet activity"), "A synchronized collection does not claim that rewards are arriving");
    diagnosis.Execute(new("activity-check"));
    diagnosis.Feed("game", Frame(4, 0x38, new byte[] { 1 }));
    var health = diagnosis.View(true, 1, 0, 0, 0, true).Health!;
    Check(health.Activity!.Outcomes["Unsupported hit header"] == 1 && health.Activity.Samples.Single().Result == "Unsupported hit header", "Live engine surfaces rejected hit layouts without inventing activity");
    Check(diagnosis.Tracker.RecentActivities.Count == 0, "Diagnostic recording does not manufacture pet progress or rows");
}

string pipeRoot = Path.Combine(root, "Path-Spelling");
Check(Storage.Pipe(pipeRoot) == Storage.Pipe(pipeRoot.ToUpperInvariant()) && Storage.Pipe(pipeRoot) == Storage.Pipe(pipeRoot + Path.DirectorySeparatorChar), "Equivalent Windows data paths share one capture endpoint and worker mutex");
Check(Storage.Pipe(pipeRoot) == Storage.Pipe(Path.Combine(pipeRoot, ".")) && Storage.Pipe(pipeRoot) != Storage.Pipe(pipeRoot + "-other"), "Normalized capture endpoints preserve separation between distinct data folders");
string gateUser = "test-" + Guid.NewGuid().ToString("N");
bool secondPrimary = true;
using (var primaryGate = new AppInstanceGate(gateUser))
{
    var thread = new Thread(() =>
    {
        using var duplicateGate = new AppInstanceGate(gateUser);
        secondPrimary = duplicateGate.IsPrimary;
    });
    thread.Start();
    thread.Join();
    Check(primaryGate.IsPrimary && !secondPrimary, "One Windows user cannot open parallel HUDs from different data folders");
}
using (var replacementGate = new AppInstanceGate(gateUser)) Check(replacementGate.IsPrimary, "Exiting the first tracker permits a clean subsequent launch");

string retryRoot = Path.Combine(root, "save-retry");

using (var saving = new Engine(retryRoot))
{

    saving.Feed("game", Identity("Encore"));
    saving.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 3
    }));

    saving.Feed("game", Points(0xd, (1066, 1)));

    string savePath = Path.Combine(retryRoot, "profiles", saving.Book.SelectedProfileKey + ".json");

    using var held = File.Open(savePath, FileMode.Open, FileAccess.Read, FileShare.Read);

    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));

    int failures = 0, recoveries = 0;
    string failureStatus = "";
    bool unchanged = false;

    await MaintenanceLoop.Run(saving.Flush, error =>
    {

        saving.MaintenanceResult(error);

        if (error != null)
        {
            failures++;
            failureStatus = saving.View(true, 1, 0, 0, 0, true).Status;
            unchanged = JsonSerializer.Deserialize<Profile>(File.ReadAllText(savePath))!.Pets[1066].CurrentSouls == 3;
            held.Dispose();
        }

        else
        {
            recoveries++;
            stop.Cancel();
        }

    }, stop.Token, TimeSpan.FromMilliseconds(10));

    Check(failures == 1 && recoveries == 1, "A locked save file does not permanently stop periodic maintenance");

    Check(unchanged && JsonSerializer.Deserialize<Profile>(File.ReadAllText(savePath))!.Pets[1066].CurrentSouls == 4, "A failed atomic save preserves the old file and retries the pending soul total");

    Check(failureStatus == "Background retry needed" && saving.View(true, 1, 0, 0, 0, true).Status == "Live", "Save failure is visible and clears after recovery without relogging");

    Check(JsonSerializer.Deserialize<Profile>(File.ReadAllText(savePath + ".bak"))!.Pets[1066].CurrentSouls == 3, "Successful retry retains the previous collection as its backup");

    int cancelledTicks = 0;
    await MaintenanceLoop.Run(() => cancelledTicks++, _ => { }, stop.Token, TimeSpan.FromMilliseconds(1));

    Check(cancelledTicks == 0, "Worker shutdown does not schedule another maintenance tick");

}

Console.WriteLine($"PASS: {checks} standalone checks");



PetFinderChecks.Run(Check);
BlackMarketChecks.Run(Check, root, Identity, Snapshot, Frame);
NearbyChecks.Run(Check, root, Identity, Frame);
using (var abyssReward = new Engine(Path.Combine(root, "abyss-reward-independent")))
{
    abyssReward.Feed("abyss", Identity("Scout"));
    abyssReward.Feed("abyss", Snapshot(new()
    {
        [1029] = 1,
        [1098] = 2
    }, new()
    {
        [1029] = 8,
        [1098] = 10
    }));
    abyssReward.Feed("abyss", Points(0x0d, (1029, 1), (1098, 1)));
    Check(abyssReward.Tracker.Profile.Pets[1029].CurrentSouls == 9 && abyssReward.Tracker.Profile.Pets[1098].CurrentSouls == 11, "Klaw Scout and Modified Karnif rewards update without spawn or kill evidence");
    Check(abyssReward.Tracker.CountedKills == 0 && abyssReward.View(true, 1, 0, 0, 0, true).Nearby!.Count == 0, "Receiving souls does not invent Abyss mob sightings or kills");
}
FramingChecks.Run(Check);
var preferences = new AppSettings { Scale = 1.5, Opacity = .5, PinnedPetId = 1050, Adapter = "private-adapter", GameProcesses = "custom-game", Left = 123, HideWhenEmpty = true, InactiveSeconds = 60 };
var appearanceReset = SettingsPatch.Apply(preferences, SettingsSections.Defaults("Appearance"));
Check(appearanceReset.Scale == 1 && appearanceReset.Opacity == .95 && appearanceReset.IsPinned(1050) && appearanceReset.Adapter == "private-adapter" && appearanceReset.Left == 123 && appearanceReset.HideWhenEmpty && appearanceReset.InactiveSeconds == 60, "Appearance reset restores its preferences without touching tracking, capture or pinned goals");
Check(!JsonSerializer.Deserialize<AppSettings>("{\"Opacity\":0.6,\"InactiveSeconds\":30}")!.HideWhenEmpty, "Existing settings keep the overlay visible when empty by default");
Check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(preferences))!.HideWhenEmpty, "Hide-when-empty preference persists across restart");

DirectionChecks.Run(Check);
SkillNavigationChecks.Run(Check, root);
CaptureRecoveryChecks.Run(Check);
AdapterRecoveryChecks.Run(Check, root);
UninstallChecks.Run(Check, root);
RecoveryAccessChecks.Run(Check, root);
StartupCommandChecks.Run(Check);
using (var snapshotSave = new Engine(Path.Combine(root, "snapshot-save-failure")))
{
    snapshotSave.Execute(new("create", Name: "Encore", AccountName: "My account"));
    snapshotSave.Feed("game", Identity("Encore"));
    snapshotSave.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    snapshotSave.Flush();
    string saved = Path.Combine(root, "snapshot-save-failure", "profiles", snapshotSave.Book.SelectedProfileKey + ".json");
    using (var locked = File.Open(saved, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool interrupted = false;
        try
        {
            snapshotSave.Feed("game", Snapshot(new()
            {
                [1066] = 1
            }, new()
            {
                [1066] = 10
            }).Concat(Points(0xd, (1066, 1))).ToArray());
        }
        catch (IOException)
        {
            interrupted = true;
        }
        Check(!interrupted && snapshotSave.Tracker.Profile.Pets[1066].CurrentSouls == 11 && !snapshotSave.Tracker.Profile.Pets[1066].ProgressNeedsSync, "A locked snapshot save cannot interrupt the following reward in the same captured payload");
        Check(JsonSerializer.Deserialize<Profile>(File.ReadAllText(saved))!.Pets[1066].CurrentSouls == 7 && snapshotSave.View(true, 1, 0, 0, 0, true).Detail.Contains("could not be saved", StringComparison.OrdinalIgnoreCase), "Snapshot save failure retains the old file and reports persistence trouble");
        snapshotSave.Feed("game", Points(0xd, (1066, 2)));
        Check(snapshotSave.Tracker.Profile.Pets[1066].CurrentSouls == 13, "Later rewards continue exactly once despite a temporary snapshot persistence failure");
    }
    snapshotSave.Flush();
    snapshotSave.MaintenanceResult(null);
    Check(JsonSerializer.Deserialize<Profile>(File.ReadAllText(saved))!.Pets[1066].CurrentSouls == 13 && snapshotSave.View(true, 1, 0, 0, 0, true).Health!.DecoderFaults == 0, "Retry saves the complete reward total without inventing a decoder fault");
}
using (var settingsWrite = new Engine(Path.Combine(root, "settings-save-failure")))
{
    settingsWrite.Execute(new("settings", Settings: new()
    {
        Accent = "Teal",
        Locked = true,
        ClickThrough = true
    }));
    string saved = Path.Combine(root, "settings-save-failure", "settings.json");
    using (var locked = File.Open(saved, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool refused = false;
        try
        {
            settingsWrite.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Locked), false), (nameof(AppSettings.ClickThrough), false), (nameof(AppSettings.StartWithWindows), true))));
        }
        catch (IOException)
        {
            refused = true;
        }
        var disk = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(saved))!;
        Check(refused && settingsWrite.Settings.Locked && settingsWrite.Settings.ClickThrough && !settingsWrite.Settings.StartWithWindows, "Failed settings persistence leaves worker startup and interaction preferences unchanged");
        Check(disk.Locked && disk.ClickThrough && !disk.StartWithWindows && disk.Accent == "Teal", "A locked settings file retains its complete previous configuration");
    }
    settingsWrite.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.Locked), false), (nameof(AppSettings.ClickThrough), false))));
    var retried = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(saved))!;
    Check(!retried.Locked && !retried.ClickThrough && retried.Accent == "Teal", "An interaction patch succeeds after the lock clears without resetting other preferences");
}
using (var quitting = new Engine(Path.Combine(root, "quit-save")))
{
    quitting.Execute(new("create", Name: "Encore", AccountName: "My account"));
    quitting.Feed("game", Identity("Encore"));
    quitting.Feed("game", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    quitting.Flush();
    string saved = Path.Combine(root, "quit-save", "profiles", quitting.Book.SelectedProfileKey + ".json");
    quitting.Feed("game", Points(0xd, (1066, 1)));
    using (var locked = File.Open(saved, FileMode.Open, FileAccess.Read, FileShare.Read))
    {
        bool refused = false;
        try
        {
            quitting.Execute(new("exit"));
        }
        catch (IOException)
        {
            refused = true;
        }
        Check(refused && !quitting.ExitRequested, "A failed collection save leaves the worker available instead of acknowledging exit");
        Check(JsonSerializer.Deserialize<Profile>(File.ReadAllText(saved))!.Pets[1066].CurrentSouls == 7, "A rejected exit preserves the previous complete on-disk collection");
    }
    quitting.Execute(new("exit"));
    Check(quitting.ExitRequested && JsonSerializer.Deserialize<Profile>(File.ReadAllText(saved))!.Pets[1066].CurrentSouls == 8, "Retrying exit after the file unlocks saves the pending reward before acknowledging shutdown");
}
using (var engine = new Engine(Path.Combine(root, "capture-retry")))
{
    engine.Execute(new("create", Name: "Encore", AccountName: "My account"));
    engine.Feed("old", Identity("Encore"));
    engine.Feed("old", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 7
    }));
    bool running = true;
    long clock = 0;
    int restarts = 0;
    var recovery = new CaptureRecovery(() => running, () => engine.StopCapture("Capture stopped unexpectedly"), () =>
    {
        restarts++;
        running = true;
    }, () => clock);
    recovery.Tick();
    running = false;
    recovery.Tick();
    Check(!engine.Tracker.Ready && engine.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Capture reader failure immediately invalidates the previously live soul balance");
    clock = 5000;
    recovery.Tick();
    recovery.Tick();
    engine.Feed("new", Identity("Encore"));
    engine.Feed("new", Points(0xd, (1066, 1)));
    Check(restarts == 1 && engine.Tracker.Profile.Pets[1066].CurrentSouls == 7 && engine.Tracker.Profile.Pets[1066].ProgressNeedsSync, "Reopening capture does not treat an old collection plus an unbased reward as live");
    engine.Feed("new", Snapshot(new()
    {
        [1066] = 1
    }, new()
    {
        [1066] = 9
    }));
    engine.Feed("new", Points(0xd, (1066, 1)));
    Check(engine.Tracker.Profile.Pets[1066].CurrentSouls == 10 && !engine.Tracker.Profile.Pets[1066].ProgressNeedsSync, "A fresh snapshot after automatic capture recovery resumes exact reward accounting");
}
AccountMutationChecks.Run(Check, root, (engine, flow, name) => engine.Feed(flow, Identity(name)), (engine, flow, total) => engine.Feed(flow, Snapshot(new() { [1066] = 1 }, new() { [1066] = total })), (engine, flow) => engine.Feed(flow, Points(0xd, (1066, 1))));
MinimapRegistrationChecks.Run(Check);
RecoveryIdentityChecks.Run(root, Check);
GameConnectionChecks.Run(Check);
TcpConnectionTableChecks.Run(Check);
PetPinChecks.Run(Check, root);
Console.WriteLine($"PASS including finder: {checks} core checks");
