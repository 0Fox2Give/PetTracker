using System.Buffers.Binary;
using PetTracker;

static class SkillNavigationChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        var now = DateTimeOffset.UtcNow;
        byte[] buff = Convert.FromHexString("F12E0111850D0000000000000000000000000000000000000000F12E01005907534740121AC700849F46");
        byte[] cast = Convert.FromHexString("F12E00702CB800A90283A30306A3B8C2440053472DC11AC700C69F468B5C01FCD901");
        byte[] spawn = Convert.FromHexString("A894030C2000010D20000002892D8246925486C60026A6460040BF42");
        var parsed = NavigationProtocol.SelfBuff(buff);
        var skill = NavigationProtocol.Cast(cast);
        check(parsed is { Entity: 6001, Point.X: > 54023 and < 54024, Point.Y: < -39442 }, "Sanitized self-buff yields recipient location");
        byte[] tick = Convert.FromHexString("F32E13290000000000000000000000000000000000000000F32E0124E2E400029C7116470600A1470072E946");
        check(NavigationProtocol.SelfBuff(tick, true) is { Entity: 6003, Point.X: > 38513 and < 38514, Point.Y: > 82432 and < 82433 }, "Sanitized periodic self-buff decodes a refreshed position");
        check(NavigationProtocol.SelfBuff(tick) == null && NavigationProtocol.SelfBuff(buff, true) == null, "Initial and periodic buff forms cannot be confused");
        check(NavigationProtocol.SelfBuff(tick[..^1], true) == null, "Truncated periodic buff position is rejected");
        var actionPose = Convert.FromHexString("F12E03DE097E478BFA40C7007C8346F82A53C2000001702CB800CB5BF1472A");
        check(NavigationProtocol.ActionPose(actionPose) is { Entity: 6001, Point.X: > 65033 and < 65034, Point.Y: < -49402, Degrees: > -52.8 and < -52.7 }, "Sanitized action pose supplies actor coordinates and facing together");
        check(NavigationProtocol.ActionPose(actionPose[..^1]) == null && NavigationProtocol.ActionPose(actionPose.Concat(new byte[] { 0 }).ToArray()) == null, "Action pose requires its complete exact layout");
        var unsupportedPose = actionPose.ToArray();
        unsupportedPose[2] = 0;
        check(NavigationProtocol.ActionPose(unsupportedPose) == null, "Unsupported short action form cannot be mistaken for coordinates");
        var badPose = actionPose.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(badPose.AsSpan(15), float.NaN);
        check(NavigationProtocol.ActionPose(badPose) == null, "Nonfinite action heading is rejected");
        check(skill is { Actor: 6001, Degrees: < -92 and > -93, TargetPoint.X: > 54016 and < 54017 }, "Sanitized skill yields facing and target coordinates separately");
        check(NavigationProtocol.SelfBuff(buff[..^1]) == null && NavigationProtocol.Cast(cast[..^1]) == null, "Truncated navigation messages rejected");
        check(NavigationProtocol.Cast(cast.Concat(new byte[] { 0 }).ToArray()) == null, "Extra cast payload cannot shift the admitted layout");
        var many = buff.ToArray();
        many[2] = 2;
        check(NavigationProtocol.SelfBuff(many) == null, "Unparsed multi-buff layout rejected");
        var other = buff.ToArray();
        other[26] = 0xf6;
        check(NavigationProtocol.SelfBuff(other) == null, "Other actor buff is not a self-position source");
        var nan = buff.ToArray();
        BinaryPrimitives.WriteSingleLittleEndian(nan.AsSpan(nan.Length - 12), float.NaN);
        check(NavigationProtocol.SelfBuff(nan) == null, "Nonfinite player location rejected");
        var mappings = new CreaturePetMappings("", Catalog.Create());
        var nav = new SkillNavigation(mappings);
        int entity = GameProtocol.Appearance(0x41, 0x36, spawn)!.Entity;
        int pet = mappings.Find(2100481)!.PetId;
        byte[] Var(int value)
        {
            var b = new List<byte>();
            do
            {
                byte v = (byte)(value & 127);
                value >>= 7;
                b.Add((byte)(v | (value > 0 ? 128 : 0)));
            } while (value > 0);
            return b.ToArray();
        }
        byte[] Pose(byte[] template, int offset, float x, float y, float z = 100)
        {
            var b = template.ToArray();
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(offset), x);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(offset + 4), y);
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(offset + 8), z);
            return b;
        }
        var origin = Pose(buff, buff.Length - 12, 10000, 10000);
        var ahead = Pose(spawn, 12, 11000, 10000);
        // Short, sanitized self-skill form. Angle follows the world XY convention.
        var heading = Convert.FromHexString("F12E00923EC200A600F12E0D135F438B5C0100");
        BinaryPrimitives.WriteSingleLittleEndian(heading.AsSpan(11), 0);
        void Ready()
        {
            nav.Clear();
            nav.Frame(6001, 0x41, 0x36, ahead, now);
            nav.Frame(6001, 0x2a, 0x38, origin, now);
            nav.Frame(6001, 2, 0x38, heading, now);
        }
        var actionOrigin = Pose(actionPose, 3, 10000, 10000);
        BinaryPrimitives.WriteSingleLittleEndian(actionOrigin.AsSpan(15), 0);
        nav.Frame(6001, 0x41, 0x36, ahead, now);
        nav.Frame(6001, 0x3c, 0x37, actionOrigin, now);
        check(nav.View(now).TryGetValue(pet, out var actionArrow) && actionArrow.Bearing(now) == 90, "Action pose produces a standalone arrow without requiring a self-buff");
        nav.Frame(6001, 0x3c, 0x37, unsupportedPose, now);
        check(nav.View(now).Count == 0, "Unparsed own action pose invalidates prior guidance");
        Ready();
        var otherPose = actionOrigin.ToArray();
        otherPose[0]--;
        nav.Frame(6001, 0x3c, 0x37, otherPose, now.AddSeconds(1));
        check(nav.Health(now.AddSeconds(1)).PositionAgeMs == 1000, "Another actor's action pose cannot refresh the player");
        Ready();
        check(nav.View(now).TryGetValue(pet, out var arrow) && arrow.Bearing(now) == 90 && !arrow.HasDistance(now) && arrow.NorthUp, "Standalone observed pose feeds an east arrow without invented metres");
        nav.Frame(6001, 0x41, 0x36, Pose(spawn, 12, 10000, 11000), now);
        check(nav.View(now)[pet].Bearing(now) == 180, "Positive world Y is south on the map");
        BinaryPrimitives.WriteSingleLittleEndian(heading.AsSpan(11), 90);
        nav.Frame(6001, 2, 0x38, heading, now);
        check(nav.View(now)[pet].Bearing(now) == 180, "Changing skill facing does not rotate a map arrow");
        check(nav.View(now.AddSeconds(2)).Count == 0 && nav.View(now.AddTicks(-1)).Count == 0, "Navigation hides expired and future evidence");
        nav.Clear();
        nav.Frame(6001, 0x41, 0x36, ahead, now);
        nav.Frame(6001, 0x2a, 0x38, origin, now);
        check(nav.View(now)[pet].Bearing(now) == 90 && nav.Health(now).FacingAgeMs == null, "Fresh positions produce a map arrow without any heading evidence");
        foreach (var point in new[] { (10000f, 9000f, 0d), (11000f, 10000f, 90d), (10000f, 11000f, 180d), (9000f, 10000f, 270d) })
        {
            nav.Frame(6001, 0x41, 0x36, Pose(spawn, 12, point.Item1, point.Item2), now);
            check(nav.View(now)[pet].Bearing(now) == point.Item3, "Map axes produce the correct cardinal direction");
        }
        Ready();
        nav.Frame(6001, 0x41, 0x36, ahead, now.AddSeconds(3));
        nav.Frame(6001, 0x2a, 0x38, origin, now.AddSeconds(3));
        check(nav.View(now.AddSeconds(3))[pet].Bearing(now.AddSeconds(3)) == 90, "Fresh positions still work after heading expires");
        Ready();
        nav.Frame(6001, 0x1d, 0x37, Var(6001).Concat(new byte[] { 1 }).ToArray(), now);
        check(nav.View(now).Count == 0, "Unresolved local movement invalidates position and heading");
        Ready();
        nav.Frame(6001, 0x23, 0x36, new byte[] { 0 }, now);
        check(nav.View(now).Count == 0, "Zone change clears directions");
        Ready();
        var tickFixture = Convert.FromHexString("F12E11860D0000000000000000000000000000000000000000F12E01025C9453470F0419C7D6619F46");
        nav.Frame(6001, 0x2b, 0x38, tickFixture, now.AddSeconds(3));
        check(nav.Health(now.AddSeconds(3)).PositionAgeMs == 0, "Periodic self-buff refreshes an expired player position");
        Ready();
        nav.Frame(6001, 2, 0x38, heading[..^1], now);
        check(nav.View(now).Count == 0, "Unsupported own cast invalidates the previous heading");
        Ready();
        nav.Frame(6002, 0, 0, Array.Empty<byte>(), now);
        check(nav.View(now).Count == 0, "Character change cannot inherit another character's arrow");
        Ready();
        nav.Frame(6001, 0x42, 0x36, Var(entity).Concat(new byte[] { 0, 3 }).ToArray(), now);
        check(nav.View(now).Count == 0, "Dead or removed mob loses its arrow");
        Ready();
        var dodge = Convert.FromHexString("F12E00651BB700A302F12E897688C2284811428557BAC200000000904E01F403");
        nav.Frame(6001, 2, 0x38, dodge, now);
        check(nav.View(now).Count == 0, "Sanitized dodge vector never becomes a world location");
        // Reduced buff and dodge regression vectors. Actor 6004
        // is a fixture attribution; production continues to require explicit identity.
        var liveDodge = Convert.FromHexString("F42E0025E2E4002802F42E4066EDC2BCE76CC22126A1C200000000904E01F403");
        var liveBuff = Convert.FromHexString("F42E0111180000000000000000000000000000000000000000F42E0100B8660E47AA0B9F4700F8EB46");
        var liveCast = NavigationProtocol.Cast(liveDodge);
        var livePosition = NavigationProtocol.SelfBuff(liveBuff);
        check(liveCast is { Actor: 6004, Degrees: > -118.71 and < -118.69, TargetPoint.X: > -60 and < -59 } && livePosition is { Entity: 6004, Point.X: > 36454 and < 36455 }, "Controlled dodge separates action yaw and displacement from self-buff absolute coordinates");
        void DodgeReady()
        {
            nav.Clear();
            nav.Frame(6004, 0x41, 0x36, Pose(spawn, 12, 37454.71875f, 81431.328125f), now);
            nav.Frame(6004, 2, 0x38, liveDodge, now);
        }
        DodgeReady();
        check(nav.Health(now).PositionAgeMs == null && nav.View(now).Count == 0, "Dodge cannot show an arrow before an independent absolute position arrives");
        nav.Frame(6004, 0x2a, 0x38, liveBuff, now.AddMilliseconds(1));
        check(nav.View(now.AddMilliseconds(1)).TryGetValue(pet, out var dodgeArrow) && Math.Abs(dodgeArrow.Bearing(now.AddMilliseconds(1))!.Value - 90) < 0.001, "Self-buff restores the east map bearing independently of dodge yaw");
        check(nav.View(now.AddSeconds(2)).Count == 0, "Target position still expires after two seconds");
        DodgeReady();
        nav.Frame(6004, 0x2a, 0x38, buff, now.AddMilliseconds(1));
        check(nav.View(now.AddMilliseconds(1)).Count == 0, "Another actor's buff cannot restore dodge guidance");
        DodgeReady();
        nav.Frame(6004, 0x1d, 0x37, Var(6004).Concat(new byte[] { 1 }).ToArray(), now.AddMilliseconds(1));
        nav.Frame(6004, 0x2a, 0x38, liveBuff, now.AddMilliseconds(2));
        check(nav.View(now.AddMilliseconds(2)).Count == 1, "Independent fresh position restores map guidance without a facing update");
        Ready();
        nav.Frame(6001, 0x1d, 0x37, Var(entity).Concat(new byte[] { 1 }).ToArray(), now);
        check(nav.View(now).Count == 0, "Unknown target movement hides its old position");
        Ready();
        var duplicate = spawn.ToArray();
        duplicate[0]++;
        nav.Frame(6001, 0x41, 0x36, duplicate, now.AddSeconds(-3));
        check(nav.View(now).Count == 0, "Stale competing instance blocks a misleading nearest-pet arrow");
        var walk = Var(entity).Concat(new byte[] { 4 }).Concat(ahead.AsSpan(12, 12).ToArray()).Concat(new byte[] { 0, 0, 0, 0, 1 }).ToArray();
        var stop = Var(entity).Concat(new byte[] { 0, 0 }).Concat(ahead.AsSpan(12, 12).ToArray()).Concat(new byte[] { 0, 0 }).ToArray();
        check(NavigationProtocol.NpcWalk(0x1b, walk) && NavigationProtocol.NpcStop(0x1a, stop), "Exact NPC walk and stop forms recognized");
        Ready();
        var localWalk = Var(6001).Concat(walk.AsSpan(Var(entity).Length).ToArray()).ToArray();
        nav.Frame(6001, 0x1b, 0x37, localWalk, now);
        check(nav.View(now).Count == 0, "Removing heading dependency cannot admit an unverified local movement position");
        check(!NavigationProtocol.NpcStop(0x1a, stop[..^1]) && !NavigationProtocol.NpcStop(0x1a, stop.Concat(new byte[] { 0 }).ToArray()), "Stationary proof rejects truncated and extended stop layouts");
        void KeepStream()
        {
            for (int i = 1; i <= 3; i++)
                nav.Frame(6001, 0, 0, Array.Empty<byte>(), now.AddMilliseconds(900 * i));
            nav.Frame(6001, 0x2a, 0x38, origin, now.AddSeconds(3));
            nav.Frame(6001, 2, 0x38, heading, now.AddSeconds(3));
        }
        void StoppedReady()
        {
            Ready();
            nav.Frame(6001, 0x1b, 0x37, walk, now);
            nav.Frame(6001, 0x1a, 0x37, stop, now);
            KeepStream();
        }
        StoppedReady();
        check(nav.View(now.AddSeconds(3)).TryGetValue(pet, out var stoppedArrow) && stoppedArrow.Bearing(now.AddSeconds(3)) != null, "Confirmed stopped NPC remains usable with continuous traffic and fresh player pose");
        check(nav.View(now.AddSeconds(5)).Count == 0, "Stopped NPC never extends the player's heading or position lifetime");
        Ready();
        KeepStream();
        check(nav.View(now.AddSeconds(3)).Count == 0, "A silent spawn is not proof of a stopped NPC");
        Ready();
        nav.Frame(6001, 0x1a, 0x37, stop, now);
        KeepStream();
        check(nav.View(now.AddSeconds(3)).Count == 0, "Stop without observed NPC walking cannot acquire stationary trust");
        StoppedReady();
        nav.Frame(6001, 0x1d, 0x37, Var(entity).Concat(new byte[] { 1 }).ToArray(), now.AddSeconds(3.1));
        check(nav.View(now.AddSeconds(3.1)).Count == 0, "Resumed compact movement invalidates the stopped position immediately");
        StoppedReady();
        nav.Frame(6001, 0x14, 0x38, Var(entity), now.AddSeconds(3.1));
        check(nav.View(now.AddSeconds(3.1)).Count == 0, "Combat displacement invalidates stationary guidance even without a decoded destination");
        StoppedReady();
        nav.Frame(6001, 0x2f, 0x37, Var(entity), now.AddSeconds(3.1));
        check(nav.View(now.AddSeconds(3.1)).Count == 0, "Unparsed trajectory update invalidates stationary guidance");
        StoppedReady();
        nav.Frame(6001, 0x2a, 0x38, origin, now.AddSeconds(6));
        nav.Frame(6001, 2, 0x38, heading, now.AddSeconds(6));
        check(nav.View(now.AddSeconds(6)).Count == 0, "Traffic resuming after a silence cannot resurrect an old stopped position");
        StoppedReady();
        nav.Frame(6001, 0x42, 0x36, Var(entity).Concat(new byte[] { 0, 3 }).ToArray(), now.AddSeconds(3.1));
        check(nav.View(now.AddSeconds(3.1)).Count == 0, "Despawn retires confirmed stopped NPC state");
        var world = new NearbyWorld(mappings);
        world.Observe(GameProtocol.Appearance(0x41, 0x36, ahead)!, now);
        world.NavigationFrame(true, 6001, 0x41, 0x36, ahead, now);
        world.NavigationFrame(true, 6001, 0x2a, 0x38, origin, now);
        world.NavigationFrame(true, 6001, 2, 0x38, heading, now);
        check(world.View(now, 120).Single().Direction == null && world.View(now, 120, true).Single().Direction != null, "Experimental setting gates the actual Nearby worker presentation");
        world.Clear();
        check(world.View(now, 120, true).Count == 0, "Capture reset clears sightings and navigation together");
        world.Observe(GameProtocol.Appearance(0x41, 0x36, ahead)!, now);
        check(world.View(now.AddSeconds(21), 20).Count == 0, "Inactive nearby row hides at the configured timeout");
        var moving = Var(entity).Concat(new byte[] { 0, 0 }).Concat(ahead.AsSpan(12, 12).ToArray()).ToArray();
        world.NavigationFrame(false, 6001, 0x1a, 0x37, moving, now.AddSeconds(22));
        check(world.View(now.AddSeconds(22), 20).Single().PetId == pet, "Observed movement restores an expired nearby row with arrows disabled");
        world.Remove(entity);
        world.NavigationFrame(true, 6001, 0x1a, 0x37, moving, now.AddSeconds(23));
        check(world.View(now.AddSeconds(23), 20, true).Count == 0, "Movement cannot resurrect a mob explicitly removed from Nearby");
        check(!new AppSettings().ExperimentalDirections, "Experimental navigation is disabled by default");
        string legacyRoot = Path.Combine(root, "legacy-navigation");
        Directory.CreateDirectory(legacyRoot);
        Storage.Save(Path.Combine(legacyRoot, "settings.json"), new AppSettings { ExperimentalDirections = true, TrackerMode = "Nearby", PinnedPetId = 1050, Scale = 1.25 });
        using (var legacy = new Engine(legacyRoot))
        {
            check(!legacy.Settings.ExperimentalDirections && legacy.Settings.TrackerMode == "Nearby" && legacy.Settings.IsPinned(1050) && legacy.Settings.Scale == 1.25, "Release disables persisted experimental arrows without changing other preferences");
        }
        byte[] Frame(byte op, byte family, byte[] body) => Var(body.Length + 6).Concat(new[] { op, family }).Concat(body).ToArray();
        using (var engine = new Engine(Path.Combine(root, "navigation-engine")))
        {
            engine.Execute(new("create", Name: "Ranger", AccountName: "Test account"));
            engine.Execute(new("patch", Patch: SettingsPatch.Fields((nameof(AppSettings.ExperimentalDirections), true))));
            byte[] identity = Var(6001).Concat(new byte[] { 0, 0, 0, 0, 7, 6 }).Concat(System.Text.Encoding.UTF8.GetBytes("Ranger")).Concat(new byte[] { 0, 0, 0, 0 }).ToArray();
            engine.Feed("primary", Frame(0x33, 0x36, identity));
            string collection = System.Text.Json.JsonSerializer.Serialize(engine.Tracker.Profile);
            byte[] sequence = Frame(0x41, 0x36, ahead).Concat(Frame(0x2a, 0x38, origin)).Concat(Frame(2, 0x38, heading)).ToArray();
            engine.Feed("background", sequence);
            check(engine.View(true, 1, 0, 0, 0, true).Nearby!.Count == 0, "Background TCP stream cannot produce navigation for active character");
            engine.Feed("primary", sequence);
            check(!engine.Settings.ExperimentalDirections && engine.View(true, 1, 0, 0, 0, true).Nearby!.Single().Direction == null, "Release rejects navigation opt-in patches even with valid position frames");
            check(System.Text.Json.JsonSerializer.Serialize(engine.Tracker.Profile) == collection, "Navigation does not change saved pet ranks or souls");
            engine.Gap("primary");
            check(engine.View(true, 1, 0, 0, 0, true).Nearby!.Count == 0, "Capture gap clears arrows through the real engine path");
        }
    }
}
