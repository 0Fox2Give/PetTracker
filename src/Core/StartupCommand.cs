namespace PetTracker;

public static class StartupCommand
{
    public static bool IsLegacyEntry(string command)
    {
        if (!command.StartsWith('"'))
            return false;
        int end = command.IndexOf('"', 1);
        if (end < 2)
            return false;
        string executable = command[1..end];
        return Path.IsPathFullyQualified(executable) && Path.GetFileName(executable).Equals("PetTracker.exe", StringComparison.OrdinalIgnoreCase)
         && command[(end + 1)..].StartsWith(" --minimized --data \"", StringComparison.Ordinal) && command.EndsWith('"');
    }
    public static string Build(string executable, string dataRoot) => Quote(executable) + " --minimized --data " + Quote(dataRoot);
    static string Quote(string value)
    {
        var result = new System.Text.StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character);
            slashes = 0;
        }
        // Backslashes immediately before a closing quote must be doubled, otherwise
        // Windows consumes the quote as part of the argument instead of its delimiter.
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
