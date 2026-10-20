using System.IO;

namespace PetTracker.Setup;
public static class UninstallPayload
{
    // Move the complete owned payload before deleting anything. A locked file
    // during staging restores earlier files instead of leaving half an app.
    // Returns a leftover staging folder only if Windows refuses final cleanup.
    public static string? Remove(string location, IEnumerable<string> names)
    {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location));
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name) || name.Contains(':'))
                throw new InvalidDataException("Invalid installed file path.");
            string path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !files.Add(path))
                throw new InvalidDataException("Invalid or duplicate installed file path.");
            // Never follow a junction/symlink out of the installation, including one
            // substituted for an owned subdirectory since installation.
            for (string? part = path; part != null; part = Path.GetDirectoryName(part))
            {
                if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("The installation contains a linked path.");
            }

            if (Directory.Exists(path))
                throw new InvalidDataException("An installed file was replaced by a folder.");
        }

        string stage = Path.GetFullPath(Path.Combine(root, ".uninstall-" + Guid.NewGuid().ToString("N")));
        if (!stage.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid uninstall staging path.");
        Directory.CreateDirectory(stage);
        var moved = new List<(string Original, string Staged)>();
        try
        {
            foreach (string file in files)
            {
                if (!File.Exists(file))
                    continue;
                string target = Path.Combine(stage, moved.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                File.Move(file, target);
                moved.Add((file, target));
            }
        }
        catch (Exception failure)
        {
            bool restored = true;
            foreach (var file in moved.AsEnumerable().Reverse())
                try
                {
                    File.Move(file.Staged, file.Original);
                }
                catch
                {
                    restored = false;
                }

            if (restored)
            {
                Directory.Delete(stage);
                throw new IOException("Pet Tracker could not be removed. Its files were restored. Close Pet Tracker and try again.", failure);
            }

            throw new IOException("Pet Tracker could not be removed completely. Original files are preserved in " + stage + ". Reinstall Pet Tracker before trying again.", failure);
        }

        // Installation removal has committed. Cleanup failures leave only staged
        // program files; unknown files and local collections are never enumerated.
        bool clean = true;
        foreach (var file in moved)
            try
            {
                File.Delete(file.Staged);
            }
            catch (IOException)
            {
                clean = false;
            }
            catch (UnauthorizedAccessException)
            {
                clean = false;
            }

        if (clean)
            try
            {
                Directory.Delete(stage);
            }
            catch (IOException)
            {
                clean = false;
            }
            catch (UnauthorizedAccessException)
            {
                clean = false;
            }

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
            for (string? folder = Path.GetDirectoryName(file); folder != null && !folder.Equals(root, StringComparison.OrdinalIgnoreCase); folder = Path.GetDirectoryName(folder))
                folders.Add(folder);
        foreach (string folder in folders.OrderByDescending(p => p.Length))
            RemoveEmpty(folder);
        RemoveEmpty(root);
        return clean ? null : stage;
    }

    static void RemoveEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
