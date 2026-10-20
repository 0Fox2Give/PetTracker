using PetTracker.Setup;

static class UninstallChecks
{
    public static void Run(Action<bool, string> check, string root)
    {
        string fixture = Path.Combine(root, "uninstall-rollback");
        Directory.CreateDirectory(fixture);
        string first = Path.Combine(fixture, "app.dll"), locked = Path.Combine(fixture, "worker.dll"), custom = Path.Combine(fixture, "my-notes.txt");
        File.WriteAllText(first, "original app");
        File.WriteAllText(locked, "original worker");
        File.WriteAllText(custom, "player notes");
        using (var handle = File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool refused = false;
            try
            {
                UninstallPayload.Remove(fixture, new[] { "app.dll", "worker.dll" });
            }
            catch (IOException)
            {
                refused = true;
            }
            check(refused, "Locked installed files stop uninstallation");
            check(File.Exists(first) && File.ReadAllText(first) == "original app" && File.ReadAllText(locked) == "original worker", "A late locked file restores all earlier staged program files");
            check(File.ReadAllText(custom) == "player notes" && Directory.GetDirectories(fixture, ".uninstall-*").Length == 0, "Rollback preserves unknown files and removes its empty staging folder");
        }
        string? remaining = UninstallPayload.Remove(fixture, new[] { "app.dll", "worker.dll", "already-missing.dll" });
        check(remaining == null && !File.Exists(first) && !File.Exists(locked) && File.ReadAllText(custom) == "player notes", "Successful uninstall removes only owned files and tolerates already missing files");
        string invalid = Path.Combine(root, "uninstall-invalid");
        Directory.CreateDirectory(invalid);
        string validFile = Path.Combine(invalid, "app.dll");
        File.WriteAllText(validFile, "retain");
        foreach (string[] entries in new[] { new[] { "app.dll", "../outside.dll" }, new[] { "app.dll", "APP.dll" }, new[] { "app.dll", "app.dll:stream" } })
        {
            bool refused = false;
            try
            {
                UninstallPayload.Remove(invalid, entries);
            }
            catch (InvalidDataException)
            {
                refused = true;
            }
            check(refused && File.ReadAllText(validFile) == "retain", "All uninstall paths are validated before moving the first file");
        }
        string nested = Path.Combine(root, "uninstall-nested");
        Directory.CreateDirectory(Path.Combine(nested, "portraits"));
        File.WriteAllText(Path.Combine(nested, "portraits", "pet.png"), "portrait");
        UninstallPayload.Remove(nested, new[] { "portraits/pet.png" });
        check(!Directory.Exists(nested), "Empty owned subdirectories and install folder are removed after commit");
        string blocked = Path.Combine(root, "uninstall-cleanup");
        Directory.CreateDirectory(blocked);
        string readOnly = Path.Combine(blocked, "app.dll");
        File.WriteAllText(readOnly, "program");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        string? leftovers = UninstallPayload.Remove(blocked, new[] { "app.dll" });
        check(leftovers != null && !File.Exists(readOnly) && File.ReadAllText(Path.Combine(leftovers, "0")) == "program", "Cleanup refusal retains the staged program file and reports its location");
        File.SetAttributes(Path.Combine(leftovers!, "0"), FileAttributes.Normal);
    }
}
