using System.Globalization;
using System.Net;

namespace PetTracker;

/// <summary>Admits only sockets owned by a configured game process in our network namespace.</summary>
public static class LinuxGameConnections
{
    public static GameConnectionScan Scan(string processNames, string procRoot = "/proc")
    {
        var owners = new HashSet<int>();
        var inodes = new HashSet<ulong>();
        foreach (string directory in Directory.EnumerateDirectories(procRoot))
        {
            if (!int.TryParse(Path.GetFileName(directory), out int pid))
                continue;
            try
            {
                string command = File.ReadAllText(Path.Combine(directory, "cmdline"));
                if (!MatchesProcess(command, processNames))
                    continue;
                owners.Add(pid);
                // Do not fall back to all TCP connections if ownership is unavailable.
                foreach (string fd in Directory.EnumerateFiles(Path.Combine(directory, "fd")))
                {
                    string? target = new FileInfo(fd).LinkTarget;
                    if (target != null && target.StartsWith("socket:[", StringComparison.Ordinal) && target.EndsWith(']') && ulong.TryParse(target.AsSpan(8, target.Length - 9), out ulong inode))
                        inodes.Add(inode);
                }
            }
            catch (DirectoryNotFoundException)
            {
            } // Process exited during the scan.
            catch (FileNotFoundException)
            {
            }
            catch (UnauthorizedAccessException) when (!owners.Contains(pid))
            {
            } // Another user's process.
        }

        if (inodes.Count == 0)
            return new(owners, Array.Empty<GameConnectionPeer>());
        var peers = new List<GameConnectionPeer>();
        foreach (string table in new[] { "tcp", "tcp6" })
        {
            string path = Path.Combine(procRoot, "net", table);
            // IPv6 can be disabled on the host. IPv4 is required.
            if (table == "tcp6" && !File.Exists(path))
                continue;
            peers.AddRange(ParseTable(File.ReadLines(path), inodes));
        }

        return new(owners, peers);
    }

    public static bool MatchesProcess(string commandLine, string processNames)
    {
        string[] args = commandLine.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0)
            return false;
        string executable = Basename(args[0]);
        // Wine can retain the loader as argv[0]. Never search arbitrary arguments:
        // a browser/editor mentioning AION2.exe is not the game process.
        if (executable is "wine" or "wine64" or "wine-preloader" or "wine64-preloader")
            executable = args.Length > 1 ? Basename(args[1]) : "";
        return processNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(name => string.Equals(executable, Basename(name), StringComparison.OrdinalIgnoreCase));
    }

    static string Basename(string path)
    {
        string name = path.Replace('\\', '/').Split('/')[^1];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    public static IReadOnlyList<GameConnectionPeer> ParseTable(IEnumerable<string> lines, IReadOnlySet<ulong> inodes)
    {
        var peers = new List<GameConnectionPeer>();
        foreach (string line in lines.Skip(1))
        {
            string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 10 || !ulong.TryParse(fields[9], out ulong inode) || !inodes.Contains(inode))
                continue;
            // ESTABLISHED or SYN_SENT: include a connecting game socket before
            // the first server payload, but never listen or expired TIME_WAIT rows.
            if (fields[3] is not ("01" or "02"))
                continue;
            var local = ParseEndpoint(fields[1]);
            var remote = ParseEndpoint(fields[2]);
            peers.Add(new(remote.Address, remote.Port, local.Address, local.Port));
        }

        return peers;
    }

    static (string Address, int Port) ParseEndpoint(string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length != 2 || parts[0].Length is not (8 or 32) || !ushort.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort port))
            throw new InvalidDataException("Invalid Linux TCP endpoint.");
        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(parts[0]);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException("Invalid Linux TCP address.", ex);
        }

        // /proc encodes each 32-bit address word in host byte order, including IPv6.
        if (BitConverter.IsLittleEndian)
            for (int i = 0; i < bytes.Length; i += 4)
                Array.Reverse(bytes, i, 4);
        return (new IPAddress(bytes).ToString(), port);
    }
}
